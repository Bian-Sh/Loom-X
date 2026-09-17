using System.Collections;
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
        broker.PendingRequested += (_, request) =>
        {
            broker.TryClaim(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId);
            pending.Add(request);
        };

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
            broker.TryClaim(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId);
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
        broker.PendingRequested += (_, request) =>
        {
            broker.TryClaim(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId);
            pending.Add(request);
        };
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
    public async Task 无订阅者_请求立即安全失败且不遗留Pending()
    {
        var broker = CreateBroker();

        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("当前没有可用的用户决策处理器。", exception.Message);
        Assert.Equal(0, broker.CancelOwner("owner", "检查残留"));
    }


    [Fact]
    public async Task 订阅者返回前未Claim_请求立即安全失败且不遗留Pending()
    {
        using var broker = CreateBroker();
        broker.PendingRequested += (_, _) => { };

        var task = broker.RequestAsync("owner-no-claim", CreateRequest(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("当前没有可用的用户决策处理器。", exception.Message);
        Assert.False(broker.CancelOwner("owner-no-claim", "cleanup") > 0);
    }

    [Fact]
    public async Task 事件快照后订阅者解除且旧Handler未Claim_请求仍立即收敛()
    {
        using var broker = CreateBroker();
        var staleInvoked = false;
        EventHandler<PendingUserDecision>? stale = null;
        stale = (_, _) => staleInvoked = true;
        EventHandler<PendingUserDecision> unsubscribe = (_, _) => broker.PendingRequested -= stale;
        broker.PendingRequested += unsubscribe;
        broker.PendingRequested += stale;

        var task = broker.RequestAsync("owner-stale", CreateRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(staleInvoked);
        Assert.Equal(0, broker.CancelOwner("owner-stale", "cleanup"));
    }

    [Fact]
    public async Task Dispose_取消并移除所有Pending()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        broker.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = "完成" }));
        Assert.False(broker.Cancel(request.RequestId, "重复取消"));
        Assert.Equal(0, broker.CancelOwner("owner", "检查残留"));
    }

    [Fact]
    public void Dispose后请求_抛出ObjectDisposedException()
    {
        var broker = CreateBroker();
        broker.PendingRequested += (_, _) => { };
        broker.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        });
    }

    [Fact]
    public async Task Dispose与Submit并发_请求只完成一次且不悬挂()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;
        using var start = new ManualResetEventSlim();
        var submit = Task.Run(() =>
        {
            start.Wait();
            return broker.Submit(
                request.RequestId,
                new Dictionary<string, object?> { ["note"] = "完成" });
        });
        var dispose = Task.Run(() =>
        {
            start.Wait();
            broker.Dispose();
        });

        start.Set();
        await dispose;
        var submitted = await submit;

        if (submitted)
        {
            Assert.Equal("完成", (await task.WaitAsync(TimeSpan.FromSeconds(5))).Values["note"]);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        broker.Dispose();
    }

    [Fact]
    public async Task Submit_调用方字典和多选值只枚举一次()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateMultiRequest(), CancellationToken.None);
        var request = await pending.Task;
        var selected = new SingleEnumerationSequence("safe");
        var values = new SingleEnumerationDictionary("features", selected);

        Assert.True(broker.Submit(request.RequestId, values));

        var result = await task;
        Assert.Equal(1, values.EnumerationCount);
        Assert.Equal(1, selected.EnumerationCount);
        Assert.Equal(
            ["safe"],
            Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Values["features"]));
    }

    [Fact]
    public async Task Submit_快照失败后请求仍可重新提交()
    {
        var logger = new RecordingLogger<UserDecisionBroker>();
        var broker = new UserDecisionBroker(logger);
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        Assert.False(broker.Submit(
            request.RequestId,
            new ThrowingEnumerationDictionary()));
        Assert.True(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = "有效内容" }));

        Assert.Equal("有效内容", (await task).Values["note"]);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("Authorization", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Exceptions, exception => exception.Contains("Bearer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Submit_敏感文本被拒绝且不会进入结果()
    {
        const string sensitive = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456";
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        Assert.False(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = sensitive }));
        Assert.True(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = "安全内容" }));

        var result = await task;
        Assert.DoesNotContain(result.Values.Values, value =>
            string.Equals(value as string, sensitive, StringComparison.Ordinal));
        Assert.Equal("安全内容", result.Values["note"]);
    }

    [Fact]
    public async Task Submit与Cancel并发_只有一个完成请求()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;
        using var start = new ManualResetEventSlim();
        var submit = Task.Run(() =>
        {
            start.Wait();
            return broker.Submit(
                request.RequestId,
                new Dictionary<string, object?> { ["note"] = "完成" });
        });
        var cancel = Task.Run(() =>
        {
            start.Wait();
            return broker.Cancel(request.RequestId, "并发取消");
        });

        start.Set();
        var outcomes = await Task.WhenAll(submit, cancel);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, outcomes.Count(value => value));
        Assert.Equal(outcomes[1], result.Cancelled);
    }

    [Fact]
    public async Task Submit与Token取消并发_请求总能收敛()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        using var cancellation = new CancellationTokenSource();
        var task = broker.RequestAsync("owner", CreateRequest(), cancellation.Token);
        var request = await pending.Task;
        using var start = new ManualResetEventSlim();
        var submit = Task.Run(() =>
        {
            start.Wait();
            return broker.Submit(
                request.RequestId,
                new Dictionary<string, object?> { ["note"] = "完成" });
        });
        var cancel = Task.Run(() =>
        {
            start.Wait();
            cancellation.Cancel();
        });

        start.Set();
        await cancel;
        var submitted = await submit;

        if (submitted)
        {
            Assert.Equal("完成", (await task.WaitAsync(TimeSpan.FromSeconds(5))).Values["note"]);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        Assert.False(broker.Cancel(request.RequestId, "重复取消"));
    }

    [Fact]
    public async Task 日志_不包含用户提交值或取消原因()
    {
        var logger = new RecordingLogger<UserDecisionBroker>();
        var broker = new UserDecisionBroker(logger);
        var pending = new ConcurrentQueue<PendingUserDecision>();
        broker.PendingRequested += (_, request) =>
        {
            broker.TryClaim(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId);
            pending.Enqueue(request);
        };

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

    [Fact]
    public async Task 日志_不包含原始OwnerId的状态消息或异常()
    {
        const string ownerId = "Authorization=Bearer owner-token C:\\Users\\测试\\会话 用户文本";
        var logger = new RecordingLogger<UserDecisionBroker>();
        var broker = new UserDecisionBroker(logger);
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync(ownerId, CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        Assert.True(broker.Submit(
            request.RequestId,
            new Dictionary<string, object?> { ["note"] = "安全内容" }));
        await task;

        Assert.DoesNotContain(logger.Messages, message => message.Contains(ownerId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.States, state => state.Contains(ownerId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Exceptions, exception => exception.Contains(ownerId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("owner-token", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.States, state => state.Contains("owner-token", StringComparison.Ordinal));
    }


    [Fact]
    public async Task TryClaim_同一请求只有一个处理者且未Claim处理者不能完成或释放()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;
        Assert.True(broker.Release(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId));
        using var start = new ManualResetEventSlim();

        var first = Task.Run(() =>
        {
            start.Wait();
            return broker.TryClaim(request.RequestId, "ui-first");
        });
        var second = Task.Run(() =>
        {
            start.Wait();
            return broker.TryClaim(request.RequestId, "ui-second");
        });

        start.Set();
        var claims = await Task.WhenAll(first, second);
        Assert.Single(claims, claimed => claimed);

        var winner = claims[0] ? "ui-first" : "ui-second";
        var loser = claims[0] ? "ui-second" : "ui-first";
        var values = new Dictionary<string, object?> { ["note"] = "完成" };

        Assert.False(broker.Submit(request.RequestId, loser, values));
        Assert.False(broker.Cancel(request.RequestId, loser, "未拥有请求"));
        Assert.False(broker.Release(request.RequestId, loser));
        Assert.True(broker.Release(request.RequestId, winner));
        Assert.True(broker.TryClaim(request.RequestId, loser));
        Assert.True(broker.Cancel(request.RequestId, loser, "测试结束"));
        Assert.True((await task).Cancelled);
    }

    [Fact]
    public async Task Submit进行中_不能释放Claim给其他处理者()
    {
        var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;
        Assert.True(broker.Release(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId));
        Assert.True(broker.TryClaim(request.RequestId, "ui-first"));
        var values = new BlockingEnumerationDictionary("note", "完成");

        var submit = Task.Run(() => broker.Submit(request.RequestId, "ui-first", values));
        await values.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(broker.Release(request.RequestId, "ui-first"));
        Assert.False(broker.TryClaim(request.RequestId, "ui-second"));
        values.AllowEnumeration.TrySetResult();

        Assert.True(await submit.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("完成", (await task.WaitAsync(TimeSpan.FromSeconds(5))).Values["note"]);
    }

    [Fact]
    public async Task Claim日志_不包含处理者标识或用户内容()
    {
        const string claimantId = "Authorization=Bearer ui-secret 用户自由文本";
        var logger = new RecordingLogger<UserDecisionBroker>();
        var broker = new UserDecisionBroker(logger);
        var pending = CaptureNext(broker, claimantId);
        var task = broker.RequestAsync("owner", CreateRequest(), CancellationToken.None);
        var request = await pending.Task;

        Assert.True(broker.Cancel(request.RequestId, claimantId, "Secret 取消原因"));
        await task;

        Assert.DoesNotContain(logger.Messages, message => message.Contains(claimantId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.States, state => state.Contains(claimantId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Exceptions, exception => exception.Contains(claimantId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("Secret 取消原因", StringComparison.Ordinal));
    }

    private static UserDecisionBroker CreateBroker() =>
        new(NullLogger<UserDecisionBroker>.Instance);

    private static TaskCompletionSource<PendingUserDecision> CaptureNext(
        UserDecisionBroker broker,
        string claimantId = UserDecisionBrokerTestExtensions.ClaimantId)
    {
        var completion = new TaskCompletionSource<PendingUserDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        broker.PendingRequested += (_, request) =>
        {
            broker.TryClaim(request.RequestId, claimantId);
            completion.TrySetResult(request);
        };
        return completion;
    }

    private static UserDecisionRequest CreateMultiRequest() =>
        new(
            "确认设置",
            "请选择能力。",
            [
                new UserDecisionField(
                    id: "features",
                    label: "能力",
                    type: UserDecisionFieldType.MultiSelect,
                    options: [new UserDecisionOption("safe", "安全")],
                    minSelections: 1,
                    maxSelections: 1),
            ]);

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

    private sealed class BlockingEnumerationDictionary(string key, object? value)
        : IReadOnlyDictionary<string, object?>
    {
        public TaskCompletionSource EnumerationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowEnumeration { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count => 1;

        public IEnumerable<string> Keys => [key];

        public IEnumerable<object?> Values => [value];

        public object? this[string requestedKey] =>
            string.Equals(requestedKey, key, StringComparison.Ordinal) ? value : throw new KeyNotFoundException();

        public bool ContainsKey(string requestedKey) =>
            string.Equals(requestedKey, key, StringComparison.Ordinal);

        public bool TryGetValue(string requestedKey, out object? requestedValue)
        {
            if (ContainsKey(requestedKey))
            {
                requestedValue = value;
                return true;
            }

            requestedValue = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            EnumerationStarted.TrySetResult();
            AllowEnumeration.Task.GetAwaiter().GetResult();
            yield return new KeyValuePair<string, object?>(key, value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleEnumerationDictionary(string key, object? value)
        : IReadOnlyDictionary<string, object?>
    {
        private int enumerationCount;

        public int EnumerationCount => Volatile.Read(ref enumerationCount);

        public int Count => throw new InvalidOperationException("不允许读取 Count。");

        public IEnumerable<string> Keys => throw new InvalidOperationException("不允许读取 Keys。");

        public IEnumerable<object?> Values => throw new InvalidOperationException("不允许读取 Values。");

        public object? this[string requestedKey] => throw new InvalidOperationException("不允许读取索引器。");

        public bool ContainsKey(string requestedKey) =>
            throw new InvalidOperationException("不允许调用 ContainsKey。");

        public bool TryGetValue(string requestedKey, out object? requestedValue) =>
            throw new InvalidOperationException("不允许调用 TryGetValue。");

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            if (Interlocked.Increment(ref enumerationCount) != 1)
            {
                throw new InvalidOperationException("字典被重复枚举。");
            }

            yield return new KeyValuePair<string, object?>(key, value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingEnumerationDictionary : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;

        public IEnumerable<string> Keys => ["note"];

        public IEnumerable<object?> Values => ["不应读取"];

        public object? this[string key] => "不应读取";

        public bool ContainsKey(string key) => true;

        public bool TryGetValue(string key, out object? value)
        {
            value = "不应读取";
            return true;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            throw new InvalidOperationException("Authorization: Bearer 不得记录");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleEnumerationSequence(string value) : IEnumerable<string>
    {
        private int enumerationCount;

        public int EnumerationCount => Volatile.Read(ref enumerationCount);

        public IEnumerator<string> GetEnumerator()
        {
            if (Interlocked.Increment(ref enumerationCount) != 1)
            {
                throw new InvalidOperationException("多选值被重复枚举。");
            }

            yield return value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> messages = new();
        private readonly ConcurrentQueue<string> states = new();
        private readonly ConcurrentQueue<string> exceptions = new();

        public IReadOnlyCollection<string> Messages => messages.ToArray();

        public IReadOnlyCollection<string> States => states.ToArray();

        public IReadOnlyCollection<string> Exceptions => exceptions.ToArray();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
            states.Enqueue(state is IEnumerable<KeyValuePair<string, object?>> properties
                ? string.Join(" | ", properties.Select(property => $"{property.Key}={property.Value}"))
                : state?.ToString() ?? string.Empty);
            exceptions.Enqueue(exception?.ToString() ?? string.Empty);
        }
    }
}
