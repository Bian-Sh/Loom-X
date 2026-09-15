using Xunit;
using LoomX.Assistant;
using LoomX.ViewModels;
using LoomX.Services;

namespace LoomX.Tests.Assistant;

/// <summary>
/// Chat UI 事件投影：AgentEvent → 消息流。验证 UI 只依赖事件，不依赖 Harness 内部对象。
/// </summary>
public sealed class AssistantViewModelTests
{
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
        var toolCall = new ToolCall("call-42", "loomx.inspect", """{"path":"D:/demo","depth":3}""");

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

    private static AssistantViewModel CreateViewModel() => new(new GatewayProcessService());

    private static AgentEvent Event(AgentEventKind kind) => AgentEvent.Create("test-session", kind);
}
