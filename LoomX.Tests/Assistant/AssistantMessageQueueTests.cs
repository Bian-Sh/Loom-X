using LoomX.ViewModels;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class AssistantMessageQueueTests
{
    [Fact]
    public void 运行中发送仍可执行并加入当前会话队列()
    {
        using var viewModel = new AssistantViewModel(new GatewayProcessService());
        viewModel.SetActiveRunForTesting("session-a");
        viewModel.InputText = "后续消息";

        Assert.False(viewModel.IsComposerStopAction);
        Assert.True(viewModel.IsComposerActionEnabled);
        viewModel.ComposerActionCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(() => viewModel.QueuedMessages.Count == 1, TimeSpan.FromSeconds(2)));
        var queued = Assert.Single(viewModel.QueuedMessages);
        Assert.Equal("session-a", queued.SessionId);
        Assert.Equal("后续消息", queued.Text);
        Assert.Equal(1, queued.DisplayOrder);
        Assert.True(queued.IsCurrentSession);
        Assert.Equal(string.Empty, viewModel.InputText);
    }

    [Fact]
    public void 未发送消息支持删除且不进入正式消息历史()
    {
        using var viewModel = new AssistantViewModel(new GatewayProcessService());
        viewModel.EnqueueMessageForTesting("session-a", "可以删除");
        viewModel.ShowQueueForSessionForTesting("session-a");
        var queued = Assert.Single(viewModel.QueuedMessages);

        queued.DeleteCommand.Execute(null);

        Assert.Empty(viewModel.QueuedMessages);
        Assert.Empty(viewModel.Messages);
        Assert.False(viewModel.HasQueuedMessages);
    }

    [Fact]
    public void 队列按会话FIFO出队且失败时暂停()
    {
        using var viewModel = new AssistantViewModel(new GatewayProcessService());
        viewModel.EnqueueMessageForTesting("session-a", "第一条");
        viewModel.EnqueueMessageForTesting("session-b", "其他会话");
        viewModel.EnqueueMessageForTesting("session-a", "第二条");

        Assert.Null(viewModel.TakeNextQueuedMessageForTesting("session-a", previousTurnSucceeded: false));
        Assert.Equal("第一条", viewModel.TakeNextQueuedMessageForTesting("session-a", previousTurnSucceeded: true)?.Text);
        Assert.Equal("第二条", viewModel.TakeNextQueuedMessageForTesting("session-a", previousTurnSucceeded: true)?.Text);
        Assert.Equal("其他会话", Assert.Single(viewModel.QueuedMessages).Text);
    }

    [Fact]
    public void 切换会话只投影目标会话队列且不会跨会话误发()
    {
        using var viewModel = new AssistantViewModel(new GatewayProcessService());
        viewModel.EnqueueMessageForTesting("session-a", "A1");
        viewModel.EnqueueMessageForTesting("session-b", "B1");

        viewModel.ShowQueueForSessionForTesting("session-b");

        Assert.False(viewModel.QueuedMessages[0].IsCurrentSession);
        Assert.True(viewModel.QueuedMessages[1].IsCurrentSession);
        Assert.Equal(1, viewModel.QueuedMessages[1].DisplayOrder);
        Assert.True(viewModel.HasQueuedMessages);
    }
}
