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
    public void Project_ToolEvents_AddStatusMessages()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.ToolCallStarted) with { ToolName = "loomx.list_providers" });
        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.list_providers", Success = true });

        var statusMessages = viewModel.Messages.Where(message => message.IsStatus).ToArray();
        Assert.Contains(statusMessages, message => message.Text.Contains("loomx.list_providers") && message.Text.Contains("调用工具"));
        Assert.Contains(statusMessages, message => message.Text.Contains("完成"));
    }

    [Fact]
    public void Project_ToolFailed_IncludesDetail()
    {
        var viewModel = CreateViewModel();

        viewModel.Project(Event(AgentEventKind.ToolCallCompleted) with { ToolName = "loomx.test_provider", Success = false, Detail = "auth_failed" });

        var last = viewModel.Messages[^1];
        Assert.True(last.IsStatus);
        Assert.Contains("auth_failed", last.Text);
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

        Assert.Contains(viewModel.Messages, message => message.IsStatus && message.Text.Contains("等待你批准") && message.Text.Contains("loomx.update_provider"));
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
