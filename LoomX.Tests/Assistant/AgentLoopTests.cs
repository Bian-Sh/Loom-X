using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.UserDecisions;
using LoomX.Services;
using LoomX.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class AgentLoopTests
{
    private static AgentLoop CreateLoop(IModelClient modelClient, ToolRegistry? registry = null) =>
        new(modelClient, registry ?? new ToolRegistry(), NullLogger<AgentLoop>.Instance,
            failureFormatter: ModelErrorFormatter.FormatException,
            maxStepsFormatter: ModelErrorFormatter.FormatMaxStepsExceeded);

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
        Assert.DoesNotContain("模拟工具故障", toolMessage.Content, StringComparison.Ordinal);
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
    public async Task RunAsync_发送前修复历史中未闭合的工具调用()
    {
        var call = new ToolCall("ask_stale", "assistant.ask_user", "{}") { ArgumentsAreSafe = true };
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("旧问题"));
        session.RestoreMessage(ChatMessage.AssistantToolCalls([call]));
        session.RestoreMessage(ChatMessage.User("取消后的追问"));
        var modelClient = new ScriptedModelClient(
            [new TextDeltaEvent("已恢复。"), new ModelCompletedEvent("stop")]);

        await CollectAsync(CreateLoop(modelClient).RunAsync(session, "继续"));

        var request = Assert.Single(modelClient.Requests);
        var assistantIndex = request.Messages.ToList().FindIndex(message => message.ToolCalls.Count > 0);
        Assert.True(assistantIndex >= 0);
        var toolResult = request.Messages[assistantIndex + 1];
        Assert.Equal(ChatRole.Tool, toolResult.Role);
        Assert.Equal("ask_stale", toolResult.ToolCallId);
        Assert.Contains("\"cancelled\":true", toolResult.Content, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("{\"cancelled\":false,\"values\":{\"choice\":\"default\"},\"custom_inputs\":{}}") ]
    [InlineData("{\"cancelled\":false,\"values\":{\"choice\":null},\"custom_inputs\":{}}") ]
    [InlineData("{\"cancelled\":true,\"values\":{},\"custom_inputs\":{}}") ]
    public async Task AskUser成功完成后_不同Id重复调用不再执行Handler(string firstResult)
    {
        var handlerCalls = 0;
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "assistant.ask_user",
            Description = "测试 AskUser 一次性交互。",
            ParametersSchema = new JsonObject(),
            Handler = (_, _) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(ToolResult.Ok(firstResult));
            },
        });
        var modelClient = new ScriptedModelClient(
            [
                new ModelToolCallEvent(new ToolCall("ask_first", "assistant.ask_user", "{}")),
                new ModelCompletedEvent("tool_calls"),
            ],
            [
                new ModelToolCallEvent(new ToolCall("ask_repeated", "assistant.ask_user", "{}")),
                new ModelCompletedEvent("tool_calls"),
            ],
            [
                new TextDeltaEvent("已根据用户结果继续处理。"),
                new ModelCompletedEvent("stop"),
            ]);
        var session = new AgentSession();

        var events = await CollectAsync(
            CreateLoop(modelClient, registry).RunAsync(session, "请显示 AskUser 测试面板"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));
        Assert.Equal(3, modelClient.Requests.Count);
        Assert.All(modelClient.Requests.Skip(1), request =>
            Assert.DoesNotContain(
                request.Tools,
                tool => string.Equals(tool.Name, "assistant.ask_user", StringComparison.OrdinalIgnoreCase)));
        var toolResults = session.Messages.Where(item => item.Role == ChatRole.Tool).ToArray();
        Assert.Equal(2, toolResults.Length);
        Assert.Equal(firstResult, toolResults[0].Content);
        var suppressed = JsonNode.Parse(toolResults[1].Content!)!.AsObject();
        Assert.True(suppressed["interaction_completed"]!.GetValue<bool>());
        Assert.True(suppressed["repeat_suppressed"]!.GetValue<bool>());
        Assert.Equal("ask_user_already_completed", suppressed["reason"]!.GetValue<string>());
        Assert.Contains("本轮用户交互已经完成", toolResults[1].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", toolResults[1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("已根据用户结果继续处理。", session.Messages[^1].Content);
    }

    [Fact]
    public async Task AskUser完成后模型连续无视终态_第二次重复时本地熔断完成()
    {
        var handlerCalls = 0;
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "assistant.ask_user",
            Description = "测试 AskUser 重复调用熔断。",
            ParametersSchema = new JsonObject(),
            Handler = (_, _) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(ToolResult.Ok(
                    "{\"cancelled\":false,\"values\":{\"choice\":\"default\"},\"custom_inputs\":{}}"));
            },
        });
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("ask_first", "assistant.ask_user", "{}")), new ModelCompletedEvent("tool_calls")],
            [new ModelToolCallEvent(new ToolCall("ask_repeat_1", "assistant.ask_user", "{}")), new ModelCompletedEvent("tool_calls")],
            [new ModelToolCallEvent(new ToolCall("ask_repeat_2", "assistant.ask_user", "{}")), new ModelCompletedEvent("tool_calls")]);
        var session = new AgentSession(new AgentSessionOptions { MaxSteps = 8 });

        var events = await CollectAsync(
            CreateLoop(modelClient, registry).RunAsync(session, "请显示 AskUser 测试面板"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));
        Assert.Equal(3, modelClient.Requests.Count);
        Assert.Equal(3, session.Messages.Count(item => item.Role == ChatRole.Tool));
        Assert.Equal(ChatRole.Assistant, session.Messages[^1].Role);
        Assert.Contains("本轮不会重复询问", session.Messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskUser执行失败后_允许模型修正参数重试()
    {
        var handlerCalls = 0;
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "assistant.ask_user",
            Description = "测试 AskUser 失败重试。",
            ParametersSchema = new JsonObject(),
            Handler = (_, _) =>
            {
                var call = Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(call == 1
                    ? ToolResult.SafeFail("请求参数无效。")
                    : ToolResult.Ok("{\"cancelled\":false,\"values\":{\"choice\":\"default\"},\"custom_inputs\":{}}"));
            },
        });
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("ask_invalid", "assistant.ask_user", "{}")), new ModelCompletedEvent("tool_calls")],
            [new ModelToolCallEvent(new ToolCall("ask_fixed", "assistant.ask_user", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已完成。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "请提问"));

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(2, Volatile.Read(ref handlerCalls));
        Assert.Equal("已完成。", session.Messages[^1].Content);
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
    public async Task ReasoningAndStageText_RetainOrderedBlocksAcrossToolStep()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());
        var client = new ScriptedModelClient(
            [new ReasoningDeltaEvent("原文"), new ReasoningDeltaEvent("摘要", true),
                new TextDeltaEvent("先检查"), new ModelToolCallEvent(new ToolCall("c1", "mock.list_providers", "{}")),
                new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("最终回复"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(client, registry).RunAsync(session, "查询"));

        Assert.Equal(2, events.Count(item => item.Kind == AgentEventKind.StepStarted));
        Assert.Equal("原文", session.Messages[1].Blocks[0].Text);
        Assert.False(session.Messages[1].Blocks[0].IsSummary);
        Assert.True(session.Messages[1].Blocks[1].IsSummary);
        Assert.Equal("先检查", session.Messages[1].Blocks[2].Text);
        Assert.Equal(ChatContentKind.ToolCall, session.Messages[1].Blocks[3].Kind);
        Assert.Equal("最终回复", session.Messages[^1].Content);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
    }

    [Fact]
    public async Task ModelWithoutCompletionEvent_FailsInsteadOfCompletingSilently()
    {
        var modelClient = new ScriptedModelClient([new TextDeltaEvent("不完整响应")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "触发空响应"));

        Assert.Equal(AgentSessionState.Failed, session.State);
        var failed = Assert.Single(events, item => item.Kind == AgentEventKind.TaskFailed);
        Assert.Contains("模型响应无效", failed.Detail);
        Assert.DoesNotContain(events, item => item.Kind == AgentEventKind.TaskCompleted);
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

    [Fact]
    public async Task UnknownTool_原始参数不进入Session事件或下一轮ModelRequest()
    {
        const string fullPath = @"C:\Users\Alice\private\config.toml";
        const string secret = "unknown-tool-private-value";
        var raw = $$"""{"path":"{{fullPath.Replace("\\", "\\\\")}}","value":"{{secret}}"}""";
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("unknown-1", "unknown.tool", raw)), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已处理。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient).RunAsync(session, "执行未知工具"));

        var stored = Assert.Single(session.Messages, message => message.ToolCalls.Count > 0).ToolCalls[0].ArgumentsJson;
        var completedMessage = Assert.Single(events, item => item.Kind == AgentEventKind.MessageCompleted && item.Message?.ToolCalls.Count > 0)
            .Message!.ToolCalls[0].ArgumentsJson;
        var nextRequest = modelClient.Requests[1].Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls[0].ArgumentsJson;
        foreach (var safe in new[] { stored, completedMessage, nextRequest })
        {
            Assert.DoesNotContain(fullPath, safe, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, safe, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SafeArgumentsProjector异常时安全失败但Handler仍收到原始参数()
    {
        const string secret = "projection-failure-secret";
        string? handled = null;
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.projector_failure",
            Description = "投影失败测试",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object"}""")!,
            SafeArgumentsProjector = _ => throw new ApplicationException("projection failed"),
            Handler = (arguments, _) =>
            {
                handled = arguments?["value"]?.GetValue<string>();
                return Task.FromResult(ToolResult.Ok("{}"));
            },
        });
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call-projector", "mock.projector_failure", $$"""{"value":"{{secret}}"}""")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("完成"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        await CollectAsync(CreateLoop(model, registry).RunAsync(session, "执行"));

        Assert.Equal(secret, handled);
        Assert.DoesNotContain(secret, session.Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls[0].ArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler返回普通失败_中央边界覆盖Session事件UiJsonl和下一轮请求()
    {
        const string secret = "private-header-value";
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.unsafe_failure",
            Description = "返回不可信失败文本",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object"}""")!,
            Handler = (_, _) => Task.FromResult(ToolResult.Fail(secret)),
        });
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("unsafe-failure-1", "mock.unsafe_failure", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已处理。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        var events = await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "执行失败工具"));

        using var viewModel = new AssistantViewModel(new GatewayProcessService());
        foreach (var agentEvent in events)
        {
            viewModel.Project(agentEvent);
        }

        var root = Path.Combine(Path.GetTempPath(), $"loomx-tool-result-safety-{Guid.NewGuid():N}");
        try
        {
            var store = new AssistantSessionStore(root);
            await store.SaveAsync(session);
            var jsonl = await File.ReadAllTextAsync(Path.Combine(root, session.Id + ".jsonl"));
            var exposed = session.Messages.Select(message => message.Content)
                .Concat(events.Select(agentEvent => agentEvent.Detail))
                .Concat(events.Select(agentEvent => agentEvent.Message?.Content))
                .Concat(modelClient.Requests.SelectMany(request => request.Messages).Select(message => message.Content))
                .Append(string.Join("\n", viewModel.Messages.Select(message => message.ItemsText)))
                .Append(jsonl);

            AssertNoPrivateData(exposed, secret);
            Assert.Contains("工具执行失败", session.Messages.Single(message => message.Role == ChatRole.Tool).Content);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Handler抛出含敏感内容异常_日志与所有安全出口只保留异常类型()
    {
        const string secret = "private-header-value";
        const string fullPath = @"C:\Users\Alice\private\config.toml";
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.private_exception",
            Description = "抛出包含敏感内容的异常",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object"}""")!,
            Handler = (_, _) =>
            {
                var exception = new PrivateHandlerException($"{secret} at {fullPath}", new InvalidOperationException(secret));
                exception.Data["private"] = fullPath;
                throw exception;
            },
        });
        var logger = new DetailedRecordingLogger<AgentLoop>();
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("private-exception-1", "mock.private_exception", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已处理。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();
        var loop = new AgentLoop(modelClient, registry, logger,
            failureFormatter: ModelErrorFormatter.FormatException,
            maxStepsFormatter: ModelErrorFormatter.FormatMaxStepsExceeded);

        var events = await CollectAsync(loop.RunAsync(session, "执行异常工具"));

        AssertNoPrivateData(
            session.Messages.Select(message => message.Content)
                .Concat(events.Select(agentEvent => agentEvent.Detail))
                .Concat(events.Select(agentEvent => agentEvent.Message?.Content))
                .Concat(modelClient.Requests.SelectMany(request => request.Messages).Select(message => message.Content)),
            secret,
            fullPath);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.NotNull(error.Exception);
        Assert.IsNotType<PrivateHandlerException>(error.Exception);
        AssertNoPrivateData([error.Message, error.StateText, error.Exception!.ToString()], secret, fullPath);
        Assert.Contains(nameof(PrivateHandlerException), error.StateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未注册工具名含敏感内容_所有安全出口使用固定名称且保持CallId配对()
    {
        const string secret = "private-header-value";
        const string rawToolName = "unknown.private-header-value";
        const string safeToolName = "unknown.tool";
        var logger = new DetailedRecordingLogger<AgentLoop>();
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("unknown-secret-1", rawToolName, "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已处理。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();
        var loop = new AgentLoop(modelClient, new ToolRegistry(), logger,
            failureFormatter: ModelErrorFormatter.FormatException,
            maxStepsFormatter: ModelErrorFormatter.FormatMaxStepsExceeded);

        var events = await CollectAsync(loop.RunAsync(session, "执行未知工具"));

        var storedCall = Assert.Single(session.Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls);
        var toolMessage = session.Messages.Single(message => message.Role == ChatRole.Tool);
        var started = Assert.Single(events, item => item.Kind == AgentEventKind.ToolCallStarted);
        var completed = Assert.Single(events, item => item.Kind == AgentEventKind.ToolCallCompleted);
        var nextRequestCall = Assert.Single(modelClient.Requests[1].Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls);
        Assert.All(new[] { storedCall.Name, toolMessage.ToolName, started.ToolName, completed.ToolName, nextRequestCall.Name },
            name => Assert.Equal(safeToolName, name));
        Assert.All(new[] { storedCall.Id, toolMessage.ToolCallId, started.ToolCallId, completed.ToolCallId, nextRequestCall.Id },
            id => Assert.Equal("unknown-secret-1", id));
        using var viewModel = new AssistantViewModel(new GatewayProcessService());
        foreach (var agentEvent in events)
        {
            viewModel.Project(agentEvent);
        }

        var root = Path.Combine(Path.GetTempPath(), $"loomx-unknown-tool-safety-{Guid.NewGuid():N}");
        try
        {
            var store = new AssistantSessionStore(root);
            await store.SaveAsync(session);
            var jsonl = await File.ReadAllTextAsync(Path.Combine(root, session.Id + ".jsonl"));
            AssertNoPrivateData(
                session.Messages.Select(message => message.Content)
                    .Concat(events.Select(agentEvent => agentEvent.Detail))
                    .Concat(events.Select(agentEvent => agentEvent.Message?.Content))
                    .Concat(modelClient.Requests.SelectMany(request => request.Messages).Select(message => message.Content))
                    .Append(string.Join("\n", viewModel.Messages.Select(message => message.ItemsText)))
                    .Append(string.Join("\n", logger.Entries.Select(entry => $"{entry.Message} | {entry.StateText} | {entry.Exception}")))
                    .Append(jsonl),
                secret,
                rawToolName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 固定结构化安全失败_经过中央边界仍保留Toml与AskUser错误码()
    {
        var tomlRegistry = TomlToolsTestSupport.CreateRegistry(new RecordingTomlDocumentService
        {
            ReadHandler = (_, _) => throw new InvalidOperationException("内部异常不得回显"),
        });
        Assert.True(tomlRegistry.TryGet("toml.read", out var tomlTool));
        using var broker = new UserDecisionBroker(NullLogger<UserDecisionBroker>.Instance);
        var registry = new ToolRegistry();
        registry.Register(tomlTool!);
        AssistantTools.RegisterAll(registry, broker);
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("toml-failure", "toml.read", "{\"path\":\"config.toml\"}")), new ModelCompletedEvent("tool_calls")],
            [new ModelToolCallEvent(new ToolCall("ask-failure", "assistant.ask_user", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已处理。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();

        await CollectAsync(CreateLoop(modelClient, registry).RunAsync(session, "执行结构化失败工具"));

        var results = session.Messages.Where(message => message.Role == ChatRole.Tool).Select(message => message.Content ?? string.Empty).ToArray();
        Assert.Contains(results, content => content.Contains("toml_operation_failed", StringComparison.Ordinal));
        Assert.Contains(results, content => content.Contains("invalid_request", StringComparison.Ordinal));
    }

    private static void AssertNoPrivateData(IEnumerable<string?> values, params string[] privateValues)
    {
        foreach (var value in values.Where(value => value is not null))
        {
            foreach (var privateValue in privateValues)
            {
                Assert.DoesNotContain(privateValue, value!, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class PrivateHandlerException(string message, Exception innerException) : Exception(message, innerException);

    private sealed class DetailedRecordingLogger<T> : ILogger<T>
    {
        public List<RecordedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var stateText = state is IEnumerable<KeyValuePair<string, object?>> properties
                ? string.Join(" | ", properties.Select(property => $"{property.Key}={property.Value}"))
                : state?.ToString() ?? string.Empty;
            Entries.Add(new RecordedLogEntry(logLevel, exception, formatter(state, exception), stateText));
        }
    }

    private sealed record RecordedLogEntry(LogLevel Level, Exception? Exception, string Message, string StateText);

}
