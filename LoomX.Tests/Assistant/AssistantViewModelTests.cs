using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using LoomX.Assistant;
using LoomX.Assistant.UserDecisions;
using LoomX.ViewModels;
using LoomX.Services;

namespace LoomX.Tests.Assistant;

/// <summary>
/// Chat UI 事件投影：AgentEvent → 消息流。验证 UI 只依赖事件，不依赖 Harness 内部对象。
/// </summary>
public sealed class AssistantViewModelTests
{
    [Fact]
    public void 历史文本块合并连续文本避免Emoji代理项被分割()
    {
        var blocks = new[]
        {
            new ChatContentBlock(ChatContentKind.Thinking, "思考"),
            new ChatContentBlock(ChatContentKind.Text, "\uD83D"),
            new ChatContentBlock(ChatContentKind.Text, "\uDD34"),
            new ChatContentBlock(ChatContentKind.Text, " 正文"),
        };

        var replay = AssistantViewModel.CoalesceHistoricalTextBlocks(blocks).ToArray();

        Assert.Equal(2, replay.Length);
        Assert.Equal(ChatContentKind.Thinking, replay[0].Kind);
        Assert.Equal("🔴 正文", replay[1].Text);
    }

    [Fact]
    public void Project_TextDeltas_ReuseStreamingMessageUntilFinalMessageCompletes()
    {
        var viewModel = CreateViewModel();
        var baseline = viewModel.Messages.Count;
        var chunks = new[]
        {
            "第一段正文正在流式输出，",
            "第二段继续追加到同一个消息气泡，",
            "最后一段到达后仍要等待完整消息事件。",
        };

        foreach (var chunk in chunks)
        {
            viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = chunk });
        }

        Assert.Equal(baseline + 1, viewModel.Messages.Count);
        var message = viewModel.Messages[^1];
        Assert.Equal(string.Concat(chunks), message.Text);
        Assert.True(message.IsStreaming);
        Assert.True(message.IsAssistantMessage);
        Assert.Equal(string.Concat(chunks), message.Markdown.ToString());

        viewModel.Project(Event(AgentEventKind.MessageCompleted) with
        {
            Message = ChatMessage.Assistant(string.Concat(chunks)),
        });

        Assert.Same(message, viewModel.Messages[^1]);
        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void Project_EventFromOtherSession_DoesNotPolluteViewedSession()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(
            AgentEvent.Create("running-session", AgentEventKind.TextDelta) with { Text = "旧会话输出" },
            "viewed-session");

        Assert.Empty(viewModel.Messages);

        viewModel.Project(
            AgentEvent.Create("viewed-session", AgentEventKind.TextDelta) with { Text = "当前会话输出" },
            "viewed-session");

        Assert.Equal("当前会话输出", Assert.Single(viewModel.Messages).Text);
    }

    [Fact]
    public void Project_ToolEvents_AggregatesCollapsedSteps()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.list_providers" });
        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.list_providers", Success = true });

        var steps = Assert.Single(viewModel.Messages, message => message.IsProcess);
        Assert.True(steps.IsExpanded);
        Assert.Contains("调用工具 loomx.list_providers", steps.ItemsText);
        Assert.Contains("完成", steps.ItemsText);
    }

    [Fact]
    public void Project_ToolFailed_IncludesDetail()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.test_provider", Success = false, Detail = "auth_failed" });

        var last = viewModel.Messages[^1];
        Assert.True(last.IsProcess);
        Assert.Contains("auth_failed", last.ItemsText);
    }

    [Fact]
    public void Project_ToolCallAfterStreaming_ClosesStreamingMessage()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "让我查一下" });
        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.get_status" });
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "查完了" });

        // 第二段流式文本应进入新的消息气泡，而不是追加到工具状态里
        var assistantMessages = viewModel.Messages.Where(message => message.IsAssistantMessage).ToArray();
        Assert.Equal(2, assistantMessages.Length);
        Assert.Equal("让我查一下", assistantMessages[0].Text);
        Assert.Equal("查完了", assistantMessages[1].Text);
    }

    [Fact]
    public void Project_TaskFailed_AddsErrorBubble()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.TaskFailed) with { Detail = "超过最大步骤数 16。" });

        var error = Assert.Single(viewModel.Messages);
        Assert.True(error.IsError);
        Assert.False(error.IsStatus);
        Assert.False(error.IsAssistantMessage);
        Assert.Contains("超过最大步骤数 16。", error.Text);
    }

    [Fact]
    public void Project_历史失败事件保留时间和正文且新轮次不删除错误()
    {
        var viewModel = CreateViewModel();
        var timestamp = DateTimeOffset.Parse("2026-09-15T08:00:00+08:00");
        viewModel.Project(Event(AgentEventKind.TaskFailed) with { Timestamp = timestamp, Detail = "历史错误" });
        viewModel.Project(Event(AgentEventKind.SessionStarted));
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "下一轮正文" });
        var error = Assert.Single(viewModel.Messages, message => message.IsError);
        Assert.Equal(timestamp, error.Timestamp);
        Assert.Equal("历史错误", error.Text);
        Assert.Empty(error.Markdown.ToString());
    }

    [Fact]
    public void StatusMessage_DoesNotRenderAsMarkdown()
    {
        var status = ChatMessageViewModel.Status("⚙ 调用工具");

        Assert.True(status.IsStatus);
        Assert.False(status.IsAssistantMessage);
    }

    [Fact]
    public void Project_ToolApprovalRequested_AddsWaitingStatus()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.ToolApprovalRequested) with { ToolName = "loomx.update_provider" });

        Assert.Contains(viewModel.Messages, message => message.IsProcess && message.ItemsText.Contains("等待你批准") && message.ItemsText.Contains("loomx.update_provider"));
    }

    [Fact]
    public void Project_MultipleStepsShareOneProcessUntilFinalContentCompletes()
    {
        var viewModel = CreateViewModel();
        viewModel.Project(Event(AgentEventKind.StepStarted));
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "原始思考" });
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "摘要", IsSummary = true });
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "先检查" });
        viewModel.Project(Event(AgentEventKind.MessageCompleted) with
        {
            Message = ChatMessage.AssistantToolCalls([new ToolCall("call-1", "loomx.inspect", "{}")], "先检查"),
        });
        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.inspect" });
        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.inspect", Success = true });

        var process = Assert.Single(viewModel.Messages, item => item.IsProcess);
        Assert.False(process.IsFinished);
        Assert.Contains("处理中", process.ProcessStatusText);
        Assert.Equal(2, process.Items.Count);

        viewModel.Project(Event(AgentEventKind.StepStarted));
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "继续思考" });
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "**完成**" });

        Assert.Same(process, Assert.Single(viewModel.Messages, item => item.IsProcess));
        Assert.False(process.IsFinished);
        Assert.Equal(2, process.Items.Count);

        viewModel.Project(Event(AgentEventKind.MessageCompleted) with { Message = ChatMessage.Assistant("**完成**") });
        viewModel.Project(Event(AgentEventKind.TaskCompleted));

        Assert.True(process.IsFinished);
        Assert.Contains("已完成", process.ProcessStatusText);
        Assert.False(process.IsExpanded);
        Assert.Equal("**完成**", viewModel.Messages.Last(item => item.IsAssistantMessage).Markdown.ToString());
    }

    [Fact]
    public void Project_ToolMessages_PreserveArgumentsAndResultsByCallId()
    {
        var viewModel = CreateViewModel();
        var toolCall = new ToolCall("call-42", "loomx.inspect", """{"path":"D:/demo","depth":3}""") { ArgumentsAreSafe = true };

        viewModel.Project(Event(AgentEventKind.MessageCompleted) with
        {
            Message = ChatMessage.AssistantToolCalls([toolCall]),
        });
        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with
        {
            ToolName = toolCall.Name,
            ToolCallId = toolCall.Id,
        });
        viewModel.Project(Event(AgentEventKind.MessageCompleted) with
        {
            Message = ChatMessage.ToolResult(toolCall, """{"files":["a.cs","b.cs"],"count":2}"""),
        });
        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with
        {
            ToolName = toolCall.Name,
            ToolCallId = toolCall.Id,
            Success = true,
        });

        var process = Assert.Single(viewModel.Messages, item => item.IsProcess);
        var toolItem = Assert.Single(process.Items, item => item.Label.Contains("工具"));
        Assert.Contains("D:/demo", toolItem.DetailsText);
        Assert.Contains("depth", toolItem.DetailsText);
        Assert.Contains("a.cs", toolItem.DetailsText);
        Assert.Contains("count", toolItem.DetailsText);
        Assert.Contains("完成", toolItem.DetailsText);
    }

    [Fact]
    public void ProcessGroup_IsSingleEntry_WithLabelledChildrenOnlyWhenExpanded()
    {
        var viewModel = CreateViewModel();
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "先想清楚" });
        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.inspect" });
        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.inspect", Success = true });

        // 思考 + 工具调用同属一个父级 foldout：消息流里只有 1 个过程条目，2 个子项
        var group = Assert.Single(viewModel.Messages, item => item.IsProcess);
        Assert.Equal(2, group.Items.Count);
        Assert.Contains("思考", group.Items[0].Label);
        Assert.Contains("工具调用", group.Items[1].Label);

        // 父组运行时默认展开；子 foldout 默认折叠并显示各自最新一行
        Assert.True(group.IsExpanded);
        Assert.All(group.Items, item => Assert.False(item.IsExpanded));
        Assert.All(group.Items, item => Assert.False(item.IsContentVisible));
        Assert.Equal("先想清楚", group.Items[0].HeaderText);
        Assert.Contains("loomx.inspect", group.Items[1].HeaderText);
        Assert.DoesNotContain("工具调用", group.Items[1].HeaderText);

        group.Items[0].IsExpanded = true;
        Assert.Equal(group.Items[0].Label, group.Items[0].HeaderText);
        Assert.Equal(90, group.Items[0].ExpandIconAngle);
        Assert.True(group.Items[0].IsContentVisible);

        group.Items[0].IsExpanded = false;
        viewModel.Project(Event(AgentEventKind.MessageCompleted) with { Message = ChatMessage.Assistant("最终答案") });
        Assert.All(group.Items, item => Assert.Equal(item.Label, item.HeaderText));

        // “已完成 / 处理中”只挂在父级，子项没有计时文案
        Assert.Contains("已完成", group.ProcessStatusText);
        Assert.All(group.Items, item => Assert.Equal($"{item.Label}: {item.Text}", item.ToString()));
    }

    [Fact]
    public void ProcessGroup_OnlyFinalMessageCompletionTurnsProcessingIntoCompleted()
    {
        var viewModel = CreateViewModel();
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "先想清楚\n再看工具" });
        var group = Assert.Single(viewModel.Messages, item => item.IsProcess);
        Assert.Contains("处理中", group.ProcessStatusText);
        Assert.False(group.IsFinished);

        // 文本仍在流式输出，尚未完成 finish content。
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "结论" });
        Assert.False(group.IsFinished);

        viewModel.Project(Event(AgentEventKind.MessageCompleted) with
        {
            Message = ChatMessage.AssistantToolCalls([new ToolCall("call-1", "loomx.inspect", "{}")], "结论"),
        });
        Assert.False(group.IsFinished);

        // 无工具调用的完整 assistant 消息才是 finish content 完成边界。
        viewModel.Project(Event(AgentEventKind.MessageCompleted) with { Message = ChatMessage.Assistant("最终结论") });
        Assert.True(group.IsFinished);
        Assert.Contains("已完成", group.ProcessStatusText);
        Assert.False(group.IsExpanded);
    }

    [Fact]
    public void NewSession_LeavesMessageStreamEmpty_NoSystemNotice()
    {
        var viewModel = CreateViewModel();
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "之前的对话" });
        Assert.NotEmpty(viewModel.Messages);

        viewModel.NewSessionCommand.Execute(null);

        // 空会话就该回到空态，不塞“新会话已开始”这类系统提示
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public void CurrentSessionTitle_FallsBackToNewSessionWhenNothingSelected()
    {
        var viewModel = CreateViewModel();

        Assert.Null(viewModel.SelectedSession);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.CurrentSessionTitle));
    }

    [Fact]
    public void ModelOption_MatchesSearchByDisplayNameOrModelId()
    {
        var option = new AssistantModelOptionViewModel("p1", "gpt-4o-mini", "GPT 4o Mini");

        Assert.True(option.MatchesSearch("mini"));
        Assert.True(option.MatchesSearch("GPT"));
        Assert.False(option.MatchesSearch("claude"));
        Assert.True(option.MatchesSearch(""));
    }

    [Fact]
    public void Project_未标记安全的旧工具参数不会进入UI详情()
    {
        const string secret = "legacy-ui-private-value";
        var viewModel = CreateViewModel();
        var message = ChatMessage.AssistantToolCalls(
            [new ToolCall("legacy-ui", "legacy.tool", $$"""{"value":"{{secret}}"}""")]);

        viewModel.Project(Event(AgentEventKind.MessageCompleted) with { Message = message });

        var process = Assert.Single(viewModel.Messages, item => item.IsProcess);
        var toolItem = Assert.Single(process.Items, item => item.Label.Contains("工具"));
        Assert.DoesNotContain(secret, toolItem.DetailsText, StringComparison.Ordinal);
        Assert.Contains("summary", toolItem.DetailsText, StringComparison.Ordinal);
    }

    private static AssistantViewModel CreateViewModel() => new(new GatewayProcessService());

    private static AgentEvent Event(AgentEventKind kind) => AgentEvent.Create("test-session", kind);
}

public sealed class AssistantViewModelUserDecisionTests
{
    [Fact]
    public async Task PendingRequested_通过UI调度提交且只调用Broker请求Id()
    {
        var broker = new RecordingUserDecisionBroker();
        var toast = new ToastService();
        var notifications = new List<ToastNotification>();
        toast.Requested += (_, notification) => notifications.Add(notification);
        var dispatchCount = 0;
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            toastService: toast,
            userDecisionBroker: broker,
            uiDispatcher: action =>
            {
                dispatchCount++;
                action();
            },
            showAskUserDialog: dialog =>
            {
                Assert.IsType<AskUserTextFieldViewModel>(Assert.Single(dialog.Fields)).TextValue = "批准";
                return Task.FromResult<bool?>(true);
            });

        viewModel.Activate();

        broker.Raise(CreatePending("submit-id", "问题正文 API Key Secret Authorization"));
        await broker.WaitForCompletionAsync();

        Assert.Equal(1, dispatchCount);
        Assert.Equal("submit-id", broker.SubmittedRequestId);
        Assert.Null(broker.CancelledRequestId);
        Assert.Equal("批准", broker.SubmittedValues!["answer"]);
        var notification = Assert.Single(notifications);
        Assert.Equal("已提交助手决策", notification.Message);
        Assert.Equal(ToastLevel.Success, notification.Level);
        Assert.DoesNotContain("批准", notification.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("问题正文", notification.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dialog关闭_按请求Id取消且不提交默认值()
    {
        var broker = new RecordingUserDecisionBroker();
        var toast = new ToastService();
        ToastNotification? notification = null;
        toast.Requested += (_, item) => notification = item;
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            toastService: toast,
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: _ => Task.FromResult<bool?>(false));

        viewModel.Activate();

        broker.Raise(CreatePending("cancel-id", "包含 Secret 的问题正文"));
        await broker.WaitForCompletionAsync();

        Assert.Equal("cancel-id", broker.CancelledRequestId);
        Assert.Null(broker.SubmittedRequestId);
        Assert.Null(broker.SubmittedValues);
        Assert.NotNull(notification);
        Assert.Equal("已取消助手决策", notification.Message);
        Assert.Equal(ToastLevel.Info, notification.Level);
        Assert.DoesNotContain("Secret", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 宿主中止AskUser_不重复取消也不显示失败Toast()
    {
        var broker = new RecordingUserDecisionBroker { CancelResult = false };
        var toast = new ToastService();
        var notifications = new List<ToastNotification>();
        toast.Requested += (_, notification) => notifications.Add(notification);
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            toastService: toast,
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: _ => Task.FromResult<bool?>(null));
        viewModel.Activate();

        broker.Raise(CreatePending("abort-id", "宿主中止问题"));
        await Task.Delay(100);

        Assert.Null(broker.CancelledRequestId);
        Assert.DoesNotContain(notifications, notification => notification.Level == ToastLevel.Error);
    }

    [Fact]
    public async Task Deactivate_解除订阅并取消当前页面请求且重复完成安全收敛()
    {
        var broker = new RecordingUserDecisionBroker();
        var toast = new ToastService();
        var notifications = new List<ToastNotification>();
        toast.Requested += (_, notification) => notifications.Add(notification);
        var dialogStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            toastService: toast,
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: _ =>
            {
                dialogStarted.TrySetResult();
                return dialogCompletion.Task;
            });
        viewModel.Activate();
        Assert.Equal(1, broker.SubscriberCount);

        broker.Raise(CreatePending("pending-id", "待处理问题"));
        await dialogStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Deactivate();
        dialogCompletion.TrySetResult(true);
        await broker.WaitForCompletionAsync();

        Assert.Equal(0, broker.SubscriberCount);
        Assert.Equal("pending-id", broker.CancelledRequestId);
        Assert.Null(broker.SubmittedRequestId);
        Assert.DoesNotContain(notifications, notification => notification.Level == ToastLevel.Error);

        viewModel.Activate();
        Assert.Equal(1, broker.SubscriberCount);
        viewModel.Dispose();
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public async Task Broker拒绝提交_仅显示固定安全错误摘要()
    {
        var broker = new RecordingUserDecisionBroker { SubmitResult = false };
        var toast = new ToastService();
        ToastNotification? notification = null;
        toast.Requested += (_, item) => notification = item;
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            toastService: toast,
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: dialog =>
            {
                Assert.IsType<AskUserTextFieldViewModel>(Assert.Single(dialog.Fields)).TextValue = "普通决定";
                return Task.FromResult<bool?>(true);
            });

        viewModel.Activate();

        broker.Raise(CreatePending("stale-id", "API Key 问题正文"));
        await broker.WaitForCompletionAsync();

        Assert.NotNull(notification);
        Assert.Equal("助手决策未能提交，请重试", notification.Message);
        Assert.Equal(ToastLevel.Error, notification.Level);
        Assert.DoesNotContain("普通决定", notification.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("API Key", notification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 真实Broker_多个ViewModel只有一个Claim且未Claim实例停用不影响请求()
    {
        using var broker = new UserDecisionBroker(NullLogger<UserDecisionBroker>.Instance);
        var dialogStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCount = 0;

        Task<bool?> ShowDialog(int index, AskUserDialogViewModel dialog)
        {
            Assert.IsType<AskUserTextFieldViewModel>(Assert.Single(dialog.Fields)).TextValue = $"处理者-{index}";
            Interlocked.Increment(ref dialogCount);
            dialogStarted.TrySetResult(index);
            return dialogCompletion.Task;
        }

        using var first = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: dialog => ShowDialog(1, dialog));
        using var second = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: dialog => ShowDialog(2, dialog));
        first.Activate();
        second.Activate();

        var task = broker.RequestAsync("assistant-run", CreatePending("unused", "问题正文").Request, CancellationToken.None);
        var owner = await dialogStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref dialogCount));

        if (owner == 1) second.Deactivate(); else first.Deactivate();
        Assert.False(task.IsCompleted);

        dialogCompletion.TrySetResult(true);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Cancelled);
        Assert.Equal($"处理者-{owner}", result.Values["answer"]);
        Assert.Equal(1, Volatile.Read(ref dialogCount));
    }

    [Fact]
    public async Task 真实Broker_提交与Deactivate竞态只完成一次并收敛()
    {
        using var broker = new UserDecisionBroker(NullLogger<UserDecisionBroker>.Instance);
        var dialogStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: dialog =>
            {
                Assert.IsType<AskUserTextFieldViewModel>(Assert.Single(dialog.Fields)).TextValue = "完成";
                dialogStarted.TrySetResult();
                return dialogCompletion.Task;
            });
        viewModel.Activate();
        var task = broker.RequestAsync("assistant-run", CreatePending("unused", "问题正文").Request, CancellationToken.None);
        await dialogStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var start = new ManualResetEventSlim();

        var deactivate = Task.Run(() =>
        {
            start.Wait();
            viewModel.Deactivate();
        });
        var submit = Task.Run(() =>
        {
            start.Wait();
            dialogCompletion.TrySetResult(true);
        });

        start.Set();
        await Task.WhenAll(deactivate, submit);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Cancelled || Equals(result.Values["answer"], "完成"));
    }

    [Fact]
    public async Task 用户决策异常日志_记录安全异常对象且不泄露敏感内容()
    {
        const string sensitive = "Authorization Bearer API Key Secret 用户自由文本";
        var loggerFactory = new RecordingLoggerFactory();
        using var broker = new UserDecisionBroker(NullLogger<UserDecisionBroker>.Instance);
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            loggerFactory: loggerFactory,
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: _ => throw new InvalidOperationException(sensitive));
        viewModel.Activate();

        var result = await broker.RequestAsync(
            "owner-sensitive-id",
            CreatePending("unused", "安全问题").Request,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Cancelled);
        Assert.Contains(loggerFactory.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is not null);
        var logText = string.Join("\n", loggerFactory.Entries.Select(entry => entry.Text));
        Assert.DoesNotContain(sensitive, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("owner-sensitive-id", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void 输入区单按钮根据运行状态与输入内容切换发送和停止()
    {
        using var viewModel = new AssistantViewModel(new GatewayProcessService());

        Assert.False(viewModel.IsComposerStopAction);
        Assert.False(viewModel.IsComposerActionEnabled);

        viewModel.InputText = "排队消息";
        Assert.False(viewModel.IsComposerStopAction);
        Assert.True(viewModel.IsComposerActionEnabled);

        SetPrivateField(viewModel, "isRunning", true);
        viewModel.InputText = string.Empty;
        Assert.True(viewModel.IsComposerStopAction);
        Assert.True(viewModel.IsComposerActionEnabled);

        viewModel.InputText = "运行中追加消息";
        Assert.False(viewModel.IsComposerStopAction);
        Assert.True(viewModel.IsComposerActionEnabled);
    }

    [Fact]
    public async Task 输入区停止状态会关闭活动AskUser并取消已Claim请求()
    {
        var broker = new RecordingUserDecisionBroker();
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action());
        viewModel.Activate();
        SetPrivateField(viewModel, "isRunning", true);

        broker.Raise(CreatePending("composer-stop", "停止前问题"));
        Assert.True(SpinWait.SpinUntil(() => viewModel.PendingAskUser is not null, TimeSpan.FromSeconds(2)));
        Assert.True(viewModel.IsComposerStopAction);

        viewModel.ComposerActionCommand.Execute(null);
        await broker.WaitForCompletionAsync();

        Assert.Equal("composer-stop", broker.CancelledRequestId);
        Assert.Null(broker.SubmittedRequestId);
        Assert.True(SpinWait.SpinUntil(() => viewModel.PendingAskUser is null, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task PendingRequested_默认投影到输入框上方卡片并由卡片完成提交()
    {
        var broker = new RecordingUserDecisionBroker();
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action());
        viewModel.Activate();

        broker.Raise(CreatePending("card-id", "卡片问题"));
        Assert.True(SpinWait.SpinUntil(() => viewModel.PendingAskUser is not null, TimeSpan.FromSeconds(2)));
        var card = Assert.IsType<AskUserDialogViewModel>(viewModel.PendingAskUser);
        Assert.IsType<AskUserTextFieldViewModel>(card.CurrentField).TextValue = "确认";
        Assert.True(card.TryCompleteSubmission());
        await broker.WaitForCompletionAsync();

        Assert.Equal("card-id", broker.SubmittedRequestId);
        Assert.Equal("确认", broker.SubmittedValues!["answer"]);
        Assert.True(SpinWait.SpinUntil(() => viewModel.PendingAskUser is null, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task 会话切换会终止活动AskUser并取消已Claim请求()
    {
        var broker = new RecordingUserDecisionBroker();
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action());
        viewModel.Activate();

        broker.Raise(CreatePending("switch-session", "切换前问题"));
        Assert.True(SpinWait.SpinUntil(() => viewModel.PendingAskUser is not null, TimeSpan.FromSeconds(2)));

        viewModel.PrepareSessionSwitchForTesting();
        await broker.WaitForCompletionAsync();

        Assert.Equal("switch-session", broker.CancelledRequestId);
        Assert.Null(broker.SubmittedRequestId);
        Assert.True(SpinWait.SpinUntil(() => viewModel.PendingAskUser is null, TimeSpan.FromSeconds(2)));
    }
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static PendingUserDecision CreatePending(string requestId, string question) => new(
        requestId,
        "owner-sensitive-id",
        new UserDecisionRequest(
            "确认",
            question,
            [new UserDecisionField("answer", "回答", UserDecisionFieldType.Text, isRequired: true, defaultText: "默认值")]));


    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<LogEntry> entries = new();

        public IReadOnlyCollection<LogEntry> Entries => entries.ToArray();

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(entries);

        public void Dispose() { }
    }

    private sealed class RecordingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var stateText = state is IEnumerable<KeyValuePair<string, object?>> properties
                ? string.Join(" | ", properties.Select(property => $"{property.Key}={property.Value}"))
                : state?.ToString() ?? string.Empty;
            entries.Enqueue(new LogEntry(
                logLevel,
                exception,
                string.Join(" | ", formatter(state, exception), stateText, exception?.ToString() ?? string.Empty)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Text);


    private sealed class RecordingUserDecisionBroker : IUserDecisionBroker
    {
        private EventHandler<PendingUserDecision>? pendingRequested;
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SubscriberCount { get; private set; }
        public bool SubmitResult { get; init; } = true;
        public bool CancelResult { get; init; } = true;
        public string? SubmittedRequestId { get; private set; }
        public IReadOnlyDictionary<string, object?>? SubmittedValues { get; private set; }
        public string? CancelledRequestId { get; private set; }

        public event EventHandler<PendingUserDecision>? PendingRequested
        {
            add
            {
                pendingRequested += value;
                SubscriberCount++;
            }
            remove
            {
                pendingRequested -= value;
                SubscriberCount--;
            }
        }

        public Task<UserDecisionResult> RequestAsync(string ownerId, UserDecisionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private string? claimantId;

        public bool TryClaim(string requestId, string candidate)
        {
            if (claimantId is not null)
            {
                return false;
            }

            claimantId = candidate;
            return true;
        }

        public bool Release(string requestId, string candidate)
        {
            if (!string.Equals(claimantId, candidate, StringComparison.Ordinal))
            {
                return false;
            }

            claimantId = null;
            return true;
        }

        public bool Submit(
            string requestId,
            string candidate,
            IReadOnlyDictionary<string, object?> values)
        {
            if (!string.Equals(claimantId, candidate, StringComparison.Ordinal))
            {
                return false;
            }

            SubmittedRequestId = requestId;
            SubmittedValues = values;
            completion.TrySetResult();
            return SubmitResult;
        }

        public bool Cancel(string requestId, string candidate, string reason)
        {
            if (!string.Equals(claimantId, candidate, StringComparison.Ordinal))
            {
                return false;
            }

            if (!CancelResult)
            {
                return false;
            }

            CancelledRequestId = requestId;
            completion.TrySetResult();
            return true;
        }

        public int CancelOwner(string ownerId, string reason) => 0;

        public void Dispose()
        {
        }

        public void Raise(PendingUserDecision pending) => pendingRequested?.Invoke(this, pending);

        public Task WaitForCompletionAsync() => completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
