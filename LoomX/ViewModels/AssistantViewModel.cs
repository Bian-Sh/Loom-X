using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using LoomX.Assistant;
using LoomX.Services;
using Microsoft.Extensions.Logging;

namespace LoomX.ViewModels;

/// <summary>
/// 小助手聊天页 VM：消费 AgentEvent 流做 Activity Projection，不绑定 Harness 内部对象。
/// 助手服务在网关进程内（GatewayProcessService.GetHostedService），网关未运行时给出明确提示。
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

    public AssistantViewModel(GatewayProcessService gatewayService, ILoggerFactory? loggerFactory = null)
    {
        this.gatewayService = gatewayService;
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<AssistantViewModel>();

        SendCommand = new AsyncCommand(SendAsync, () => IsRunning || !string.IsNullOrWhiteSpace(InputText));
        CancelCommand = new DelegateCommand(Cancel);
        NewSessionCommand = new DelegateCommand(NewSession);
        LoadSessionCommand = new AsyncCommand(parameter => LoadSessionAsync(parameter as AssistantSessionSummary));

        RefreshSessions();
        AddSystemMessage("我是 LoomX 小助手，可以帮你配置 Provider、诊断连通性、接入中转站。网关启动后即可开始。");
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<AssistantSessionSummary> Sessions { get; } = [];

    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand LoadSessionCommand { get; }

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
            if (SetProperty(ref selectedSession, value) && value is not null)
            {
                _ = LoadSessionAsync(value);
            }
        }
    }

    private AssistantService? ResolveService() => gatewayService.GetHostedService<AssistantService>();

    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0) return;

        var service = ResolveService();
        if (service is null)
        {
            AddSystemMessage("网关未在本进程运行，小助手暂不可用。请先启动网关。");
            return;
        }

        InputText = string.Empty;
        Messages.Add(new ChatMessageViewModel(ChatRole.User, text));
        IsRunning = true;
        StatusText = "小助手正在工作…";

        try
        {
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
                Messages.Add(ChatMessageViewModel.Status($"⚠ 任务失败：{agentEvent.Detail}"));
                break;

            case AgentEventKind.WaitingForUser:
                Messages.Add(ChatMessageViewModel.Status($"✋ 等待你操作：{agentEvent.Detail}"));
                break;

            case AgentEventKind.TaskCompleted:
                streamingMessage = null;
                break;
        }
    }

    private void Cancel() => ResolveService()?.Cancel();

    private void NewSession()
    {
        ResolveService()?.NewSession();
        Messages.Clear();
        streamingMessage = null;
        AddSystemMessage("新会话已开始。");
    }

    private async Task LoadSessionAsync(AssistantSessionSummary? summary)
    {
        if (summary is null) return;
        var service = ResolveService();
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
