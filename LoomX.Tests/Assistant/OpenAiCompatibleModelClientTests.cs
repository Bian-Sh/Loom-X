using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class OpenAiCompatibleModelClientTests
{
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string body;

        public FakeHttpHandler(HttpStatusCode statusCode, string body)
        {
            this.statusCode = statusCode;
            this.body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private static ToolDefinition CreateTool() => new()
    {
        Name = "mock.list_providers",
        Description = "列出 Provider",
        ParametersSchema = JsonNode.Parse("""{"type":"object","properties":{}}""")!,
        Handler = (_, _) => Task.FromResult(ToolResult.Ok("{}")),
    };

    private static async Task<List<ModelStreamEvent>> CollectAsync(IAsyncEnumerable<ModelStreamEvent> events)
    {
        var collected = new List<ModelStreamEvent>();
        await foreach (var streamEvent in events)
        {
            collected.Add(streamEvent);
        }
        return collected;
    }

    [Fact]
    public async Task StreamAsync_ParsesTextDeltasAndCompletion()
    {
        var sse = string.Join('\n',
            """data: {"choices":[{"delta":{"content":"当前"},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{"content":"有 2 个"},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "",
            "data: [DONE]",
            "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var request = new ModelRequest([ChatMessage.User("查询")], []);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        Assert.Equal(3, events.Count);
        Assert.Equal("当前", Assert.IsType<TextDeltaEvent>(events[0]).Text);
        Assert.Equal("有 2 个", Assert.IsType<TextDeltaEvent>(events[1]).Text);
        Assert.Equal("stop", Assert.IsType<ModelCompletedEvent>(events[2]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_AssemblesFragmentedToolCall()
    {
        var sse = string.Join('\n',
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"mock.list_providers","arguments":""}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"prov"}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"ider\":true}"}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "",
            "data: [DONE]",
            "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var request = new ModelRequest([ChatMessage.User("查询")], [CreateTool()]);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        Assert.Equal(2, events.Count);
        var toolCall = Assert.IsType<ModelToolCallEvent>(events[0]).ToolCall;
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("mock.list_providers", toolCall.Name);
        Assert.Equal("""{"provider":true}""", toolCall.ArgumentsJson);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[1]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_SkipsToolCallsWithEmptyName()
    {
        // 上游（如 sensenova）可能在 tool_calls 里附带空 id/name 的占位调用，
        // 客户端应过滤掉，避免透传后被判为「未注册工具」并污染历史导致 400。
        var sse = string.Join('\n',
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"mock.list_providers","arguments":"{}"}},{"index":1,"function":{"arguments":"{}"}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "",
            "data: [DONE]",
            "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var request = new ModelRequest([ChatMessage.User("查询")], [CreateTool()]);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        var toolCalls = events.OfType<ModelToolCallEvent>().ToList();
        Assert.Single(toolCalls);
        Assert.Equal("mock.list_providers", toolCalls[0].ToolCall.Name);
        Assert.Equal("call_1", toolCalls[0].ToolCall.Id);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[^1]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_PlaceholderWithoutIdAndName_IsDropped()
    {
        // 复现 sensenova 真实畸形：占位片段既无 id/name 也无 index，
        // 旧逻辑会经 ?? 0 归到 index 0 并新建空 builder，累积出空 name 调用。
        // 新逻辑应在解析阶段直接丢弃，不污染正常的 index 0 builder。
        var sse = string.Join('\n',
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"mock.list_providers","arguments":"{}"}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{"tool_calls":[{"function":{"arguments":"{}"}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "",
            "data: [DONE]",
            "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var request = new ModelRequest([ChatMessage.User("查询")], [CreateTool()]);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        var toolCalls = events.OfType<ModelToolCallEvent>().ToList();
        Assert.Single(toolCalls);
        Assert.Equal("call_1", toolCalls[0].ToolCall.Id);
        Assert.Equal("mock.list_providers", toolCalls[0].ToolCall.Name);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[^1]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_DedupesFannedOutToolCallsById()
    {
        // 复现 LoomX 会话 38b4976249be48eda6ad7e7acb7085a0 的事故：
        // sensenova 在一次响应里把 6 个 tool_call 扇出成多份相同 id 的副本，
        // 每个副本的 index 递增；旧逻辑按 index 累积会产生 63 个调用（6 个 id 重复 N 次），
        // 写回历史后违反 OpenAI 协议 "同一 assistant 消息内 tool_call_id 唯一" 约束
        // → 下一次请求 400 InvalidRequest → Session Failed。
        // 新逻辑必须按 id 去重，最终只发射 6 条 ModelToolCallEvent。
        var parts = new List<string>
        {
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","function":{"name":"mock.list_providers","arguments":"{}"}},{"index":1,"id":"call_b","function":{"name":"mock.list_providers","arguments":"{}"}},{"index":2,"id":"call_c","function":{"name":"mock.list_providers","arguments":"{}"}},{"index":3,"id":"call_d","function":{"name":"mock.list_providers","arguments":"{}"}},{"index":4,"id":"call_e","function":{"name":"mock.list_providers","arguments":"{}"}},{"index":5,"id":"call_f","function":{"name":"mock.list_providers","arguments":"{}"}}]},"finish_reason":null}]}""",
        };
        // 重复扇出 10 次（实际事故里是 ~10 次导致 60+ 条），模拟 sensenova 行为
        for (var round = 1; round <= 10; round++)
        {
            var deltas = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                var id = $"call_{(char)('a' + i)}";
                var idx = round * 6 + i;
                // 想要的输出：{"index":N,"id":"call_a","function":{"arguments":"{}"}}
                // 用 $$$"""..."""：单 { 单 } 都视为字面量；{{{ = 一个 { + 一个插值开始。
                deltas.Add($$$"""{"index":{{{idx}}},"id":"{{{id}}}","function":{"arguments":"{}"}}""");
            }
            parts.Add($$$"""data: {"choices":[{"delta":{"tool_calls":[{{{string.Join(",", deltas)}}}]},"finish_reason":null}]}""");
        }
        parts.Add("""data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");
        parts.Add("data: [DONE]");
        var sse = string.Join("\n\n", parts);

        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var request = new ModelRequest([ChatMessage.User("查询")], [CreateTool()]);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        var toolCalls = events.OfType<ModelToolCallEvent>().ToList();
        Assert.Equal(6, toolCalls.Count);
        var ids = toolCalls.Select(t => t.ToolCall.Id).ToList();
        Assert.Equal(6, ids.Distinct().Count());
        Assert.Equal(new[] { "call_a", "call_b", "call_c", "call_d", "call_e", "call_f" }, ids);
        Assert.All(toolCalls, t => Assert.Equal("mock.list_providers", t.ToolCall.Name));
        // arguments 形如 "{}{}{}..."（首轮 1 段 + 10 轮扇出共 11 段），任一 tool_call 都不能为空，
        // 且其字符数 = 2 * 11 = 22。
        Assert.All(toolCalls, t => Assert.Equal(22, t.ToolCall.ArgumentsJson.Length));
        Assert.All(toolCalls, t => Assert.True(t.ToolCall.ArgumentsJson.All(c => c == '{' || c == '}')));
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[^1]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_RepeatedFinishReasonDoesNotReplayToolCalls()
    {
        // 上游若在同一流里出现多个 finish_reason chunk（如重试/异常后再次发送完成帧），
        // 旧逻辑会把同一组 tool_call 重复发射；新逻辑应在每个 finish_reason 后清空累积器。
        var sse = string.Join('\n',
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"mock.list_providers","arguments":"{}"}}]},"finish_reason":null}]}""",
            "",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "",
            "data: [DONE]",
            "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var request = new ModelRequest([ChatMessage.User("查询")], [CreateTool()]);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        // 只允许 1 个 tool_call + 2 个 completion
        Assert.Equal(3, events.Count);
        Assert.IsType<ModelToolCallEvent>(events[0]);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[1]).FinishReason);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[2]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_SendsAuthHeaderModelAndToolSchema()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1/", "test-model", apiKey: "sk-test");
        var request = new ModelRequest(
            [ChatMessage.System("系统提示"), ChatMessage.User("查询"), ChatMessage.ToolResult(new ToolCall("call_9", "mock.list_providers", "{}"), """{"ok":true}""")],
            [CreateTool()]);

        await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test", handler.LastRequest?.Headers.Authorization?.Parameter);
        Assert.Equal("http://localhost/v1/chat/completions", handler.LastRequest?.RequestUri?.ToString());

        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("test-model", body["model"]?.GetValue<string>());
        Assert.True(body["stream"]?.GetValue<bool>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]?.GetValue<string>());
        Assert.Equal("tool", messages[2]!["role"]?.GetValue<string>());
        Assert.Equal("call_9", messages[2]!["tool_call_id"]?.GetValue<string>());
        var tools = body["tools"]!.AsArray();
        Assert.Equal("mock.list_providers", tools[0]!["function"]!["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task StreamAsync_AssistantToolCallsSerializedInHistory()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");
        var history = new List<ChatMessage>
        {
            ChatMessage.User("查询"),
            ChatMessage.AssistantToolCalls([new ToolCall("call_1", "mock.list_providers", "{}")]),
            ChatMessage.ToolResult(new ToolCall("call_1", "mock.list_providers", "{}"), "结果"),
        };

        await CollectAsync(client.StreamAsync(new ModelRequest(history, [CreateTool()]), CancellationToken.None));

        var messages = JsonNode.Parse(handler.LastRequestBody!)!["messages"]!.AsArray();
        var toolCalls = messages[1]!["tool_calls"]!.AsArray();
        Assert.Equal("call_1", toolCalls[0]!["id"]?.GetValue<string>());
        Assert.Equal("function", toolCalls[0]!["type"]?.GetValue<string>());
        Assert.Equal("mock.list_providers", toolCalls[0]!["function"]!["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task StreamAsync_NonSuccessStatus_ThrowsModelClientException()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task StreamAsync_ErrorStatus_ParsesOpenAiErrorBodyIntoStructuredException()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit reached for gpt-4o. Please retry after 20s.","type":"tokens","code":"rate_limit_exceeded"}}""");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "gpt-4o");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Equal(ModelErrorKind.RateLimited, exception.Kind);
        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("rate_limit_exceeded", exception.ErrorCode);
        Assert.Contains("Rate limit reached", exception.UpstreamMessage);
    }

    [Fact]
    public async Task StreamAsync_ErrorBodyWithSecret_IsRedactedBeforeThrowing()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided: sk-liveAbc123456789Secret","code":"invalid_api_key"}}""");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Equal(ModelErrorKind.Authentication, exception.Kind);
        Assert.NotNull(exception.UpstreamMessage);
        Assert.DoesNotContain("liveAbc123456789Secret", exception.UpstreamMessage);
        Assert.DoesNotContain("liveAbc123456789Secret", exception.Message);
    }

    [Fact]
    public async Task StreamAsync_ErrorStatusWithoutBody_StillClassifiesByStatusCode()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.ServiceUnavailable, "");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Equal(ModelErrorKind.ServerOverloaded, exception.Kind);
        Assert.Equal(503, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
        Assert.Null(exception.UpstreamMessage);
    }

    [Fact]
    public async Task StreamAsync_ExtraHeaders_AreSentWithRequest()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "http://localhost/v1", "test-model",
            extraHeaders: new Dictionary<string, string>
            {
                ["User-Agent"] = "custom-relay-client/1.0",
                ["X-Client"] = "loomx-assistant",
            });

        await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None));

        Assert.Equal("custom-relay-client/1.0", string.Join(",", handler.LastRequest!.Headers.GetValues("User-Agent")));
        Assert.Equal("loomx-assistant", string.Join(",", handler.LastRequest.Headers.GetValues("X-Client")));
    }

    [Fact]
    public async Task StreamAsync_ExtraHeaders_CannotOverrideAuthorization()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "http://localhost/v1", "test-model", apiKey: "sk-real",
            extraHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer attacker" });

        await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None));

        Assert.Equal("Bearer sk-real", handler.LastRequest?.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task StreamAsync_ReasoningEffort_WrittenToPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "http://localhost/v1", "test-model", reasoningEffort: "high");

        await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None));

        Assert.Equal("high", JsonNode.Parse(handler.LastRequestBody!)!["reasoning_effort"]?.GetValue<string>());
    }

    [Fact]
    public async Task StreamAsync_NoReasoningEffort_FieldOmitted()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None));

        Assert.Null(JsonNode.Parse(handler.LastRequestBody!)!.AsObject()["reasoning_effort"]);
    }
}
