using System.Net;
using System.Text;
using LoomX.Configuration;
using LoomX.Contracts;
using LoomX.Services;
using LoomX.Tests.Logging;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class AnthropicProxyClientTests
{
    [Fact]
    public async Task SendAsync_FailureLog_ContainsSafeSummaryWithoutBodies()
    {
        const string prompt = "sensitive-user-prompt";
        const string upstreamBody = "{\"error\":{\"message\":\"sensitive-upstream-response\"}}";
        using var httpClient = new HttpClient(new ResponseHandler(HttpStatusCode.BadRequest, upstreamBody));
        var logger = new RecordingLogger<AnthropicProxyClient>();
        var client = new AnthropicProxyClient(httpClient, logger);
        var model = new ResolvedModelConfig
        {
            ModelId = "claude-sonnet-4-5",
            OllamaModelName = "claude-sonnet-4-5",
            DisplayName = "Claude Sonnet",
            ProviderId = "anthropic",
            ApiModes = ["anthropic"],
            BaseUrl = "https://api.anthropic.com",
            ApiKey = "secret",
            AnthropicModel = "claude-sonnet-4-5"
        };
        var request = new AnthropicMessagesRequest
        {
            Model = model.ModelId,
            Messages =
            [
                new AnthropicMessage
                {
                    Role = "user",
                    Content = [new AnthropicContentBlock { Type = "text", Text = prompt }]
                }
            ]
        };

        await client.SendAsync(model, request, CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Contains(model.ProviderId, message);
        Assert.Contains(model.ModelId, message);
        Assert.Contains("400", message);
        Assert.DoesNotContain(prompt, message);
        Assert.DoesNotContain("sensitive-upstream-response", message);
    }

    [Fact]
    public async Task SendAsync_UsesSharedProviderExecutionPipeline()
    {
        var pipeline = new CapturingPipeline("{\"content\":[]}", "application/json");
        using var httpClient = new HttpClient(new ThrowingHandler());
        var client = new AnthropicProxyClient(httpClient, new RecordingLogger<AnthropicProxyClient>(), pipeline);
        var model = CreateModel();

        var (statusCode, response, error) = await client.SendAsync(
            model,
            CreateRequest(model, stream: false),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.NotNull(response);
        Assert.Null(error);
        Assert.NotNull(pipeline.Context);
        Assert.Equal(model.ProviderId, pipeline.Context!.ProviderId);
        Assert.Equal(model.ModelId, pipeline.Context.ModelId);
        Assert.Equal("anthropic", pipeline.Context.ApiMode);
        Assert.Equal("/v1/messages", pipeline.Context.UpstreamPath);
    }

    [Fact]
    public async Task SendStreamAsync_UsesSharedProviderExecutionPipelineAndOwnsResultLifetime()
    {
        var pipeline = new CapturingPipeline("event: message_stop\n\n", "text/event-stream");
        using var httpClient = new HttpClient(new ThrowingHandler());
        var client = new AnthropicProxyClient(httpClient, new RecordingLogger<AnthropicProxyClient>(), pipeline);
        var model = CreateModel();

        var (statusCode, stream, error) = await client.SendStreamAsync(
            model,
            CreateRequest(model, stream: true),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.NotNull(stream);
        Assert.Null(error);
        Assert.NotNull(pipeline.Context);
        Assert.Equal("anthropic", pipeline.Context!.ApiMode);
        Assert.False(pipeline.ResponseDisposed);

        using (var reader = new StreamReader(stream!, Encoding.UTF8, leaveOpen: true))
        {
            Assert.Equal("event: message_stop\n\n", await reader.ReadToEndAsync());
        }
        await stream!.DisposeAsync();

        Assert.True(pipeline.ResponseDisposed);
    }

    [Fact]
    public async Task BaseUrl已包含V1时真实路由保留重复版本段并记录警告()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, "{\"content\":[]}");
        using var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger<AnthropicProxyClient>();
        var client = new AnthropicProxyClient(httpClient, logger);
        var model = new ResolvedModelConfig
        {
            ModelId = "claude-sonnet-4-5",
            OllamaModelName = "claude-sonnet-4-5",
            DisplayName = "Claude Sonnet",
            ProviderId = "anthropic",
            ApiModes = ["anthropic"],
            BaseUrl = "https://api.anthropic.com/v1",
            ApiKey = "secret",
            AnthropicModel = "claude-sonnet-4-5"
        };

        await client.SendAsync(model, new AnthropicMessagesRequest
        {
            Model = model.ModelId,
            Messages =
            [
                new AnthropicMessage
                {
                    Role = "user",
                    Content = [new AnthropicContentBlock { Type = "text", Text = "hello" }]
                }
            ]
        }, CancellationToken.None);

        Assert.Equal("https://api.anthropic.com/v1/v1/messages", handler.RequestUri);
        Assert.Contains(logger.Messages, message => message.Contains("重复版本段", StringComparison.Ordinal)
            && message.Contains("/v1/v1/messages", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret", StringComparison.Ordinal));
    }

    private static ResolvedModelConfig CreateModel() => new()
    {
        ModelId = "claude-sonnet-4-5",
        OllamaModelName = "claude-sonnet-4-5",
        DisplayName = "Claude Sonnet",
        ProviderId = "anthropic",
        ApiModes = ["anthropic"],
        BaseUrl = "https://api.anthropic.com",
        ApiKey = "secret",
        AnthropicModel = "claude-sonnet-4-5"
    };

    private static AnthropicMessagesRequest CreateRequest(ResolvedModelConfig model, bool stream) => new()
    {
        Model = model.ModelId,
        Stream = stream,
        Messages =
        [
            new AnthropicMessage
            {
                Role = "user",
                Content = [new AnthropicContentBlock { Type = "text", Text = "hello" }]
            }
        ]
    };

    private sealed class CapturingPipeline(string responseBody, string contentType) : IProviderExecutionPipeline
    {
        public ProviderExecutionContext? Context { get; private set; }
        public bool ResponseDisposed { get; private set; }

        public Task<ProviderExecutionResult> ExecuteAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            Context = context;
            return Task.FromResult(new ProviderExecutionResult(
                HttpStatusCode.OK,
                contentType,
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                Encoding.UTF8.GetBytes(responseBody),
                false));
        }

        public Task<ProviderStreamingResult> ExecuteStreamingAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            Context = context;
            var response = new TrackingResponseMessage(() => ResponseDisposed = true)
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseBody, Encoding.UTF8, contentType),
            };
            return Task.FromResult(new ProviderStreamingResult(response, response.Content.ReadAsStream(cancellationToken)));
        }
    }

    private sealed class TrackingResponseMessage(Action onDispose) : HttpResponseMessage
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) onDispose();
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("测试不应直接发送 HTTP 请求。");
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
