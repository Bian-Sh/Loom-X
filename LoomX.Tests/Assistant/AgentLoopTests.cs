using LoomX.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class AgentLoopTests
{
    private static AgentLoop CreateLoop(IModelClient modelClient, ToolRegistry? registry = null) =>
        new(modelClient, registry ?? new ToolRegistry(), NullLogger<AgentLoop>.Instance);

    private static async Task<List<AgentEvent>> CollectAsync(
        IAsyncEnumerable<AgentEvent> events,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<AgentEvent>();
        await foreach (var agentEvent in events.WithCancellation(cancellationToken))
        {
            collected.Add(agentEvent);
        }
        return collected;
    }

    [Fact]
    public async Task SimpleAnswer_EmitsSessionTextAndCompleted()
    {
        var modelClient = new ScriptedModelClient(
            [new TextDeltaEvent("你好，"), new TextDeltaEvent("世界"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "打个招呼"));

        Assert.Equal(AgentEventKind.SessionStarted, events[0].Kind);
        Assert.Equal(2, events.Count(item => item.Kind == AgentEventKind.TextDelta));
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal("你好，世界", session.Messages[^1].Content);
        Assert.Equal(ChatRole.User, session.Messages[0].Role);
        Assert.Single(modelClient.Requests);
    }

    [Fact]
    public async Task ToolRoundTrip_FeedsResultBackToModel()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.list_providers", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("当前有 2 个 Provider。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "看看我现在有几个 Provider"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);

        var started = Assert.Single(events, item => item.Kind == AgentEventKind.ToolCallStarted);
        Assert.Equal("mock.list_providers", started.ToolName);
        var completed = Assert.Single(events, item => item.Kind == AgentEventKind.ToolCallCompleted);
        Assert.True(completed.Success);

        // 消息序列：用户 → 助手(工具调用) → 工具结果 → 助手(最终回答)
        Assert.Equal(4, session.Messages.Count);
        Assert.Equal(ChatRole.User, session.Messages[0].Role);
        Assert.Equal(ChatRole.Assistant, session.Messages[1].Role);
        Assert.Single(session.Messages[1].ToolCalls);
        Assert.Equal(ChatRole.Tool, session.Messages[2].Role);
        Assert.Equal("call_1", session.Messages[2].ToolCallId);
        Assert.Contains("openai", session.Messages[2].Content);
        Assert.Equal("当前有 2 个 Provider。", session.Messages[3].Content);

        // 第二次模型请求必须带上工具结果
        Assert.Equal(2, modelClient.Requests.Count);
        var toolMessage = modelClient.Requests[1].Messages[^1];
        Assert.Equal(ChatRole.Tool, toolMessage.Role);
        Assert.Equal("mock.list_providers", toolMessage.ToolName);
    }

    [Fact]
    public async Task ToolThrows_ErrorIsFedBackToModelAndLoopRecovers()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateThrowingTool());
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.explode", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("工具暂时不可用。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "试一下"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        var completed = Assert.Single(events, item => item.Kind == AgentEventKind.ToolCallCompleted);
        Assert.False(completed.Success);
        var toolMessage = session.Messages[2];
        Assert.Contains("工具执行失败", toolMessage.Content);
        Assert.Contains("模拟工具故障", toolMessage.Content);
    }

    [Fact]
    public async Task UnknownTool_ErrorIsFedBackToModel()
    {
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "loomx.list_providers", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("我还没有这个工具。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "列出 Provider"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Contains("未注册的工具", session.Messages[2].Content);
    }

    [Fact]
    public async Task InvalidToolArguments_ErrorIsFedBackToModel()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.list_providers", "{不是json")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("参数格式不对。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "查询"));

        Assert.Contains("有效的 JSON", session.Messages[2].Content);
        Assert.Equal(AgentSessionState.Completed, session.State);
    }

    [Fact]
    public async Task DuplicateToolCallIds_AreDedupedBeforeExecuting()
    {
        // 复现 LoomX 会话 38b4976249be48eda6ad7e7acb7085a0 事故：
        // 上游把同一批 tool_call 扇出成多份相同 id 的副本，AgentLoop 必须在派发前按 id 去重，
        // 否则会执行 N 次、写 N 条重复 tool 结果进历史、下一次请求被 sensenova 拒绝为 400。
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());
        // 第二次调用直接 stop，确保我们只关注"第一步如何把 6 次调用降成 1 次"。
        var modelClient = new ScriptedModelClient(
            [
                new ModelToolCallEvent(new ToolCall("call_a", "mock.list_providers", "{}")),
                new ModelToolCallEvent(new ToolCall("call_a", "mock.list_providers", "{}")),
                new ModelToolCallEvent(new ToolCall("call_a", "mock.list_providers", "{}")),
                new ModelToolCallEvent(new ToolCall("call_a", "mock.list_providers", "{}")),
                new ModelToolCallEvent(new ToolCall("call_a", "mock.list_providers", "{}")),
                new ModelToolCallEvent(new ToolCall("call_a", "mock.list_providers", "{}")),
                new ModelCompletedEvent("tool_calls"),
            ],
            [new TextDeltaEvent("已读。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "查询"));

        // 仅派发了一次工具（去重生效），写回历史也只有 1 条 tool 结果。
        var toolCompleted = events.Count(e => e.Kind == AgentEventKind.ToolCallCompleted);
        Assert.Equal(1, toolCompleted);

        // 历史里 assistant 的 tool_calls 只剩 1 项；后续跟 1 条 tool 消息。
        var assistantWithCalls = session.Messages.Single(m => m.ToolCalls is { Count: > 0 });
        Assert.Single(assistantWithCalls.ToolCalls!);
        var toolResults = session.Messages.Where(m => m.Role == ChatRole.Tool).ToList();
        Assert.Single(toolResults);
        Assert.Equal(AgentSessionState.Completed, session.State);
    }

    [Fact]
    public async Task MaxStepsExceeded_FailsTask()
    {
        var toolCallsTurn = (IReadOnlyList<ModelStreamEvent>)[
            new ModelToolCallEvent(new ToolCall("call_1", "mock.list_providers", "{}")),
            new ModelCompletedEvent("tool_calls")];
        var modelClient = new ScriptedModelClient(toolCallsTurn, toolCallsTurn, toolCallsTurn);
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());
        var session = new AgentSession(new AgentSessionOptions { MaxSteps = 2 });

        var events = await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "循环查询"));

        Assert.Equal(AgentSessionState.Failed, session.State);
        var failed = Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
        Assert.Contains("最大步骤数", failed.Detail);
        Assert.Equal(2, modelClient.Requests.Count);
    }

    [Fact]
    public async Task CancellationDuringStream_CancelsSession()
    {
        var session = new AgentSession();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var events = await CollectAsync(
            CreateLoop(new HangingModelClient()).RunAsync(session, "等不到回答"),
            cancellation.Token);

        Assert.Equal(AgentSessionState.Cancelled, session.State);
        Assert.DoesNotContain(events, item => item.Kind == AgentEventKind.TaskCompleted);
    }

    [Fact]
    public async Task ModelTimeout_FailsTask()
    {
        var session = new AgentSession(new AgentSessionOptions { ModelTimeout = TimeSpan.FromMilliseconds(100) });

        var events = await CollectAsync(CreateLoop(new HangingModelClient()).RunAsync(session, "超时测试"));

        Assert.Equal(AgentSessionState.Failed, session.State);
        var failed = Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
        Assert.Contains("超时", failed.Detail);
    }

    [Fact]
    public async Task ModelThrows_FailsTask()
    {
        var modelClient = new ScriptedModelClient(); // 没有剧本，第一次调用即抛出
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "触发模型故障"));

        Assert.Equal(AgentSessionState.Failed, session.State);
        Assert.Equal(AgentEventKind.TaskFailed, events[^1].Kind);
    }

    [Fact]
    public async Task ModelClientException_FailedDetailCarriesStructuredErrorInfo()
    {
        var modelClient = new ThrowingModelClient(new ModelClientException(
            "模型服务返回错误状态 429。",
            ModelErrorKind.RateLimited,
            statusCode: 429,
            errorCode: "rate_limit_exceeded",
            upstreamMessage: "Rate limit reached, retry after 20s."));
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "触发限流"));

        Assert.Equal(AgentSessionState.Failed, session.State);
        var failed = Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
        // 默认文化 zh-CN：本地化描述 + 状态码 + Provider 错误码 + 上游描述
        Assert.Contains("速率限制", failed.Detail);
        Assert.Contains("429", failed.Detail);
        Assert.Contains("rate_limit_exceeded", failed.Detail);
        Assert.Contains("retry after 20s", failed.Detail);
    }

    [Fact]
    public async Task ModelClientException_WithoutStructuredInfo_FallsBackToUnknownKind()
    {
        var modelClient = new ThrowingModelClient(new ModelClientException("老格式的失败。"));
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "触发失败"));

        var failed = Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
        Assert.Contains("模型请求失败", failed.Detail);
    }

    /// <summary>总是抛出指定异常的模型客户端。</summary>
    private sealed class ThrowingModelClient(Exception exception) : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
            ModelRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            if (exception is not null) throw exception;
            yield break;
        }
    }

    [Fact]
    public async Task ToolTimeout_ErrorIsFedBackToModel()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.slow",
            Description = "执行很慢的工具",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object","properties":{}}""")!,
            Timeout = TimeSpan.FromMilliseconds(100),
            Handler = async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return ToolResult.Ok("不应到达");
            },
        });
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.slow", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("工具超时了。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "调用慢工具"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Contains("超时", session.Messages[2].Content);
        var completed = events.Single(item => item.Kind == AgentEventKind.ToolCallCompleted);
        Assert.False(completed.Success);
    }

    [Fact]
    public async Task SessionWithSystemPrompt_IncludesSystemMessageFirst()
    {
        var session = new AgentSession(new AgentSessionOptions { SystemPrompt = "你是 LoomX 小助手。" });
        var modelClient = new ScriptedModelClient(
            [new TextDeltaEvent("好的"), new ModelCompletedEvent("stop")]);

        await CollectAsync(CreateLoop(modelClient).RunAsync(session, "你好"));

        Assert.Equal(ChatRole.System, modelClient.Requests[0].Messages[0].Role);
        Assert.Equal("你是 LoomX 小助手。", modelClient.Requests[0].Messages[0].Content);
    }

    [Fact]
    public async Task MultiTurn_SecondRunKeepsHistory()
    {
        var modelClient = new ScriptedModelClient(
            [new TextDeltaEvent("第一次回答"), new ModelCompletedEvent("stop")],
            [new TextDeltaEvent("第二次回答"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();
        var loop = CreateLoop(modelClient);

        await CollectAsync(loop.RunAsync(session, "第一句"));
        Assert.Equal(AgentSessionState.Completed, session.State);
        await CollectAsync(loop.RunAsync(session, "第二句"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(4, session.Messages.Count);
        Assert.Equal(2, modelClient.Requests.Count);
        // 第二轮请求应携带第一轮完整历史（用户1 + 助手1 + 用户2）
        Assert.Equal(3, modelClient.Requests[1].Messages.Count);
    }
}
