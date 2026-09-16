using System.Collections.Concurrent;
using LoomX.Assistant.UserDecisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class UserDecisionBrokerTests
{
    [Fact]
    public async Task 并发请求_分配唯一请求标识()
    {
        var broker = CreateBroker();
        var pending = new ConcurrentBag<PendingUserDecision>();
        var tasks = new ConcurrentBag<Task<UserDecisionResult>>();
        broker.PendingRequested += (_, request) => pending.Add(request);

        Parallel.For(0, 64, index =>
        {
            tasks.Add(broker.RequestAsync($"owner-{index}", CreateRequest(), CancellationToken.None));
        });

        Assert.Equal(64, pending.Count);
        Assert.Equal(64, pending.Select(item => item.RequestId).Distinct(StringComparer.Ordinal).Count());

        foreach (var request in pending)
        {
            Assert.True(broker.Cancel(request.RequestId, "测试结束"));
        }

        Assert.All(await Task.WhenAll(tasks), result => Assert.True(result.Cancelled));
    }

    [Fact]
    public async Task 单个请求_Pending事件只发布一次()
    {
        var broker = CreateBroker();
        var publishCount = 0;
        PendingUserDecision? pending = null;
        broker.PendingRequested += (_, request) =>
        {
            Interlocked.Increment(ref publishCount);
            pending = request;
        };

        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);

        Assert.Equal(1, Volatile.Read(ref publishCount));
        Assert.NotNull(pending);
        Assert.True(broker.Submit(
            pending.RequestId,
            new Dictionary<string, object?> { ["note"] = "已确认" }));
        Assert.False((await task).Cancelled);
        Assert.Equal(1, Volatile.Read(ref publishCount));
    }

    [Fact]
    public async Task Submit_返回结构化结果并拒绝重复完成()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;
        var values = new Dictionary<string, object?> { ["note"] = "用户决定" };

        Assert.True(broker.Submit(request.RequestId, values));
        Assert.False(broker.Submit(request.RequestId, values));
        Assert.False(broker.Cancel(request.RequestId, "重复取消"));

        var result = await task;
        Assert.False(result.Cancelled);
        Assert.Equal("用户决定", result.Values["note"]);
    }

    [Fact]
    public async Task Cancel_返回不含字段值的取消结果()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        Assert.True(broker.Cancel(request.RequestId, "页面关闭"));

        var result = await task;
        Assert.True(result.Cancelled);
        Assert.Equal("页面关闭", result.CancellationReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task CancellationToken_取消任务并移除Pending()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        using var cancellation = new CancellationTokenSource();
        var task = broker.RequestAsync("owner", CreateRequest(), cancellation.Token);
        var request = await pending.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(broker.Cancel(request.RequestId, "重复取消"));
    }

    [Fact]
    public async Task CancelOwner_只取消指定Owner的请求()
    {
        var broker = CreateBroker();
        var pending = new ConcurrentBag<PendingUserDecision>();
        broker.PendingRequested += (_, request) => pending.Add(request);
        var first = broker.RequestAsync("page-a", CreateRequest(), CancellationToken.None);
        var second = broker.RequestAsync("page-a", CreateRequest(), CancellationToken.None);
        var other = broker.RequestAsync("page-b", CreateRequest(), CancellationToken.None);

        Assert.Equal(2, broker.CancelOwner("page-a", "页面卸载"));

        Assert.All(await Task.WhenAll(first, second), result => Assert.True(result.Cancelled));
        var otherPending = Assert.Single(pending, item => item.OwnerId == "page-b");
        Assert.True(broker.Submit(
            otherPending.RequestId,
            new Dictionary<string, object?> { ["note"] = "保留" }));
        Assert.False((await other).Cancelled);
        Assert.Equal(0, broker.CancelOwner("page-a", "重复取消"));
    }

    [Fact]
    public async Task 事件订阅者抛异常_请求失败且不遗留Pending()
    {
        var logger = new RecordingLogger<UserDecisionBroker>();
        var broker = new UserDecisionBroker(logger);
        string? requestId = null;
        broker.PendingRequested += (_, request) =>
        {
            requestId = request.RequestId;
            throw new InvalidOperationException("订阅者失败：绝密自由文本");
        };

        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("无法发布用户决策请求。", exception.Message);
        Assert.NotNull(requestId);
        Assert.False(broker.Cancel(requestId, "检查残留"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("绝密自由文本", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 完成请求_Continuation不会内联执行()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;
        var gate = new object();
        var continuationRan = false;
        var continuation = task.ContinueWith(
            _ =>
            {
                lock (gate)
                {
                    continuationRan = true;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        lock (gate)
        {
            Assert.True(broker.Submit(
                request.RequestId,
                new Dictionary<string, object?> { ["note"] = "完成" }));
            Assert.False(continuationRan);
        }

        await continuation;
        Assert.True(continuationRan);
    }

    [Fact]
    public async Task 无效提交_返回False并允许随后重新提交()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        Assert.False(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = "   " }));
        Assert.True(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = "有效内容" }));

        Assert.Equal("有效内容", (await task).Values["note"]);
    }

    [Fact]
    public async Task 日志_不包含用户提交值或取消原因()
    {
        var logger = new RecordingLogger<UserDecisionBroker>();
        var broker = new UserDecisionBroker(logger);
        var pending = new ConcurrentQueue<PendingUserDecision>();
        broker.PendingRequested += (_, request) => pending.Enqueue(request);

        var submitted = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        Assert.True(pending.TryDequeue(out var submitRequest));
        Assert.True(broker.Submit(
            submitRequest.RequestId,
            new Dictionary<string, object?> { ["note"] = "绝密用户自由文本" }));
        await submitted;

        var cancelled = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        Assert.True(pending.TryDequeue(out var cancelRequest));
        Assert.True(broker.Cancel(cancelRequest.RequestId, "包含私密原因"));
        await cancelled;

        Assert.DoesNotContain(logger.Messages, message => message.Contains("绝密用户自由文本", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("包含私密原因", StringComparison.Ordinal));
    }

    private static UserDecisionBroker CreateBroker() =>
        new(NullLogger<UserDecisionBroker>.Instance);

    private static TaskCompletionSource<PendingUserDecision> CaptureNext(UserDecisionBroker broker)
    {
        var completion = new TaskCompletionSource<PendingUserDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        broker.PendingRequested += (_, request) => completion.TrySetResult(request);
        return completion;
    }

    private static UserDecisionRequest CreateRequest() =>
        new(
            "确认设置",
            "请补充说明。",
            [
                new UserDecisionField(
                    id: "note",
                    label: "说明",
                    type: UserDecisionFieldType.Text,
                    isRequired: true,
                    maxLength: 100),
            ]);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages => messages.ToArray();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Enqueue($"{formatter(state, exception)} {exception}");
    }
}
