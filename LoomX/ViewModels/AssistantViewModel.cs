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
    private readonly ToastService? toastService;

    private string inputText = string.Empty;
    private string statusText = string.Empty;
    private bool isRunning;
    private bool hasPersistenceWarning;
    private string? lastUserText;
    private string errorMessage = string.Empty;
    private DispatcherTimer? elapsedTimer;
    private AssistantSessionItemViewModel? selectedSession;
    private ChatMessageViewModel? streamingMessage;
    private ChatMessageViewModel? currentGroup;
    private bool suppressSelectionLoad;
    private bool isModelPickerOpen;
    private bool isHistoryOpen;
    private string selectedModelSummary = string.Empty;
    private string selectedModelName = string.Empty;
    private string modelSearchTerm = string.Empty;
    private AssistantPermissionOption selectedPermissionMode;
    private AssistantReasoningOption selectedReasoningEffort;
    private ApprovalRequestViewModel? pendingApproval;
    private IReadOnlyList<AssistantModelGroupViewModel> allModelGroups = [];

    public AssistantViewModel(GatewayProcessService gatewayService, ILoggerFactory? loggerFactory = null, ToastService? toastService = null)
    {
        this.gatewayService = gatewayService;
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<AssistantViewModel>();
        this.toastService = toastService;

        SendCommand = new AsyncCommand(() => SendAsync(false), () => !IsRunning && !string.IsNullOrWhiteSpace(InputText));
        CancelCommand = new DelegateCommand(Cancel);
        NewSessionCommand = new DelegateCommand(NewSession);
        LoadSessionCommand = new AsyncCommand(parameter => LoadSessionAsync(parameter as AssistantSessionItemViewModel));
        RetryCommand = new AsyncCommand(() => SendAsync(true), () => !IsRunning && !string.IsNullOrWhiteSpace(lastUserText));
        DismissErrorCommand = new DelegateCommand(ClearError);

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

        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMessages));
        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSessions));

        RefreshSessions();
        RefreshModelSummary();
        StartElapsedTimer();
    }

    /// <summary>每秒刷新运行中的过程块耗时；没有 UI 线程（单元测试）时静默降级。</summary>
    private void StartElapsedTimer()
    {
        try
        {
            elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnElapsedTick);
            elapsedTimer.Start();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "过程块计时器不可用，耗时只在事件到达时刷新");
        }
    }

    private void OnElapsedTick(object? sender, EventArgs args)
    {
        if (!isRunning) return;
        foreach (var message in Messages)
        {
            if (message.IsProcess && !message.IsFinished) message.RefreshElapsed();
        }
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    /// <summary>历史会话列表。条目是 ViewModel 包装，支持行内改名与删除。</summary>
    public ObservableCollection<AssistantSessionItemViewModel> Sessions { get; } = [];

    /// <summary>模型选择弹层中的 Provider 分组（foldout）。</summary>
    public ObservableCollection<AssistantModelGroupViewModel> ModelGroups { get; } = [];

    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand LoadSessionCommand { get; }

    /// <summary>请求失败卡片上的「重试」：重发最后一条用户输入，不重复追加用户气泡。</summary>
    public ICommand RetryCommand { get; }

    /// <summary>请求失败卡片上的「关闭」。</summary>
    public ICommand DismissErrorCommand { get; }

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
            if (SetProperty(ref isRunning, value))
            {
                (SendCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                (RetryCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>请求失败态：在消息流底部展示失败卡片（不是输入框上方的固定文本）。</summary>
    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => errorMessage.Length > 0;

    private void ClearError() => ErrorMessage = string.Empty;

    /// <summary>历史会话选择；选中即载入。</summary>
    public AssistantSessionItemViewModel? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (!SetProperty(ref selectedSession, value)) return;
            OnPropertyChanged(nameof(CurrentSessionTitle));
            if (value is not null && !suppressSelectionLoad)
            {
                _ = LoadSessionAsync(value);
            }
        }
    }

    /// <summary>标题区展示的会话名；未选中历史会话时回退到“新会话”。</summary>
    public string CurrentSessionTitle =>
        string.IsNullOrWhiteSpace(selectedSession?.Title)
            ? ResourceLookup.Resolve("assistant.session.new")
            : selectedSession!.Title;

    /// <summary>历史会话浮层开关（双向绑定到 Popup）。</summary>
    public bool IsHistoryOpen
    {
        get => isHistoryOpen;
        set => SetProperty(ref isHistoryOpen, value);
    }

    /// <summary>消息流为空时展示空态。</summary>
    public bool HasNoMessages => Messages.Count == 0;

    /// <summary>历史会话浮层没有任何记录。</summary>
    public bool HasNoSessions => Sessions.Count == 0;

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
            UpdateModelSummary();
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

    /// <param name="isRetry">重试时不重复追加用户气泡，直接复用上一次的用户输入。</param>
    private async Task SendAsync(bool isRetry)
    {
        var text = isRetry ? lastUserText ?? string.Empty : InputText.Trim();
        if (text.Length == 0) return;

        if (!isRetry) InputText = string.Empty;
        lastUserText = text;
        hasPersistenceWarning = false;
        ErrorMessage = string.Empty;
        if (!isRetry) Messages.Add(new ChatMessageViewModel(ChatRole.User, text));
        IsRunning = true;
        StatusText = ResourceLookup.Resolve("assistant.status.working");
        AssistantService? service = null;

        try
        {
            service = await EnsureServiceAsync();
            if (service is null)
            {
                ErrorMessage = ResourceLookup.Resolve("assistant.error.service_unavailable");
                return;
            }

            service.ApprovalHandler = ShowApprovalAsync;
            await foreach (var agentEvent in service.SendAsync(text))
            {
                await Dispatcher.UIThread.InvokeAsync(() => Project(agentEvent));
            }
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "AI 助手请求被拒绝");
            Dispatcher.UIThread.Post(() => ErrorMessage = exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AI 助手运行失败");
            Dispatcher.UIThread.Post(() => ErrorMessage = ResourceLookup.Resolve("assistant.error.unexpected"));
        }
        finally
        {
            if (service is not null) service.ApprovalHandler = null;
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                if (!hasPersistenceWarning) StatusText = string.Empty;
                streamingMessage = null;
                currentGroup = null;
                RefreshSessions();
                _ = TryAutoTitleAsync(service);
            });
        }
    }

    /// <summary>
    /// 一轮对话结束后给会话起标题：只有还没有自定义/摘要标题时才调模型，
    /// 失败静默（标题是锦上添花，不该打扰用户）。
    /// </summary>
    private async Task TryAutoTitleAsync(AssistantService? service)
    {
        if (service is null) return;
        try
        {
            var sessionId = service.CurrentSession.Id;
            var hasTitle = service.ListSessions().Any(item => item.SessionId == sessionId && item.HasCustomTitle);
            if (hasTitle) return;

            var generated = await service.TrySummarizeTitleAsync();
            if (generated is not null)
                Dispatcher.UIThread.Post(RefreshSessions);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "会话摘要标题生成失败（已静默）");
        }
    }

    /// <summary>最终内容完整输出后结束当前过程 foldout，并断开本轮引用。</summary>
    private void FinishCurrentGroup(DateTimeOffset? timestamp = null)
    {
        currentGroup?.Finish(timestamp);
        currentGroup = null;
    }

    /// <summary>取得当前过程 foldout；没有就新建一个（与正文同级的消息流条目）。</summary>
    private ChatMessageViewModel EnsureGroup(DateTimeOffset? timestamp)
    {
        if (currentGroup is null)
        {
            currentGroup = ChatMessageViewModel.Process(timestamp);
            Messages.Add(currentGroup);
        }

        return currentGroup;
    }

    /// <summary>取得 foldout 内指定标签的子项；标签与上一项相同则复用，不同则另起一项。</summary>
    private static ProcessItemViewModel EnsureGroupItem(ChatMessageViewModel group, string label) =>
        group.EnsureItem(label);

    private static string StepLabel => ResourceLookup.Resolve("assistant.process.steps");

    /// <summary>AgentEvent → UI 投影（规格 #15：UI 只消费事件）。</summary>
    internal void Project(AgentEvent agentEvent)
    {
        switch (agentEvent.Kind)
        {
            case AgentEventKind.SessionStarted:
                // 新用户轮次只切断上一轮引用；没有最终内容的旧过程不能冒充“已完成”。
                streamingMessage = null;
                currentGroup = null;
                break;

            case AgentEventKind.StepStarted:
                // step 只切换阶段性正文；整轮的思考与工具调用仍归入同一个父 foldout。
                streamingMessage = null;
                break;

            case AgentEventKind.TextDelta:
                streamingMessage ??= AppendStreamingMessage(agentEvent.Timestamp);
                streamingMessage.Append(agentEvent.Text ?? string.Empty);
                break;

            case AgentEventKind.ReasoningDelta:
            {
                var label = ResourceLookup.Resolve(agentEvent.IsSummary
                    ? "assistant.process.summary" : "assistant.process.thinking");
                EnsureGroupItem(EnsureGroup(agentEvent.Timestamp), label).Append(agentEvent.Text ?? string.Empty);
                break;
            }

            case AgentEventKind.MessageCompleted:
                if (agentEvent.Message?.Role == ChatRole.Assistant)
                {
                    if (streamingMessage is not null) streamingMessage.IsStreaming = false;
                    // 带工具调用的 assistant 消息仍是中间步骤；无工具调用才是完整 finish content。
                    if (agentEvent.Message.ToolCalls.Count == 0)
                    {
                        FinishCurrentGroup(agentEvent.Timestamp);
                        streamingMessage = null;
                    }
                }
                break;

            case AgentEventKind.ToolCallStarted:
                streamingMessage = null;
                EnsureGroupItem(EnsureGroup(agentEvent.Timestamp), StepLabel)
                    .Append(string.Format(ResourceLookup.Resolve("assistant.step.call"), agentEvent.ToolName) + "\n");
                break;

            case AgentEventKind.ToolApprovalRequested:
                streamingMessage = null;
                EnsureGroupItem(EnsureGroup(agentEvent.Timestamp), StepLabel)
                    .Append(string.Format(ResourceLookup.Resolve("assistant.step.approval"), agentEvent.ToolName) + "\n");
                break;

            case AgentEventKind.ToolCallCompleted:
                EnsureGroupItem(EnsureGroup(agentEvent.Timestamp), StepLabel)
                    .Append(agentEvent.Success == true
                        ? string.Format(ResourceLookup.Resolve("assistant.step.completed"), agentEvent.ToolName) + "\n"
                        : string.Format(ResourceLookup.Resolve("assistant.step.failed"), agentEvent.ToolName, agentEvent.Detail) + "\n");
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
                currentGroup = null;
                // Detail 为 ModelErrorFormatter 组装的多行详情（本地化描述 + 状态码/错误码/上游描述）
                Messages.Add(ChatMessageViewModel.Status($"⚠ {ResourceLookup.Resolve("assistant.task.failed")}：\n{agentEvent.Detail}"));
                break;

            case AgentEventKind.WaitingForUser:
                Messages.Add(ChatMessageViewModel.Status($"✋ 等待你操作：{agentEvent.Detail}"));
                break;

            case AgentEventKind.TaskCompleted:
                streamingMessage = null;
                currentGroup = null;
                break;

            case AgentEventKind.TaskCancelled:
                streamingMessage = null;
                currentGroup = null;
                Messages.Add(ChatMessageViewModel.Status(ResourceLookup.Resolve("assistant.task.cancelled")));
                break;

            case AgentEventKind.PersistenceFailed:
                hasPersistenceWarning = true;
                StatusText = ResourceLookup.Resolve("assistant.persistence.failed");
                break;
        }
    }

    /// <summary>打开模型选择弹层：从服务端拉取可用模型并按 Provider 分组。</summary>
    public async Task OpenModelPickerAsync()
    {
        var service = await EnsureServiceAsync();
        if (service is null)
        {
            AddSystemMessage("AI 助手服务初始化失败，请查看控制台日志。");
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
                selectedModelName = fallback;
                UpdateModelSummary();
                return;
            }

            var (preferredProvider, preferredModel) = service.GetModelSelection();
            var groups = await service.ListAvailableModelsAsync();
            if (preferredProvider is null || preferredModel is null)
            {
                var firstGroup = groups.FirstOrDefault();
                var firstModel = firstGroup?.Models.FirstOrDefault();
                selectedModelName = firstModel is null
                    ? fallback
                    : $"{firstGroup!.ProviderDisplayName} / {firstModel.ModelId}";
                UpdateModelSummary();
                RefreshSessions();
                return;
            }

            var group = groups.FirstOrDefault(item =>
                string.Equals(item.ProviderBusinessId, preferredProvider, StringComparison.OrdinalIgnoreCase));
            selectedModelName = group is null
                ? preferredModel
                : $"{group.ProviderDisplayName} / {preferredModel}";
            UpdateModelSummary();
            RefreshSessions();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI 助手模型摘要刷新失败");
            selectedModelName = fallback;
            UpdateModelSummary();
        }
    }

    private void UpdateModelSummary() => SelectedModelSummary = string.IsNullOrEmpty(selectedModelName)
        ? string.Empty : $"{selectedModelName} · {SelectedReasoningEffort.Label}";

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
        hasPersistenceWarning = false;
        StatusText = string.Empty;
        ErrorMessage = string.Empty;
        lastUserText = null;
        Messages.Clear();
        streamingMessage = null;
        currentGroup = null;
        suppressSelectionLoad = true;
        try
        {
            SelectedSession = null;
        }
        finally
        {
            suppressSelectionLoad = false;
        }
        // 刻意不在消息流里插“新会话已开始”：空会话就该回到空态，别拿系统提示占位置。
    }

    /// <summary>删除历史会话；删除当前会话时自动开新会话。</summary>
    public void DeleteSession(AssistantSessionItemViewModel? item)
    {
        if (item is null) return;
        var service = ResolveService();
        if (service is null) return;

        var wasCurrent = service.CurrentSession.Id == item.SessionId;
        service.DeleteSession(item.SessionId);

        RefreshSessions();

        if (wasCurrent)
        {
            NewSession();
        }
    }

    /// <summary>行内改名：回车或失焦提交，空串表示回到自动标题。</summary>
    public async Task RenameSessionAsync(AssistantSessionItemViewModel item, string? newTitle)
    {
        var service = ResolveService();
        if (service is null) return;

        var trimmed = newTitle?.Trim();
        if (string.Equals(trimmed, item.Summary.Title, StringComparison.Ordinal))
        {
            item.CancelRename();
            return;
        }

        try
        {
            await service.RenameSessionAsync(item.SessionId, trimmed);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AI 助手会话改名失败 {SessionId}", item.SessionId);
            toastService?.Show(ResourceLookup.Resolve("assistant.history.rename.failed"), ToastLevel.Error);
            item.CancelRename();
            return;
        }

        // RefreshSessions 会重建列表并把 SelectedSession 指向当前会话的新实例，
        // 标题因此立刻跟着变（改名的就是这个会话）。
        RefreshSessions();

        toastService?.Show(ResourceLookup.Resolve("assistant.history.renamed"), ToastLevel.Success);
    }

    private async Task LoadSessionAsync(AssistantSessionItemViewModel? item)
    {
        if (item is null) return;
        ErrorMessage = string.Empty;
        lastUserText = null;
        var service = await EnsureServiceAsync();
        if (service is null) return;

        if (!await service.LoadSessionAsync(item.SessionId))
        {
            AddSystemMessage("会话载入失败（文件可能已损坏或删除）。");
            return;
        }

        // 载入成功后同步标题区：CurrentSessionTitle 是 SelectedSession 的派生属性，
        // 不写回就会一直显示上一个会话的标题，直到下次 RefreshSessions 才纠正。
        SyncCurrentSessionSelection(item);

        Messages.Clear();
        streamingMessage = null;
        currentGroup = null;
        var session = service.CurrentSession;
        var hasToolActivities = session.Activities.Any(item => item.Kind == AgentEventKind.ToolCallStarted);
        var history = session.Messages.Where(item => item.Role != ChatRole.System)
            .Select(item => (Timestamp: item.Timestamp, Message: (ChatMessage?)item, Activity: (AgentEvent?)null))
            .Concat(session.Activities.Select(item => (item.Timestamp, Message: (ChatMessage?)null, Activity: (AgentEvent?)item)))
            .OrderBy(item => item.Timestamp).ToArray();
        foreach (var entry in history)
        {
            if (entry.Activity is { } activity)
            {
                if (activity.Kind is not (AgentEventKind.SessionStarted or AgentEventKind.MessageCompleted)) Project(activity);
                continue;
            }
            var message = entry.Message!;
            if (message.Role == ChatRole.Tool) continue;
            if (message.Role == ChatRole.User)
            {
                streamingMessage = null;
                currentGroup = null;
                Messages.Add(new ChatMessageViewModel(message.Role, message.Content ?? string.Empty, message.Timestamp));
                continue;
            }

            if (message.Blocks.Count > 0)
            {
                foreach (var block in message.Blocks)
                {
                    if (block.Kind == ChatContentKind.Thinking)
                        Project(new AgentEvent(session.Id, AgentEventKind.ReasoningDelta, message.Timestamp)
                        { Text = block.Text, IsSummary = block.IsSummary });
                    else if (block.Kind == ChatContentKind.Text)
                        Project(new AgentEvent(session.Id, AgentEventKind.TextDelta, message.Timestamp) { Text = block.Text });
                }
            }
            else if (!string.IsNullOrWhiteSpace(message.Content))
                Project(new AgentEvent(session.Id, AgentEventKind.TextDelta, message.Timestamp) { Text = message.Content });

            Project(new AgentEvent(session.Id, AgentEventKind.MessageCompleted, message.Timestamp) { Message = message });
            if (!hasToolActivities && message.ToolCalls.Count > 0)
            {
                EnsureGroupItem(EnsureGroup(message.Timestamp), StepLabel)
                    .Append(string.Join('\n', message.ToolCalls.Select(call =>
                        string.Format(ResourceLookup.Resolve("assistant.step.call"), call.Name))));
            }
        }
        streamingMessage = null;
        currentGroup = null;
    }

    /// <summary>
    /// 把 <see cref="SelectedSession"/> 指向当前服务会话对应的列表项（只刷标题，不触发载入）。
    /// 列表里找不到时用 <paramref name="fallback"/> 兜底（例如列表还没刷新就载入了会话）。
    /// </summary>
    private void SyncCurrentSessionSelection(AssistantSessionItemViewModel? fallback = null)
    {
        var service = ResolveService();
        if (service is null) return;

        var currentId = service.CurrentSession.Id;
        suppressSelectionLoad = true;
        try
        {
            SelectedSession = Sessions.FirstOrDefault(candidate => candidate.SessionId == currentId)
                              ?? (fallback?.SessionId == currentId ? fallback : null);
        }
        finally
        {
            suppressSelectionLoad = false;
        }
    }

    /// <summary>
    /// 重新加载历史会话列表（打开历史浮层前调用，保证列表是最新的），
    /// 并把当前服务会话同步到 <see cref="SelectedSession"/>，让标题区跟着更新。
    /// </summary>
    public void RefreshSessions()
    {
        Sessions.Clear();
        var service = ResolveService();
        if (service is null) return;

        foreach (var summary in service.ListSessions())
        {
            Sessions.Add(new AssistantSessionItemViewModel(summary));
        }

        // 只同步标题，不触发载入（载入会清空当前消息）。
        SyncCurrentSessionSelection();
    }

    private ChatMessageViewModel AppendStreamingMessage(DateTimeOffset? timestamp = null)
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant, string.Empty, timestamp) { IsStreaming = true };
        Messages.Add(message);
        return message;
    }

    private void AddSystemMessage(string text) => Messages.Add(ChatMessageViewModel.Status(text));

    public void NotifyCopied() => toastService?.Show(ResourceLookup.Resolve("assistant.copied"), ToastLevel.Success);
}

/// <summary>
/// 历史会话列表条目：包一层以便支持行内改名（编辑态切换）与 hover 操作按钮。
/// </summary>
public sealed class AssistantSessionItemViewModel : NotifyViewModel
{
    private bool isEditing;
    private string editText;

    public AssistantSessionItemViewModel(AssistantSessionSummary summary)
    {
        Summary = summary;
        editText = summary.Title;
    }

    public AssistantSessionSummary Summary { get; }

    public string SessionId => Summary.SessionId;

    public string Title => Summary.Title;

    public string DisplayUpdatedAt => Summary.DisplayUpdatedAt;

    /// <summary>是否处于行内改名状态（文本框替换标题文本）。</summary>
    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (SetProperty(ref isEditing, value)) OnPropertyChanged(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !isEditing;

    /// <summary>编辑框内容；进入编辑态时重置为当前标题。</summary>
    public string EditText
    {
        get => editText;
        set => SetProperty(ref editText, value);
    }

    public void BeginRename()
    {
        EditText = Title;
        IsEditing = true;
    }

    public void CancelRename() => IsEditing = false;
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
    private bool isExpanded;
    private DateTimeOffset? finishedAt;
    private readonly ChatEntryKind kind;

    public ChatMessageViewModel(ChatRole role, string text, DateTimeOffset? timestamp = null)
        : this(role, text, ChatEntryKind.Message, timestamp)
    {
    }

    private ChatMessageViewModel(ChatRole role, string text, ChatEntryKind kind, DateTimeOffset? timestamp)
    {
        Role = role;
        this.text = text;
        this.kind = kind;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        if (IsAssistantMessage)
        {
            Markdown.Append(text);
        }
    }

    public ChatRole Role { get; }

    public bool IsStatus => kind == ChatEntryKind.Status;

    /// <summary>
    /// 过程块 = 思考与工具调用共同的<b>父级 foldout</b>，与助手正文（finish content）同属消息流的一级条目。
    /// “处理中 / 已完成 xx” 只出现在这一层，子项只负责“思考”“工具调用”这类标签。
    /// </summary>
    public bool IsProcess => kind == ChatEntryKind.Process;

    /// <summary>foldout 内的子项：思考、摘要、工具调用。展开时才显示标签。</summary>
    public ObservableCollection<ProcessItemViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    /// <summary>子项文本的合并结果（供折叠预览与断言使用）。</summary>
    public string ItemsText => Items.Count == 0 ? string.Empty : string.Join('\n', Items.Select(item => item.Text));

    public DateTimeOffset Timestamp { get; }

    public string DisplayTime => Timestamp.ToLocalTime().ToString("HH:mm");

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!SetProperty(ref isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpandIconAngle));
        }
    }

    /// <summary>过程块折叠箭头的旋转角度：折叠朝右，展开朝下。</summary>
    public double ExpandIconAngle => IsExpanded ? 90 : 0;

    /// <summary>过程块是否已结束（finish_content 完整输出之后）。</summary>
    public bool IsFinished => finishedAt is not null;

    /// <summary>过程块耗时；结束后冻结。</summary>
    public TimeSpan Elapsed => (finishedAt ?? DateTimeOffset.UtcNow) - Timestamp;

    /// <summary>耗时文案：12s / 1m23s / 1h05m。</summary>
    public string DurationText => FormatDuration(Elapsed);

    /// <summary>过程块标题：运行中“处理中 12s”，结束后“已完成 12s”。</summary>
    public string ProcessStatusText => string.Format(
        ResourceLookup.Resolve(IsFinished ? "assistant.process.completed" : "assistant.process.elapsed"),
        DurationText);

    /// <summary>过程块有内容即可展开；运行中默认展开，最终内容完成后默认折叠。</summary>
    public bool CanExpand => HasItems;

    /// <summary>
    /// 结束过程块：最终内容完整输出后冻结耗时并默认折叠。
    /// </summary>
    public void Finish(DateTimeOffset? timestamp = null)
    {
        // 只在首次结束时冻结耗时；折叠则每次都强制（任务结束时要把展开中的过程块收起来）。
        finishedAt ??= timestamp ?? DateTimeOffset.UtcNow;
        IsExpanded = false;
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(CanExpand));
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ProcessStatusText));
    }

    /// <summary>运行中由计时器驱动的耗时刷新。</summary>
    public void RefreshElapsed()
    {
        if (finishedAt is not null) return;
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ProcessStatusText));
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h{value.Minutes:00}m";
        if (value.TotalMinutes >= 1) return $"{(int)value.TotalMinutes}m{value.Seconds:00}s";
        return $"{Math.Max(0, (int)value.TotalSeconds)}s";
    }

    public bool IsUser => Role == ChatRole.User && kind == ChatEntryKind.Message;

    public bool IsAssistantMessage => Role == ChatRole.Assistant && kind == ChatEntryKind.Message;

    /// <summary>助手消息的 Markdown 构建器（LiveMarkdown 实时渲染，流式追加即更新）。</summary>
    public LiveMarkdown.Avalonia.ObservableStringBuilder Markdown { get; } = new();

    public string Text
    {
        get => text;
        private set
        {
            if (SetProperty(ref text, value)) OnPropertyChanged(nameof(ProcessPreview));
        }
    }

    /// <summary>
    /// 过程块折叠时展示的内部最新一行 —— 刻意不带“思考 / 工具调用”标签，
    /// 标签只在展开后随子项一起出现。
    /// </summary>
    public string ProcessPreview
    {
        get
        {
            var merged = ItemsText;
            if (merged.Length == 0) return string.Empty;
            var span = merged.AsSpan().TrimEnd();
            var index = span.LastIndexOfAny('\r', '\n');
            return (index >= 0 ? span[(index + 1)..] : span).Trim().ToString();
        }
    }

    /// <summary>子项内容变化时由子项回调，刷新折叠预览。</summary>
    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(ProcessPreview));
        OnPropertyChanged(nameof(ItemsText));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanExpand));
    }

    /// <summary>取得指定标签的子项；与最后一项同标签则复用，否则另起一项。</summary>
    public ProcessItemViewModel EnsureItem(string label)
    {
        if (Items.Count > 0 && Items[^1].Label == label) return Items[^1];
        var item = new ProcessItemViewModel(label, this);
        Items.Add(item);
        RefreshPreview();
        return item;
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
    }

    public static ChatMessageViewModel Status(string text) =>
        new(ChatRole.Assistant, text, ChatEntryKind.Status, null);

    /// <summary>新建一个过程 foldout（父级），内容是空的，子项通过 <see cref="EnsureItem"/> 挂进去。</summary>
    public static ChatMessageViewModel Process(DateTimeOffset? timestamp = null)
    {
        var process = new ChatMessageViewModel(ChatRole.Assistant, string.Empty, ChatEntryKind.Process, timestamp);
        process.IsExpanded = true;
        return process;
    }
}

/// <summary>
/// 过程 foldout 里的一个子项（思考 / 摘要 / 工具调用）。
/// 它<b>不是</b>消息流的一级条目，标签只在父级展开后可见。
/// </summary>
public sealed class ProcessItemViewModel : NotifyViewModel
{
    private string text = string.Empty;
    private bool isExpanded;

    public ProcessItemViewModel(string label, ChatMessageViewModel owner)
    {
        Label = label;
        Owner = owner;
    }

    public string Label { get; }

    public ChatMessageViewModel Owner { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!SetProperty(ref isExpanded, value)) return;
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(ExpandIconAngle));
        }
    }

    public double ExpandIconAngle => IsExpanded ? 90 : 0;

    /// <summary>折叠时显示最新一行，展开时显示固定标签。</summary>
    public string HeaderText => IsExpanded || string.IsNullOrEmpty(Preview) ? Label : Preview;

    private string Preview
    {
        get
        {
            if (text.Length == 0) return string.Empty;
            var span = text.AsSpan().TrimEnd();
            var index = span.LastIndexOfAny('\r', '\n');
            return (index >= 0 ? span[(index + 1)..] : span).Trim().ToString();
        }
    }

    public string Text
    {
        get => text;
        private set
        {
            if (!SetProperty(ref text, value)) return;
            OnPropertyChanged(nameof(HeaderText));
            Owner.RefreshPreview();
        }
    }

    public void Append(string delta) => Text += delta;

    public override string ToString() => $"{Label}: {Text}";
}

public enum ChatEntryKind { Message, Status, Process }
