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
