using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using LoomX.Assistant;
using LoomX.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

    private sealed class GzipResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var source = new MemoryStream();
            using (var gzip = new GZipStream(source, CompressionLevel.Fastest, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
            {
                writer.Write(body);
            }

            var content = new ByteArrayContent(source.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class SequenceHttpHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private readonly Queue<string> responses = new(responseBodies);

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            if (!responses.TryDequeue(out var body))
            {
                throw new InvalidOperationException("测试没有为本次模型请求准备响应。");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    [Fact]
    public async Task StreamAsync_ResponsesEndpoint_ConvertsRequestAndParsesToolCall()
    {
        const string responsesSse = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_123","created_at":123}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","item":{"id":"msg_1","type":"message","role":"assistant"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","delta":"先查一下"}

        event: response.output_item.added
        data: {"type":"response.output_item.added","item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"mock.list_providers"}}

        event: response.function_call_arguments.delta
        data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","delta":"{}"}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_123"}}

        data: [DONE]

        """;
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responsesSse);
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "https://api.example.com/v1", "test-model",
            endpointFormat: "responses");
        var request = new ModelRequest([ChatMessage.User("查询")], [CreateTool()]);

        var events = await CollectAsync(client.StreamAsync(request, CancellationToken.None));

        Assert.Equal("https://api.example.com/v1/responses", handler.LastRequest?.RequestUri?.ToString());
        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Null(body["messages"]);
        Assert.Equal("查询", body["input"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("function", body["tools"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("mock.list_providers", body["tools"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("先查一下", Assert.IsType<TextDeltaEvent>(events[0]).Text);
        Assert.Equal("mock.list_providers", Assert.IsType<ModelToolCallEvent>(events[1]).ToolCall.Name);
        Assert.Equal("{}", Assert.IsType<ModelToolCallEvent>(events[1]).ToolCall.ArgumentsJson);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[2]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_GzipResponsesJson_EmitsToolCallAndCompletion()
    {
        const string responsesJson = """
        {
          "id": "resp_gzip",
          "output": [
            {
              "type": "function_call",
              "call_id": "call_gzip",
              "name": "loomx.list_providers",
              "arguments": "{}"
            }
          ]
        }
        """;
        var handler = new GzipResponseHandler(responsesJson);
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler),
            "https://provider.example/v1",
            "test-model",
            endpointFormat: "responses");

        var events = await CollectAsync(client.StreamAsync(
            new ModelRequest([ChatMessage.User("读取 Provider")], [CreateTool()]),
            CancellationToken.None));

        var toolCall = Assert.IsType<ModelToolCallEvent>(events[0]).ToolCall;
        Assert.Equal("call_gzip", toolCall.Id);
        Assert.Equal("loomx.list_providers", toolCall.Name);
        Assert.Equal("{}", toolCall.ArgumentsJson);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[1]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_ResponsesEndpoint_EmptyBodyThrowsVisibleModelError()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, string.Empty);
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "https://api.example.com/v1", "test-model",
            endpointFormat: "responses");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Equal(ModelErrorKind.Unknown, exception.Kind);
        Assert.Contains("模型服务返回空响应", exception.Message);
        Assert.Contains("模型响应无效", exception.UpstreamMessage);
    }

    [Fact]
    public async Task StreamAsync_ResponsesEndpoint_Status200QuotaErrorIsClassified()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.OK,
            """{"error":{"code":"insufficient_quota","message":"余额不足，请充值"}}""");
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "https://api.example.com/v1", "test-model",
            endpointFormat: "responses");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Equal(ModelErrorKind.InsufficientQuota, exception.Kind);
        Assert.Equal("insufficient_quota", exception.ErrorCode);
        Assert.Contains("余额不足", exception.UpstreamMessage);
    }

    [Fact]
    public async Task StreamAsync_ChatCompletionsEndpoint_Status200QuotaErrorIsClassified()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.OK,
            "{\"error\":{\"code\":\"INSUFFICIENT_BALANCE\",\"message\":\"余额不足，请充值\"}}\n");
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler), "https://api.example.com/v1", "test-model");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(new ModelRequest([ChatMessage.User("hi")], []), CancellationToken.None)));

        Assert.Equal(ModelErrorKind.InsufficientQuota, exception.Kind);
        Assert.Equal("INSUFFICIENT_BALANCE", exception.ErrorCode);
        Assert.Contains("余额不足", exception.UpstreamMessage);
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
    public async Task StreamAsync_EmptyFinishReason_DoesNotFlushSenseNovaToolArgumentsEarly()
    {
        // SenseNova 会在参数增量帧中返回 finish_reason:""，只有最后一帧才返回 tool_calls。
        // 空字符串不是完成信号；若提前 drain，三个调用都会只剩首段 "{"。
        var sse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_openai","function":{"name":"mock.test_endpoint","arguments":"{"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"key\": "}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"open"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"ai\""}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"}"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":1,"id":"call_azure","function":{"name":"mock.test_endpoint","arguments":"{"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"arguments":"\"key\": "}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"arguments":"\"azure\""}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"arguments":"}"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":2,"id":"call_ollama","function":{"name":"mock.test_endpoint","arguments":"{"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":2,"function":{"arguments":"\"key\": "}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":2,"function":{"arguments":"\"ollama"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":2,"function":{"arguments":"\"}"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "data: [DONE]");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var events = await CollectAsync(client.StreamAsync(
            new ModelRequest([ChatMessage.User("检查三个 Endpoint")], [CreateTool()]),
            CancellationToken.None));

        var toolCalls = events.OfType<ModelToolCallEvent>().Select(item => item.ToolCall).ToArray();
        Assert.Equal(3, toolCalls.Length);
        Assert.Equal("""{"key": "openai"}""", toolCalls[0].ArgumentsJson);
        Assert.Equal("""{"key": "azure"}""", toolCalls[1].ArgumentsJson);
        Assert.Equal("""{"key": "ollama"}""", toolCalls[2].ArgumentsJson);
        Assert.All(toolCalls, toolCall => Assert.NotNull(JsonNode.Parse(toolCall.ArgumentsJson)));
        var completed = Assert.Single(events.OfType<ModelCompletedEvent>());
        Assert.Equal("tool_calls", completed.FinishReason);
    }

    [Fact]
    public async Task StreamAsync_IncompleteToolArguments_FailsBeforeToolExecution()
    {
        var sse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_incomplete","function":{"name":"mock.test_endpoint","arguments":"{"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "data: [DONE]");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var exception = await Assert.ThrowsAsync<ModelClientException>(async () =>
            await CollectAsync(client.StreamAsync(
                new ModelRequest([ChatMessage.User("检查")], [CreateTool()]),
                CancellationToken.None)));

        Assert.Equal(ModelErrorKind.Unknown, exception.Kind);
        Assert.Contains("工具调用参数不完整", exception.UpstreamMessage);
    }

    [Fact]
    public async Task FunctionCallLifecycle_SenseNovaStream_ExecutesToolAndCompletesSecondModelTurn()
    {
        var toolCallSse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_lifecycle","function":{"name":"mock.test_endpoint","arguments":"{"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"key\": "}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"openai\""}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"}"}}]},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "data: [DONE]");
        var finalAnswerSse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"content":"生命周期验收通过"},"finish_reason":""}]}""",
            """data: {"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "data: [DONE]");
        var handler = new SequenceHttpHandler(toolCallSse, finalAnswerSse);
        var client = new OpenAiCompatibleModelClient(
            new HttpClient(handler),
            "https://provider.example/v1",
            "test-model");
        var registry = new ToolRegistry();
        var executionCount = 0;
        string? executedKey = null;
        registry.Register(new ToolDefinition
        {
            Name = "mock.test_endpoint",
            Description = "读取 Endpoint 的无副作用验收工具。",
            ParametersSchema = JsonNode.Parse(
                """{"type":"object","properties":{"key":{"type":"string"}},"required":["key"]}""")!,
            Handler = (arguments, _) =>
            {
                executionCount++;
                executedKey = arguments?["key"]?.GetValue<string>();
                return Task.FromResult(ToolResult.Ok("Endpoint 状态正常。"));
            },
        });
        var session = new AgentSession(new AgentSessionOptions
        {
            MaxSteps = 3,
            SystemPrompt = "必须先调用工具，收到结果后给出最终回答。",
        });
        var loop = new AgentLoop(client, registry, NullLogger<AgentLoop>.Instance);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in loop.RunAsync(session, "检查 openai Endpoint"))
        {
            events.Add(agentEvent);
        }

        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
        Assert.Equal(1, executionCount);
        Assert.Equal("openai", executedKey);
        Assert.Equal(2, handler.RequestBodies.Count);

        var secondRequest = JsonNode.Parse(handler.RequestBodies[1])!.AsObject();
        var messages = secondRequest["messages"]!.AsArray();
        var assistantToolMessage = messages.Single(message =>
            message?["role"]?.GetValue<string>() == "assistant"
            && message?["tool_calls"] is JsonArray);
        var persistedCall = assistantToolMessage!["tool_calls"]![0]!;
        Assert.Equal("call_lifecycle", persistedCall["id"]!.GetValue<string>());
        Assert.Equal(
            """{"key": "openai"}""",
            persistedCall["function"]!["arguments"]!.GetValue<string>());
        var toolResultMessage = messages.Single(message => message?["role"]?.GetValue<string>() == "tool");
        Assert.Equal("call_lifecycle", toolResultMessage!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("生命周期验收通过", session.Messages[^1].Content);
    }

    [Fact]
    public async Task StreamAsync_FlushesToolCallWhenDoneMarkerOmitsFinishReason()
    {
        var sse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_done","function":{"name":"mock.list_providers","arguments":"{}"}}]},"finish_reason":null}]}""",
            "data: [DONE]");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var events = await CollectAsync(client.StreamAsync(
            new ModelRequest([ChatMessage.User("查询")], [CreateTool()]),
            CancellationToken.None));

        var toolCall = Assert.IsType<ModelToolCallEvent>(Assert.Single(events.Take(1))).ToolCall;
        Assert.Equal("call_done", toolCall.Id);
        Assert.Equal("mock.list_providers", toolCall.Name);
        Assert.Equal("{}", toolCall.ArgumentsJson);
        Assert.Equal("tool_calls", Assert.IsType<ModelCompletedEvent>(events[1]).FinishReason);
    }

    [Fact]
    public async Task StreamAsync_FlushesToolCallWhenConnectionClosesWithoutDoneMarker()
    {
        const string sse = """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_eof","function":{"name":"mock.list_providers","arguments":"{}"}}]},"finish_reason":null}]}""";
        var handler = new FakeHttpHandler(HttpStatusCode.OK, sse);
        var client = new OpenAiCompatibleModelClient(new HttpClient(handler), "http://localhost/v1", "test-model");

        var events = await CollectAsync(client.StreamAsync(
            new ModelRequest([ChatMessage.User("查询")], [CreateTool()]),
            CancellationToken.None));

        Assert.Equal("call_eof", Assert.IsType<ModelToolCallEvent>(events[0]).ToolCall.Id);
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
        // 重复扇出是同一完整参数快照，不能被拼成 "{}{}{}..." 这种无效 JSON。
        Assert.All(toolCalls, t => Assert.Equal("{}", t.ToolCall.ArgumentsJson));
        Assert.All(toolCalls, t => Assert.NotNull(JsonNode.Parse(t.ToolCall.ArgumentsJson)));
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

    [Fact]
    [Trait("Category", "Live")]
    public async Task SenseNovaLive_FunctionCallLifecycle_CompletesWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("LOOMX_LIVE_SENSENOVA_FUNCTIONCALL"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var providerBusinessId = Environment.GetEnvironmentVariable("LOOMX_LIVE_PROVIDER") ?? "sensenova";
        var modelId = Environment.GetEnvironmentVariable("LOOMX_LIVE_MODEL") ?? "glm-5.2";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = AppDataPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var db = new ConfigurationDbContext(options);
        var provider = await db.Providers
            .AsNoTracking()
            .Include(item => item.Models)
            .SingleOrDefaultAsync(item => item.BusinessId == providerBusinessId);
        Assert.NotNull(provider);
        Assert.True(provider.Enabled, $"Provider '{providerBusinessId}' 未启用。");
        var model = provider.Models.SingleOrDefault(item => item.ModelId == modelId);
        Assert.NotNull(model);
        Assert.True(model.Enabled, $"模型 '{providerBusinessId}/{modelId}' 未启用。");
        Assert.False(string.IsNullOrWhiteSpace(provider.ProtectedApiKey), "Provider 未配置 API Key。");
        Assert.True(
            ProtectedApiKeyStore.TryUnprotect(provider.ProtectedApiKey!, out var apiKey),
            "Provider API Key 无法由当前 Windows 用户解密。");

        using var httpClient = new HttpClient(new HttpClientHandler());
        var executionPipeline = new MinimumIntervalExecutionPipeline(TimeSpan.FromSeconds(5));
        var client = new OpenAiCompatibleModelClient(
            httpClient,
            (model.BaseUrl ?? provider.BaseUrl).TrimEnd('/'),
            model.ModelId,
            apiKey,
            NullLogger<OpenAiCompatibleModelClient>.Instance,
            MergeHeaders(provider.HeadersJson, model.HeadersJson),
            endpointFormat: provider.EndpointFormat,
            executionPipeline: executionPipeline,
            providerId: provider.BusinessId);
        var registry = new ToolRegistry();
        var executionCount = 0;
        string? executedKey = null;
        registry.Register(new ToolDefinition
        {
            Name = "acceptance.inspect_endpoint",
            Description = "Function Call 生命周期验收专用的无副作用工具。",
            ParametersSchema = JsonNode.Parse(
                """{"type":"object","properties":{"key":{"type":"string","enum":["openai"]}},"required":["key"],"additionalProperties":false}""")!,
            Handler = (arguments, _) =>
            {
                executionCount++;
                executedKey = arguments?["key"]?.GetValue<string>();
                return Task.FromResult(ToolResult.Ok("Endpoint openai 状态正常；请回复 LIFECYCLE_OK。"));
            },
        });
        var session = new AgentSession(new AgentSessionOptions
        {
            MaxSteps = 2,
            ModelTimeout = TimeSpan.FromSeconds(60),
            SystemPrompt = "你正在执行验收。必须先且只调用一次 acceptance.inspect_endpoint，参数 key 固定为 openai；收到工具结果后只回复 LIFECYCLE_OK，不得再次调用工具。",
        });
        var loop = new AgentLoop(client, registry, NullLogger<AgentLoop>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in loop.RunAsync(
                           session,
                           "开始 Function Call 全生命周期验收。",
                           timeout.Token))
        {
            events.Add(agentEvent);
        }

        Assert.Equal(2, executionPipeline.RequestCount);
        Assert.Equal(1, executionCount);
        Assert.Equal("openai", executedKey);
        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(AgentEventKind.TaskCompleted, events[^1].Kind);
        Assert.Contains("LIFECYCLE_OK", session.Messages[^1].Content, StringComparison.Ordinal);
        var assistantToolMessage = Assert.Single(session.Messages, item => item.ToolCalls.Count > 0);
        var toolCall = Assert.Single(assistantToolMessage.ToolCalls);
        Assert.Equal("acceptance.inspect_endpoint", toolCall.Name);
        Assert.Equal("openai", JsonNode.Parse(toolCall.ArgumentsJson)?["key"]?.GetValue<string>());
        Assert.Single(session.Messages, item => item.Role == ChatRole.Tool);
    }

    private static IReadOnlyDictionary<string, string> MergeHeaders(string providerHeadersJson, string modelHeadersJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ParseHeaders(providerHeadersJson)) result[pair.Key] = pair.Value;
        foreach (var pair in ParseHeaders(modelHeadersJson)) result[pair.Key] = pair.Value;
        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(string headersJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private sealed class MinimumIntervalExecutionPipeline(TimeSpan minimumInterval) : IProviderExecutionPipeline
    {
        private readonly ProviderExecutionPipeline inner = new();
        private readonly SemaphoreSlim gate = new(1, 1);
        private DateTimeOffset lastCompletedAt = DateTimeOffset.MinValue;

        public int RequestCount { get; private set; }

        public async Task<ProviderExecutionResult> ExecuteAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var remaining = minimumInterval - (DateTimeOffset.UtcNow - lastCompletedAt);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }

                RequestCount++;
                var result = await inner.ExecuteAsync(httpClient, request, context, cancellationToken);
                lastCompletedAt = DateTimeOffset.UtcNow;
                return result;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
