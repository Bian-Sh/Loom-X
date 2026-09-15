using System.Runtime.CompilerServices;
using LoomX.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class AgentLoopRetryTests
{
    [Theory]
    [InlineData(ModelErrorKind.ConnectionFailed, null)]
    [InlineData(ModelErrorKind.Timeout, 408)]
    [InlineData(ModelErrorKind.RateLimited, 429)]
    [InlineData(ModelErrorKind.ServerError, 500)]
    [InlineData(ModelErrorKind.ServerOverloaded, 503)]
    public async Task 可恢复错误最多尝试五次(ModelErrorKind kind, int? status)
    {
        var client = new FailingClient(new ModelClientException("安全摘要", kind, status));
        var events = await Collect(client);
        Assert.Equal(5, client.Attempts);
        Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
    }

    [Theory]
    [InlineData(ModelErrorKind.Authentication, 401)]
    [InlineData(ModelErrorKind.InsufficientQuota, 402)]
    [InlineData(ModelErrorKind.InsufficientQuota, 429)]
    [InlineData(ModelErrorKind.PermissionDenied, 403)]
    [InlineData(ModelErrorKind.InvalidRequest, 400)]
    [InlineData(ModelErrorKind.ModelNotFound, 404)]
    public async Task 确定性错误只尝试一次(ModelErrorKind kind, int status)
    {
        var client = new FailingClient(new ModelClientException("安全摘要", kind, status));
        var events = await Collect(client);
        Assert.Equal(1, client.Attempts);
        Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
    }

    [Fact]
    public async Task 第五次成功不重复用户消息()
    {
        var client = new FailingClient(new ModelClientException("暂时不可用", ModelErrorKind.ServerOverloaded, 503), succeedsAt: 5);
        var events = await Collect(client);
        Assert.Equal(5, client.Attempts);
        Assert.Single(events, item => item.Kind == AgentEventKind.TextDelta);
        Assert.Single(events, item => item.Kind == AgentEventKind.MessageCompleted && item.Message?.Role == ChatRole.User);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
    }

    [Fact]
    public async Task 已输出正文后失败不得自动重放()
    {
        var client = new FailingClient(new ModelClientException("连接中断", ModelErrorKind.ConnectionFailed), partial: true);
        var events = await Collect(client);
        Assert.Equal(1, client.Attempts);
        Assert.Single(events, item => item.Kind == AgentEventKind.TextDelta);
        Assert.Equal(AgentEventKind.TaskFailed, events[^1].Kind);
    }

    [Fact]
    public async Task 服务端等待提示优先且延迟有上限()
    {
        var client = new FailingClient(new ModelClientException("限流", ModelErrorKind.RateLimited, 429)
            { RetryAfter = TimeSpan.FromSeconds(90) });
        var delays = new List<TimeSpan>();
        await Collect(client, (delay, _) => { delays.Add(delay); return Task.CompletedTask; });
        Assert.Equal(4, delays.Count);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(60), delay));
    }

    [Fact]
    public async Task 没有等待提示时指数退避且只等待四次()
    {
        var client = new FailingClient(new ModelClientException("暂时不可用", ModelErrorKind.ServerError, 500));
        var delays = new List<double>();
        await Collect(client, (delay, _) => { delays.Add(delay.TotalSeconds); return Task.CompletedTask; });
        Assert.Equal(new double[] { 1, 2, 4, 8 }, delays);
    }

    [Fact]
    public async Task 取消重试等待立即结束且不生成失败气泡()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FailingClient(new ModelClientException("连接失败", ModelErrorKind.ConnectionFailed));
        var events = await Collect(client, (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        }, cancellation.Token);
        Assert.Equal(1, client.Attempts);
        Assert.Equal(AgentEventKind.TaskCancelled, events[^1].Kind);
        Assert.DoesNotContain(events, item => item.Kind == AgentEventKind.TaskFailed);
    }

    [Fact]
    public async Task 每次超时都重新尝试并最终结束()
    {
        var client = new FailingClient(new OperationCanceledException());
        var events = await Collect(client);
        Assert.Equal(5, client.Attempts);
        Assert.Equal(AgentEventKind.TaskFailed, events[^1].Kind);
    }

    [Fact]
    public async Task 首个流式事件前的底层连接中断仍可重试()
    {
        var client = new FailingClient(new IOException("连接已重置"));
        await Collect(client);
        Assert.Equal(5, client.Attempts);
    }

    [Fact]
    public async Task 未分类的服务器状态码仍可重试()
    {
        var client = new FailingClient(new ModelClientException("安全摘要", ModelErrorKind.Unknown, 502));
        await Collect(client);
        Assert.Equal(5, client.Attempts);
    }

    [Fact]
    public async Task 重试日志不包含输入及上游详情()
    {
        var client = new FailingClient(new ModelClientException("安全摘要", ModelErrorKind.ServerError, 500,
            upstreamMessage: "用户正文 sk-sensitive-test Authorization private-header"));
        var logger = new RecordingLogger();
        var loop = new AgentLoop(client, new ToolRegistry(), logger, retryDelay: (_, _) => Task.CompletedTask);
        await foreach (var _ in loop.RunAsync(new AgentSession(), "机密用户 prompt")) { }
        var log = string.Join("\n", logger.Entries);
        Assert.Contains("Agent 模型准备重试", log);
        Assert.DoesNotContain("机密用户", log);
        Assert.DoesNotContain("用户正文", log);
        Assert.DoesNotContain("sk-sensitive", log);
        Assert.DoesNotContain("Authorization", log);
        Assert.DoesNotContain("private-header", log);
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<AgentLoop>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception) + exception);
    }

    private static async Task<List<AgentEvent>> Collect(IModelClient client,
        Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken cancellationToken = default)
    {
        var loop = new AgentLoop(client, new ToolRegistry(), NullLogger<AgentLoop>.Instance,
            retryDelay: delay ?? ((_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }));
        var events = new List<AgentEvent>();
        await foreach (var item in loop.RunAsync(new AgentSession(), "测试请求", cancellationToken)) events.Add(item);
        return events;
    }

    private sealed class FailingClient(Exception error, int succeedsAt = int.MaxValue, bool partial = false) : IModelClient
    {
        public int Attempts { get; private set; }
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            Attempts++;
            if (partial || Attempts >= succeedsAt) yield return new TextDeltaEvent("测试正文");
            if (Attempts >= succeedsAt) yield return new ModelCompletedEvent("stop");
            else throw error;
        }
    }
}
