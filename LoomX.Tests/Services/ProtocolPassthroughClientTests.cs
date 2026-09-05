using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaHub.Configuration;
using OllamaHub.Contracts;
using OllamaHub.Tests.Logging;
using OllamaHub.Services;
using Xunit;

namespace OllamaHub.Tests.Services;

public sealed class ProtocolPassthroughClientTests
{
    [Fact]
    public async Task ProxyAsync_FailureLog_ContainsSafeSummaryWithoutBodies()
    {
        const string prompt = "sensitive-user-prompt";
        const string upstreamBody = "sensitive-upstream-response";
        var handler = new CapturingHandler(HttpStatusCode.BadRequest, upstreamBody);
        using var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger<ProtocolPassthroughClient>();
        var client = new ProtocolPassthroughClient(httpClient, logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();
        var model = CreateModel();
        var payload = new OpenAIChatCompletionsRequest
        {
            Model = model.ModelId,
            Messages = [new OpenAIChatMessage { Role = "user", Content = JsonValue.Create(prompt) }]
        };

        await client.ProxyAsync(httpContext, model, "openai", "/chat/completions", payload, CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Contains(model.ProviderId, message);
        Assert.Contains(model.ModelId, message);
        Assert.Contains("400", message);
        Assert.DoesNotContain(prompt, message);
        Assert.DoesNotContain(upstreamBody, message);
    }

    [Fact]
    public async Task ProxyAsync_OpenAiRequest_SendsConfiguredModelId()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();

        var model = new ResolvedModelConfig
        {
            ModelId = "deepseek/deepseek-v4-pro",
            OllamaModelName = "360智脑/deepseek-v4-pro",
            DisplayName = "360智脑/deepseek-v4-pro",
            ProviderId = "360智脑",
            ApiModes = ["openai"],
            BaseUrl = "https://api.360.cn",
            ApiKey = "secret",
            AnthropicModel = "deepseek/deepseek-v4-pro"
        };

        var payload = new OpenAIChatCompletionsRequest
        {
            Model = model.ModelId,
            Messages =
            [
                new OpenAIChatMessage
                {
                    Role = "user",
                    Content = JsonValue.Create("hello")
                }
            ],
            Stream = false
        };

        await client.ProxyAsync(httpContext, model, "openai", "/chat/completions", payload, CancellationToken.None);

        Assert.NotNull(handler.RequestBody);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("deepseek/deepseek-v4-pro", json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ProxyAsync_UsesV1OnlyWhenProviderBaseUrlContainsIt()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();
        var model = CreateModel("https://token.sensenova.cn/v1");

        await client.ProxyAsync(httpContext, model, "openai", "/chat/completions", new { model = model.ModelId }, CancellationToken.None);

        Assert.Equal("https://token.sensenova.cn/v1/chat/completions", handler.RequestUri);
    }

    [Fact]
    public async Task ProxyAsync_DoesNotAddV1ToProviderBaseUrl()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();
        var model = CreateModel("https://token.sensenova.cn");

        await client.ProxyAsync(httpContext, model, "openai", "/chat/completions", new { model = model.ModelId }, CancellationToken.None);

        Assert.Equal("https://token.sensenova.cn/chat/completions", handler.RequestUri);
    }

    [Fact]
    public async Task ProxyGatewayAttemptAsync_NormalizesEmptyOpenAiFinishReasonInSse()
    {
        const string responseBody = "data: {\"id\":\"completion-1\",\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"thinking\"},\"finish_reason\":\"\"}]}\n\ndata: [DONE]\n";
        var handler = new CapturingHandler(responseBody: responseBody, mediaType: "text/event-stream");
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();
        var model = CreateModel("https://token.sensenova.cn/v1");

        Assert.True(await client.ProxyGatewayAttemptAsync(httpContext, model, "openai", "/chat/completions", new { model = model.ModelId }, CancellationToken.None));

        httpContext.Response.Body.Position = 0;
        var forwarded = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        Assert.Contains("\"finish_reason\":null", forwarded);
        Assert.DoesNotContain("\"finish_reason\":\"\"", forwarded);
    }

    [Fact]
    public async Task ProxyOpenAiResponsesGatewayAttemptAsync_ConvertsResponsesSseWithoutLoggingBodies()
    {
        const string prompt = "sensitive-user-prompt";
        const string responseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1","created_at":123}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","item":{"id":"msg_1","type":"message","role":"assistant"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","delta":"healthy"}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1"}}

        """;
        var handler = new CapturingHandler(responseBody: responseBody, mediaType: "text/event-stream");
        using var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger<ProtocolPassthroughClient>();
        var client = new ProtocolPassthroughClient(httpClient, logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();
        var model = CreateModel("https://token.sensenova.cn/v1");
        var payload = JsonNode.Parse($$"""
        {
          "model": "{{model.ModelId}}",
          "messages": [{"role": "user", "content": "{{prompt}}"}],
          "stream": true
        }
        """)!.AsObject();

        Assert.True(await client.ProxyOpenAiResponsesGatewayAttemptAsync(httpContext, model, payload, CancellationToken.None));

        Assert.Equal("https://token.sensenova.cn/v1/responses", handler.RequestUri);
        using var upstreamRequest = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(upstreamRequest.RootElement.TryGetProperty("input", out _));
        Assert.False(upstreamRequest.RootElement.TryGetProperty("messages", out _));
        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
        httpContext.Response.Body.Position = 0;
        var downstreamBody = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        Assert.Contains("\"content\":\"healthy\"", downstreamBody);
        Assert.EndsWith("data: [DONE]\n\n", downstreamBody);
        Assert.All(logger.Messages, message => Assert.DoesNotContain(prompt, message));
    }

    [Fact]
    public async Task ProxyAsync_OpenAiJsonNode_PreservesRawJsonFields()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();

        var model = new ResolvedModelConfig
        {
            ModelId = "deepseek/deepseek-v4-pro",
            OllamaModelName = "360智脑/deepseek-v4-pro",
            DisplayName = "360智脑/deepseek-v4-pro",
            ProviderId = "360智脑",
            ApiModes = ["openai"],
            BaseUrl = "https://api.360.cn",
            ApiKey = "secret",
            AnthropicModel = "deepseek/deepseek-v4-pro"
        };

        var payload = JsonNode.Parse("""
        {
          "model": "alias-model",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "hello" }
              ]
            }
          ],
          "tool_choice": { "type": "function", "function": { "name": "read_file" } },
          "custom_field": { "enabled": true }
        }
        """)!.AsObject();

        payload["model"] = model.ModelId;

        await client.ProxyAsync(httpContext, model, "openai", "/chat/completions", payload, CancellationToken.None);

        Assert.NotNull(handler.RequestBody);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("deepseek/deepseek-v4-pro", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("function", json.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.True(json.RootElement.GetProperty("custom_field").GetProperty("enabled").GetBoolean());
        Assert.Equal("text", json.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task ProxyAsync_OpenAiJsonNode_MergesModelExtraFieldsAtTopLevel()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();

        var payload = JsonNode.Parse("""
        {
          "model": "alias-model",
          "messages": [
            {"role": "user", "content": "Hello!"}
          ]
        }
        """)!.AsObject();

        payload["model"] = "deepseek/deepseek-v4-pro";

        var modelExtraValue = JsonNode.Parse("""
        {
          "type": "enabled"
        }
        """);

        var model = new ResolvedModelConfig
        {
            ModelId = "deepseek/deepseek-v4-pro",
            OllamaModelName = "360智脑/deepseek-v4-pro",
            DisplayName = "360智脑/deepseek-v4-pro",
            ProviderId = "360智脑",
            ApiModes = ["openai"],
            BaseUrl = "https://api.360.cn",
            ApiKey = "secret",
            AnthropicModel = "deepseek/deepseek-v4-pro",
            Extra = new Dictionary<string, JsonNode?>
            {
                ["thinking"] = modelExtraValue,
                ["reasoning_effort"] = JsonValue.Create("high")
            }
        };

        // Merge model.Extra fields at top level (same behavior as HandleChatCompletionsAsync)
        foreach (var kvp in model.Extra)
        {
            payload[kvp.Key] = kvp.Value?.DeepClone();
        }

        await client.ProxyAsync(httpContext, model, "openai", "/chat/completions", payload, CancellationToken.None);

        Assert.NotNull(handler.RequestBody);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(json.RootElement.GetProperty("thinking").GetProperty("type").GetString() == "enabled");
        Assert.Equal("high", json.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("deepseek/deepseek-v4-pro", json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ProxyAsync_OllamaRequest_SendsConfiguredModelId()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ProtocolPassthroughClient(httpClient, NullLogger<ProtocolPassthroughClient>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        httpContext.Response.Body = new MemoryStream();

        var model = new ResolvedModelConfig
        {
            ModelId = "deepseek/deepseek-v4-pro",
            OllamaModelName = "360智脑/deepseek-v4-pro",
            DisplayName = "360智脑/deepseek-v4-pro",
            ProviderId = "360智脑",
            ApiModes = ["ollama"],
            BaseUrl = "https://api.360.cn",
            ApiKey = "secret",
            AnthropicModel = "deepseek/deepseek-v4-pro"
        };

        var payload = new OllamaChatRequest
        {
            Model = model.ModelId,
            Messages =
            [
                new OllamaChatMessage
                {
                    Role = "user",
                    Content = "hello"
                }
            ],
            Stream = false
        };

        await client.ProxyAsync(httpContext, model, "ollama", "/api/chat", payload, CancellationToken.None);

        Assert.NotNull(handler.RequestBody);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("deepseek/deepseek-v4-pro", json.RootElement.GetProperty("model").GetString());
    }

    private static ResolvedModelConfig CreateModel(string baseUrl = "https://api.360.cn") => new()
    {
        ModelId = "deepseek/deepseek-v4-pro",
        OllamaModelName = "360智脑/deepseek-v4-pro",
        DisplayName = "360智脑/deepseek-v4-pro",
        ProviderId = "360智脑",
        ApiModes = ["openai"],
        BaseUrl = baseUrl,
        ApiKey = "secret",
        AnthropicModel = "deepseek/deepseek-v4-pro"
    };

    private sealed class CapturingHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "{}", string mediaType = "application/json") : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, mediaType)
            };
        }
    }
}
