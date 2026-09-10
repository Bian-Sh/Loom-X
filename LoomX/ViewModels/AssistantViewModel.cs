using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using LoomX.Assistant;
using LoomX.Localization;
using LoomX.Services;
using Microsoft.Extensions.Logging;

namespace LoomX.ViewModels;

/// <summary>
/// 小助手聊天页 VM：消费 AgentEvent 流做 Activity Projection，不绑定 Harness 内部对象。
/// 助手服务复用网关容器中的配置与工具注册，但首次使用时可在不监听网关端口的状态下初始化。
/// 底部工具栏承载：修改权限（自动批准/逐条批准）、模型指定（只走 Provider 模型，不走 combo）、思考等级。
/// </summary>
public sealed class AssistantViewModel : NotifyViewModel
{
    private readonly GatewayProcessService gatewayService;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AssistantViewModel> logger;

    private string inputText = string.Empty;
    private string statusText = string.Empty;
    private bool isRunning;
    private AssistantSessionSummary? selectedSession;
    private ChatMessageViewModel? streamingMessage;
    private bool suppressSelectionLoad;
    private bool isModelPickerOpen;
    private string selectedModelSummary = string.Empty;
    private string modelSearchTerm = string.Empty;
    private AssistantPermissionOption selectedPermissionMode;
    private AssistantReasoningOption selectedReasoningEffort;
    private ApprovalRequestViewModel? pendingApproval;
    private IReadOnlyList<AssistantModelGroupViewModel> allModelGroups = [];

    public AssistantViewModel(GatewayProcessService gatewayService, ILoggerFactory? loggerFactory = null)
    {
        this.gatewayService = gatewayService;
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<AssistantViewModel>();

        SendCommand = new AsyncCommand(SendAsync, () => !IsRunning && !string.IsNullOrWhiteSpace(InputText));
        CancelCommand = new DelegateCommand(Cancel);
        NewSessionCommand = new DelegateCommand(NewSession);
        LoadSessionCommand = new AsyncCommand(parameter => LoadSessionAsync(parameter as AssistantSessionSummary));

        PermissionModeOptions =
        [
            new AssistantPermissionOption(AssistantPermissionMode.AutoApprove, ResourceLookup.Resolve("assistant.permission.auto")),
            new AssistantPermissionOption(AssistantPermissionMode.AskEachTime, ResourceLookup.Resolve("assistant.permission.ask")),
        ];
        ReasoningEffortOptions =
        [
            new AssistantReasoningOption(AssistantPreferences.DefaultReasoningEffort, ResourceLookup.Resolve("assistant.reasoning.default")),
            new AssistantReasoningOption("minimal", ResourceLookup.Resolve("assistant.reasoning.minimal")),
            new AssistantReasoningOption("low", ResourceLookup.Resolve("assistant.reasoning.low")),
            new AssistantReasoningOption("medium", ResourceLookup.Resolve("assistant.reasoning.medium")),
            new AssistantReasoningOption("high", ResourceLookup.Resolve("assistant.reasoning.high")),
        ];

        var service = ResolveService();
        selectedPermissionMode = PermissionModeOptions.First(item => item.Mode == (service?.PermissionMode ?? AssistantPermissionMode.AutoApprove));
        selectedReasoningEffort = ReasoningEffortOptions.First(item => item.Value == (service?.ReasoningEffort ?? AssistantPreferences.DefaultReasoningEffort));

        RefreshSessions();
        RefreshModelSummary();
        AddSystemMessage("我是 LoomX 小助手，可以帮你配置 Provider、诊断连通性、接入中转站。请选择模型后即可开始。");
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<AssistantSessionSummary> Sessions { get; } = [];

    /// <summary>模型选择弹层中的 Provider 分组（foldout）。</summary>
    public ObservableCollection<AssistantModelGroupViewModel> ModelGroups { get; } = [];

    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand LoadSessionCommand { get; }

    public IReadOnlyList<AssistantPermissionOption> PermissionModeOptions { get; }

    public IReadOnlyList<AssistantReasoningOption> ReasoningEffortOptions { get; }

    public string InputText
    {
        get => inputText;
        set
        {
            if (SetProperty(ref inputText, value)) (SendCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (SetProperty(ref isRunning, value)) (SendCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>历史会话下拉选择；选中即载入。</summary>
    public AssistantSessionSummary? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (!SetProperty(ref selectedSession, value)) return;
            if (value is not null && !suppressSelectionLoad)
            {
                _ = LoadSessionAsync(value);
            }
        }
    }

    /// <summary>修改权限模式；切换即持久化到助手偏好。</summary>
    public AssistantPermissionOption SelectedPermissionMode
    {
        get => selectedPermissionMode;
        set
        {
            if (!SetProperty(ref selectedPermissionMode, value)) return;
            ResolveService()?.PermissionMode = value.Mode;
        }
    }

    /// <summary>思考等级；切换即持久化到助手偏好。</summary>
    public AssistantReasoningOption SelectedReasoningEffort
    {
        get => selectedReasoningEffort;
        set
        {
            if (!SetProperty(ref selectedReasoningEffort, value)) return;
            ResolveService()?.ReasoningEffort = value.Value;
        }
    }

    /// <summary>模型选择弹层开关（双向绑定到 Popup）。</summary>
    public bool IsModelPickerOpen
    {
        get => isModelPickerOpen;
        set => SetProperty(ref isModelPickerOpen, value);
    }

    /// <summary>底部工具栏上展示的当前模型摘要。</summary>
    public string SelectedModelSummary
    {
        get => selectedModelSummary;
        private set => SetProperty(ref selectedModelSummary, value);
    }

    /// <summary>待批准的修改操作；null 时审批卡片隐藏。</summary>
    public ApprovalRequestViewModel? PendingApproval
    {
        get => pendingApproval;
        private set => SetProperty(ref pendingApproval, value);
    }

    /// <summary>模型弹层是否没有任何分组（空态提示）。</summary>
    public bool HasNoModelGroups => ModelGroups.Count == 0;

    private AssistantService? ResolveService() => gatewayService.GetHostedService<AssistantService>();

    private async Task<AssistantService?> EnsureServiceAsync(CancellationToken cancellationToken = default)
    {
        await gatewayService.EnsureHostedServicesAsync(cancellationToken);
        return ResolveService();
    }

    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0) return;

        InputText = string.Empty;
        Messages.Add(new ChatMessageViewModel(ChatRole.User, text));
        IsRunning = true;
        StatusText = "小助手正在工作…";
        AssistantService? service = null;

        try
        {
            service = await EnsureServiceAsync();
            if (service is null)
            {
                AddSystemMessage("小助手服务初始化失败，请查看控制台日志。");
                return;
            }

            service.ApprovalHandler = ShowApprovalAsync;
            await foreach (var agentEvent in service.SendAsync(text))
            {
                Dispatcher.UIThread.Post(() => Project(agentEvent));
            }
        }
        catch (InvalidOperationException exception)
        {
            Dispatcher.UIThread.Post(() => AddSystemMessage(exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "小助手运行失败");
            Dispatcher.UIThread.Post(() => AddSystemMessage("小助手运行失败，请查看控制台日志。"));
        }
        finally
        {
            if (service is not null) service.ApprovalHandler = null;
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                StatusText = string.Empty;
                streamingMessage = null;
                RefreshSessions();
            });
        }
    }

    /// <summary>AgentEvent → UI 投影（规格 #15：UI 只消费事件）。</summary>
    internal void Project(AgentEvent agentEvent)
    {
        switch (agentEvent.Kind)
        {
            case AgentEventKind.TextDelta:
                streamingMessage ??= AppendStreamingMessage();
                streamingMessage.Append(agentEvent.Text ?? string.Empty);
                break;

            case AgentEventKind.ToolCallStarted:
                streamingMessage = null;
                Messages.Add(ChatMessageViewModel.Status($"⚙ 调用工具 {agentEvent.ToolName}"));
                break;

            case AgentEventKind.ToolApprovalRequested:
                streamingMessage = null;
                Messages.Add(ChatMessageViewModel.Status($"✋ 等待你批准修改：{agentEvent.ToolName}"));
                break;

            case AgentEventKind.ToolCallCompleted:
                Messages.Add(agentEvent.Success == true
                    ? ChatMessageViewModel.Status($"✓ {agentEvent.ToolName} 完成")
                    : ChatMessageViewModel.Status($"✗ {agentEvent.ToolName} 失败：{agentEvent.Detail}"));
                break;

            case AgentEventKind.SkillLoaded:
                Messages.Add(ChatMessageViewModel.Status($"📖 已加载 Skill {agentEvent.Detail}"));
                break;

            case AgentEventKind.SubagentStarted:
                Messages.Add(ChatMessageViewModel.Status("🔍 诊断工人已出发"));
                break;

            case AgentEventKind.SubagentCompleted:
                Messages.Add(ChatMessageViewModel.Status($"🔍 诊断完成：{agentEvent.Detail}"));
                break;

            case AgentEventKind.TaskFailed:
                streamingMessage = null;
                // Detail 为 ModelErrorFormatter 组装的多行详情（本地化描述 + 状态码/错误码/上游描述）
                Messages.Add(ChatMessageViewModel.Status($"⚠ {ResourceLookup.Resolve("assistant.task.failed")}：\n{agentEvent.Detail}"));
                break;

            case AgentEventKind.WaitingForUser:
                Messages.Add(ChatMessageViewModel.Status($"✋ 等待你操作：{agentEvent.Detail}"));
                break;

            case AgentEventKind.TaskCompleted:
                streamingMessage = null;
                break;
        }
    }

    /// <summary>打开模型选择弹层：从服务端拉取可用模型并按 Provider 分组。</summary>
    public async Task OpenModelPickerAsync()
    {
        var service = await EnsureServiceAsync();
        if (service is null)
        {
            AddSystemMessage("小助手服务初始化失败，请查看控制台日志。");
            return;
        }

        var groups = await service.ListAvailableModelsAsync();
        var (preferredProvider, preferredModel) = service.GetModelSelection();
        var hasPreferredSelection = preferredProvider is not null && preferredModel is not null;
        var firstModel = groups.SelectMany(group => group.Models).FirstOrDefault();
        allModelGroups = groups
            .Select(group => new AssistantModelGroupViewModel(
                group.ProviderDisplayName,
                group.Models.Select(model => new AssistantModelOptionViewModel(
                    model.ProviderBusinessId,
                    model.ModelId,
                    model.DisplayName,
                    hasPreferredSelection
                        ? string.Equals(model.ProviderBusinessId, preferredProvider, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(model.ModelId, preferredModel, StringComparison.OrdinalIgnoreCase)
                        : firstModel is not null
                            && string.Equals(model.ProviderBusinessId, firstModel.ProviderBusinessId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(model.ModelId, firstModel.ModelId, StringComparison.OrdinalIgnoreCase))).ToArray()))
            .ToArray();
        ApplyModelFilter();
        IsModelPickerOpen = true;
    }

    public void FilterModels(string? search)
    {
        modelSearchTerm = search?.Trim() ?? string.Empty;
        ApplyModelFilter();
    }

    private void ApplyModelFilter()
    {
        ModelGroups.Clear();
        foreach (var group in allModelGroups)
        {
            var models = group.Models.Where(item => item.MatchesSearch(modelSearchTerm)).ToArray();
            if (models.Length == 0) continue;
            ModelGroups.Add(new AssistantModelGroupViewModel(group.ProviderName, models)
            {
                IsExpanded = group.IsExpanded || modelSearchTerm.Length > 0,
            });
        }

        OnPropertyChanged(nameof(HasNoModelGroups));
    }

    /// <summary>选定模型（只走 Provider 模型，不走 combo）；持久化并关闭弹层。</summary>
    public void SelectModel(AssistantModelOptionViewModel option)
    {
        var service = ResolveService();
        if (service is null) return;

        service.SelectModel(option.ProviderBusinessId!, option.ModelId!);

        foreach (var group in allModelGroups)
        {
            foreach (var model in group.Models)
            {
                model.IsSelected = ReferenceEquals(model, option);
            }
        }

        IsModelPickerOpen = false;
        RefreshModelSummary();
    }

    /// <summary>模型摘要：未持久化选择时显示可用列表首个模型。</summary>
    private async void RefreshModelSummary()
    {
        var fallback = ResourceLookup.Resolve("assistant.model.none");
        try
        {
            var service = await EnsureServiceAsync();
            if (service is null)
            {
                SelectedModelSummary = fallback;
                return;
            }

            var (preferredProvider, preferredModel) = service.GetModelSelection();
            var groups = await service.ListAvailableModelsAsync();
            if (preferredProvider is null || preferredModel is null)
            {
                var firstGroup = groups.FirstOrDefault();
                var firstModel = firstGroup?.Models.FirstOrDefault();
                SelectedModelSummary = firstModel is null
                    ? fallback
                    : $"{firstGroup!.ProviderDisplayName} / {firstModel.ModelId}";
                RefreshSessions();
                return;
            }

            var group = groups.FirstOrDefault(item =>
                string.Equals(item.ProviderBusinessId, preferredProvider, StringComparison.OrdinalIgnoreCase));
            SelectedModelSummary = group is null
                ? preferredModel
                : $"{group.ProviderDisplayName} / {preferredModel}";
            RefreshSessions();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "小助手模型摘要刷新失败");
            SelectedModelSummary = fallback;
        }
    }

    /// <summary>逐条批准模式的 UI 审批桥：弹出审批卡片并等待用户决定。</summary>
    private Task<bool> ShowApprovalAsync(ToolApprovalRequest request)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            PendingApproval?.Resolve(false);
            PendingApproval = new ApprovalRequestViewModel(request, completion);
        });
        return completion.Task;
    }

    private void Cancel()
    {
        PendingApproval?.Resolve(false);
        ResolveService()?.Cancel();
    }

    private void NewSession()
    {
        ResolveService()?.NewSession();
        Messages.Clear();
        streamingMessage = null;
        suppressSelectionLoad = true;
        try
        {
            SelectedSession = null;
        }
        finally
        {
            suppressSelectionLoad = false;
        }

        AddSystemMessage("新会话已开始。");
    }

    /// <summary>删除历史会话；删除当前会话时自动开新会话。</summary>
    public void DeleteSession(AssistantSessionSummary? summary)
    {
        if (summary is null) return;
        var service = ResolveService();
        if (service is null) return;

        var wasCurrent = service.CurrentSession.Id == summary.SessionId;
        service.DeleteSession(summary.SessionId);

        suppressSelectionLoad = true;
        try
        {
            RefreshSessions();
            if (SelectedSession?.SessionId == summary.SessionId) SelectedSession = null;
        }
        finally
        {
            suppressSelectionLoad = false;
        }

        if (wasCurrent)
        {
            NewSession();
        }
    }

    private async Task LoadSessionAsync(AssistantSessionSummary? summary)
    {
        if (summary is null) return;
        var service = await EnsureServiceAsync();
        if (service is null) return;

        if (!await service.LoadSessionAsync(summary.SessionId))
        {
            AddSystemMessage("会话载入失败（文件可能已损坏或删除）。");
            return;
        }

        Messages.Clear();
        streamingMessage = null;
        foreach (var message in service.CurrentSession.Messages.Where(item => item.Role != ChatRole.System))
        {
            if (message.Role == ChatRole.Tool) continue; // 工具结果详情不重复展示
            if (string.IsNullOrWhiteSpace(message.Content) && message.ToolCalls.Count > 0)
            {
                Messages.Add(ChatMessageViewModel.Status($"⚙ 调用工具 {string.Join("、", message.ToolCalls.Select(call => call.Name))}"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                Messages.Add(new ChatMessageViewModel(message.Role, message.Content!));
            }
        }
    }

    private void RefreshSessions()
    {
        Sessions.Clear();
        var service = ResolveService();
        if (service is null) return;
        foreach (var summary in service.ListSessions()) Sessions.Add(summary);
    }

    private ChatMessageViewModel AppendStreamingMessage()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant, string.Empty) { IsStreaming = true };
        Messages.Add(message);
        return message;
    }

    private void AddSystemMessage(string text) => Messages.Add(ChatMessageViewModel.Status(text));
}

/// <summary>修改权限下拉项。</summary>
public sealed record AssistantPermissionOption(AssistantPermissionMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>思考等级下拉项。</summary>
public sealed record AssistantReasoningOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>模型选择弹层中的 Provider 模型项。</summary>
public sealed class AssistantModelOptionViewModel : NotifyViewModel
{
    private bool isSelected;

    public AssistantModelOptionViewModel(string? providerBusinessId, string? modelId, string displayName, bool isSelected = false)
    {
        ProviderBusinessId = providerBusinessId;
        ModelId = modelId;
        DisplayName = displayName;
        this.isSelected = isSelected;
    }

    public string? ProviderBusinessId { get; }
    public string? ModelId { get; }
    public string DisplayName { get; }
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public bool MatchesSearch(string searchTerm) =>
        string.IsNullOrWhiteSpace(searchTerm)
        || DisplayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
        || (ModelId?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>模型选择弹层中的 Provider 分组（foldout）。</summary>
public sealed class AssistantModelGroupViewModel : NotifyViewModel
{
    private bool isExpanded = true;

    public AssistantModelGroupViewModel(string providerName, IReadOnlyList<AssistantModelOptionViewModel> models)
    {
        ProviderName = providerName;
        Models = models;
    }

    public string ProviderName { get; }
    public IReadOnlyList<AssistantModelOptionViewModel> Models { get; }
    public int ModelCount => Models.Count;
    public double ExpandIconAngle => IsExpanded ? 90 : 0;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (SetProperty(ref isExpanded, value)) OnPropertyChanged(nameof(ExpandIconAngle));
        }
    }
}

/// <summary>待批准的修改操作卡片：批准/拒绝后完成等待中的审批。</summary>
public sealed class ApprovalRequestViewModel : NotifyViewModel
{
    private readonly TaskCompletionSource<bool> completion;

    public ApprovalRequestViewModel(ToolApprovalRequest request, TaskCompletionSource<bool> completion)
    {
        ToolName = request.ToolName;
        ArgumentsSummary = request.ArgumentsSummary;
        this.completion = completion;
        ApproveCommand = new DelegateCommand(() => Resolve(true));
        RejectCommand = new DelegateCommand(() => Resolve(false));
    }

    public string ToolName { get; }
    public string ArgumentsSummary { get; }
    public bool HasArguments => ArgumentsSummary.Length > 0;
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }

    public void Resolve(bool approved) => completion.TrySetResult(approved);
}

/// <summary>消息流条目：用户/助手消息或工具/状态提示。助手消息支持流式追加。</summary>
public sealed class ChatMessageViewModel : NotifyViewModel
{
    private string text;
    private bool isStreaming;

    public ChatMessageViewModel(ChatRole role, string text)
    {
        Role = role;
        this.text = text;
        if (role == ChatRole.Assistant && !IsStatus)
        {
            Markdown.Append(text);
        }
    }

    public ChatRole Role { get; }

    public bool IsStatus { get; private init; }

    public bool IsUser => Role == ChatRole.User;

    public bool IsAssistantMessage => Role == ChatRole.Assistant && !IsStatus;

    /// <summary>助手消息的 Markdown 构建器（LiveMarkdown 实时渲染，流式追加即更新）。</summary>
    public LiveMarkdown.Avalonia.ObservableStringBuilder Markdown { get; } = new();

    public string Text
    {
        get => text;
        private set => SetProperty(ref text, value);
    }

    public bool IsStreaming
    {
        get => isStreaming;
        set => SetProperty(ref isStreaming, value);
    }

    public void Append(string delta)
    {
        Text += delta;
        if (IsAssistantMessage) Markdown.Append(delta);
        OnPropertyChanged(nameof(Text));
    }

    public static ChatMessageViewModel Status(string text) =>
        new(ChatRole.Assistant, text) { IsStatus = true };
}
