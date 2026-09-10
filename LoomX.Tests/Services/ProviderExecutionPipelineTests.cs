using System.Net;
using System.Text;
using LoomX.Assistant;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class ProviderExecutionPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_CapturesStatusHeadersAndBody()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.TooManyRequests,
            "{\"error\":\"busy\"}",
            "application/json"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline();

        var result = await pipeline.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal("application/json", result.ContentType);
        Assert.Equal("trace-1", Assert.Single(result.Headers["X-Trace"]));
        Assert.Equal("{\"error\":\"busy\"}", result.BodyText);
        Assert.True(result.IsRetryable);
        Assert.False(result.IsSuccess);
        Assert.False(result.WasNormalized);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesEmptyOpenAiFinishReason()
    {
        const string body = "{\"choices\":[{\"index\":0,\"finish_reason\":\"\"}]}";
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, body, "application/json"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline();

        var result = await pipeline.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions", NormalizeOpenAiFinishReasons: true),
            CancellationToken.None);

        Assert.True(result.WasNormalized);
        Assert.Contains("\"finish_reason\":null", result.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"finish_reason\":\"\"", result.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotNormalizeWhenContextDisablesIt()
    {
        const string body = "data: {\"choices\":[{\"finish_reason\":\"\"}]}\n\ndata: [DONE]\n";
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, body, "text/event-stream"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline();

        var result = await pipeline.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);

        Assert.False(result.WasNormalized);
        Assert.Contains("\"finish_reason\":\"\"", result.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssistantClient_UsesInjectedPipelineAndProviderContext()
    {
        var pipeline = new CapturingPipeline();
        using var httpClient = new HttpClient(new ThrowingHandler());
        var client = new OpenAiCompatibleModelClient(
            httpClient,
            "https://provider.example/v1",
            "model-1",
            executionPipeline: pipeline,
            providerId: "provider-1");

        await foreach (var _ in client.StreamAsync(
            new ModelRequest([ChatMessage.User("hello")], []),
            CancellationToken.None))
        {
        }

        Assert.NotNull(pipeline.Context);
        Assert.Equal("provider-1", pipeline.Context!.ProviderId);
        Assert.Equal("model-1", pipeline.Context.ModelId);
        Assert.Equal("/chat/completions", pipeline.Context.UpstreamPath);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string body, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            };
            response.Headers.TryAddWithoutValidation("X-Trace", "trace-1");
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingPipeline : IProviderExecutionPipeline
    {
        public ProviderExecutionContext? Context { get; private set; }

        public Task<ProviderExecutionResult> ExecuteAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            Context = context;
            return Task.FromResult(new ProviderExecutionResult(
                HttpStatusCode.OK,
                "text/event-stream",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                Encoding.UTF8.GetBytes("data: [DONE]\n"),
                false));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("测试不应直接发送 HTTP 请求。");
    }
}
