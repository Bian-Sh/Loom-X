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

    [Fact]
    public async Task AskEachTime_RejectedWriteTool_NotExecuted()
    {
        var executed = false;
        var registry = CreateWriteToolRegistry(() => executed = true);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.write_thing", """{"value":1}""")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("好的。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, AssistantPermissionMode.AskEachTime);
        service.ApprovalHandler = _ => Task.FromResult(false);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in service.SendAsync("改一下")) events.Add(agentEvent);

        Assert.False(executed);
        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolApprovalRequested && item.ToolName == "mock.write_thing");
        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolCallCompleted && item.Success == false);
        Assert.Contains(service.CurrentSession.Messages, message => message.Role == ChatRole.Tool && message.Content!.Contains("拒绝"));
    }

    [Fact]
    public async Task AskEachTime_ApprovedWriteTool_Executed()
    {
        var executed = false;
        var registry = CreateWriteToolRegistry(() => executed = true);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.write_thing", """{"value":1}""")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("改好了。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, AssistantPermissionMode.AskEachTime);

        ToolApprovalRequest? captured = null;
        service.ApprovalHandler = request =>
        {
            captured = request;
            return Task.FromResult(true);
        };

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in service.SendAsync("改一下")) events.Add(agentEvent);

        Assert.True(executed);
        Assert.NotNull(captured);
        Assert.Equal("mock.write_thing", captured!.ToolName);
        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolCallCompleted && item.Success == true);
    }

    [Fact]
    public async Task AutoApprove_WriteToolRunsWithoutApproval()
    {
        var executed = false;
        var registry = CreateWriteToolRegistry(() => executed = true);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.write_thing", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("改好了。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, AssistantPermissionMode.AutoApprove);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in service.SendAsync("改一下")) events.Add(agentEvent);

        Assert.True(executed);
        Assert.DoesNotContain(events, item => item.Kind == AgentEventKind.ToolApprovalRequested);
    }

    private static ToolRegistry CreateWriteToolRegistry(Action onExecuted)
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.write_thing",
            Description = "模拟写操作",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object","properties":{}}""")!,
            RiskLevel = ToolRiskLevel.Write,
            Handler = (_, _) =>
            {
                onExecuted();
                return Task.FromResult(ToolResult.Ok("""{"ok":true}"""));
            },
        });
        return registry;
    }

    private AssistantService CreateService(
        StubModelClientFactory factory,
        ToolRegistry? registry = null,
        AssistantPermissionMode? permissionMode = null)
    {
        AssistantPreferencesStore? preferencesStore = null;
        if (permissionMode is not null)
        {
            preferencesStore = new AssistantPreferencesStore(Path.Combine(rootDirectory, $"prefs-{Guid.NewGuid():N}.json"));
            preferencesStore.Save(new AssistantPreferences { PermissionMode = permissionMode.Value });
        }

        return new AssistantService(
            factory,
            registry ?? new ToolRegistry(),
            new AssistantSessionStore(Path.Combine(rootDirectory, $"sessions-{Guid.NewGuid():N}")),
            NullLoggerFactory.Instance,
            preferencesStore);
    }

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
