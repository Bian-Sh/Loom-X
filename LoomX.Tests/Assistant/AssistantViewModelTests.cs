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
    public void Project_TextDelta_CreatesStreamingMessageAndAppends()
    {
        var viewModel = CreateViewModel();
        var baseline = viewModel.Messages.Count;

        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "你好，" });
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "世界。" });

        Assert.Equal(baseline + 1, viewModel.Messages.Count);
        var message = viewModel.Messages[^1];
        Assert.Equal("你好，世界。", message.Text);
        Assert.True(message.IsStreaming);
        Assert.True(message.IsAssistantMessage);
        Assert.Equal("你好，世界。", message.Markdown.ToString());
    }

    [Fact]
    public void Project_ToolEvents_AggregatesCollapsedSteps()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.list_providers" });
        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.list_providers", Success = true });

        var steps = Assert.Single(viewModel.Messages, message => message.IsProcess);
        Assert.False(steps.IsExpanded);
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
    public void Project_TaskFailed_AddsWarningStatus()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.TaskFailed) with { Detail = "超过最大步骤数 16。" });

        Assert.Contains(viewModel.Messages, message => message.IsStatus && message.Text.Contains("任务失败"));
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
    public void Project_ReasoningAndSteps_DefaultCollapsed_CompletionCollapsesExpandedProcess()
    {
        var viewModel = CreateViewModel();
        viewModel.Project(Event(AgentEventKind.StepStarted));
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "原始思考" });
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "摘要", IsSummary = true });
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "先检查" });
        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.inspect" });

        // 思考与工具调用归并进同一个父级 foldout，正文之前只有一个过程条目
        var processes = viewModel.Messages.Where(item => item.IsProcess).ToArray();
        Assert.Equal(2, processes.Length);
        Assert.All(processes, item => Assert.False(item.IsExpanded));
        Assert.Equal(2, processes[0].Items.Count);   // 思考 + 摘要
        Assert.Contains("原始思考", processes[0].ItemsText);
        Assert.Single(processes[1].Items);           // 处理步骤

        processes[0].IsExpanded = true;
        processes[1].IsExpanded = true;
        viewModel.Project(Event(AgentEventKind.StepStarted));
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "**完成**" });
        viewModel.Project(Event(AgentEventKind.TaskCompleted));

        Assert.All(processes, item => Assert.False(item.IsExpanded));
        Assert.Equal("**完成**", viewModel.Messages.Last(item => item.IsAssistantMessage).Markdown.ToString());
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
        Assert.Contains("步骤", group.Items[1].Label);

        // 折叠时不显示标签，只滚动显示内部最新一行
        Assert.False(group.IsExpanded);
        Assert.Contains("loomx.inspect", group.ProcessPreview);
        Assert.DoesNotContain("步骤", group.ProcessPreview);

        // “已完成 / 已处理”只挂在父级，子项没有计时文案
        Assert.Contains("已处理", group.ProcessStatusText);
        Assert.All(group.Items, item => Assert.Equal($"{item.Label}: {item.Text}", item.ToString()));
    }

    [Fact]
    public void ProcessGroup_FinishContentTurnsElapsedIntoCompletedAndCollapses()
    {
        var viewModel = CreateViewModel();
        viewModel.Project(Event(AgentEventKind.ReasoningDelta) with { Text = "先想清楚\n再看工具" });
        var group = Assert.Single(viewModel.Messages, item => item.IsProcess);
        Assert.Contains("已处理", group.ProcessStatusText);
        Assert.False(group.IsFinished);

        // 正文开始输出 = finish content
        viewModel.Project(Event(AgentEventKind.TextDelta) with { Text = "结论" });

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
