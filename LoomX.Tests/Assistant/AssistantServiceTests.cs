using Xunit;
using LoomX.Assistant;
using LoomX.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

/// <summary>
/// AssistantService 门面：事件流驱动、模型未配置错误、会话持久化与恢复。
/// </summary>
public sealed class AssistantServiceTests : IDisposable
{
    private readonly string rootDirectory = Path.Combine(Path.GetTempPath(), $"loomx-svc-sessions-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SendAsync_StreamsEvents_AndPersistsSession()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("你好，"), new TextDeltaEvent("我是小助手。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model));
        var events = new List<AgentEvent>();

        await foreach (var agentEvent in service.SendAsync("你好"))
        {
            events.Add(agentEvent);
        }

        Assert.Contains(events, item => item.Kind == AgentEventKind.SessionStarted);
        Assert.Equal(2, events.Count(item => item.Kind == AgentEventKind.TextDelta));
        Assert.Contains(events, item => item.Kind == AgentEventKind.TaskCompleted);
        Assert.Equal(AgentSessionState.Completed, service.CurrentSession.State);

        // 运行结束后会话已持久化
        var summaries = service.ListSessions();
        var summary = Assert.Single(summaries);
        Assert.Equal("你好", summary.Title);
    }

    [Fact]
    public async Task SendAsync_ModelNotConfigured_Throws()
    {
        var service = CreateService(new StubModelClientFactory(null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var unused in service.SendAsync("你好")) { }
        });

        Assert.Contains("模型未配置", exception.Message);
    }

    [Fact]
    public async Task SendAsync_EmptyMessage_Throws()
    {
        var service = CreateService(new StubModelClientFactory(new ScriptedModelClient(
            [new ModelCompletedEvent("stop")])));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var unused in service.SendAsync("   ")) { }
        });
    }

    [Fact]
    public async Task LoadSession_RestoresHistory_AsCurrentSession()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("已查到。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model));
        await foreach (var unused in service.SendAsync("查询 Provider")) { }

        var sessionId = service.CurrentSession.Id;
        service.NewSession();
        Assert.NotEqual(sessionId, service.CurrentSession.Id);

        Assert.True(await service.LoadSessionAsync(sessionId));
        Assert.Equal(sessionId, service.CurrentSession.Id);
        Assert.Contains(service.CurrentSession.Messages, message => message.Content == "查询 Provider");
    }

    [Fact]
    public async Task NewSession_ClearsCurrentHistory()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("回答"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model));
        await foreach (var unused in service.SendAsync("问题")) { }

        service.NewSession();

        Assert.DoesNotContain(service.CurrentSession.Messages, message => message.Content == "问题");
    }

    private AssistantService CreateService(StubModelClientFactory factory) => new(
        factory,
        new ToolRegistry(),
        new AssistantSessionStore(rootDirectory),
        NullLoggerFactory.Instance);

    /// <summary>剧本式模型工厂：TryCreateAsync 返回预置客户端（或 null 模拟未配置）。</summary>
    private sealed class StubModelClientFactory(IModelClient? client)
        : AssistantModelClientFactory(
            new ThrowingHttpClientFactory(),
            null!,
            NullLogger<OpenAiCompatibleModelClient>.Instance,
            NullLogger<AssistantModelClientFactory>.Instance)
    {
        public override Task<IModelClient?> TryCreateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(client);

        public override Task<AssistantModelInfo?> DescribeAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AssistantModelInfo?>(client is null ? null : new AssistantModelInfo("stub", "stub-model", "https://stub.example.com/v1"));
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
