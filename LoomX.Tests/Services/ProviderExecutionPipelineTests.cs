using System.Net;
using System.IO.Compression;
using System.Text;
using LoomX.Assistant;
using LoomX.Plugins;
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
    public async Task ExecuteAsync_DecompressesGzipResponseBeforeParsing()
    {
        const string body = "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
        var compressed = CompressGzip(body);
        using var httpClient = new HttpClient(new StaticBinaryResponseHandler(
            HttpStatusCode.OK,
            compressed,
            "application/json",
            "gzip"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/responses");
        var pipeline = new ProviderExecutionPipeline();

        var result = await pipeline.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/responses"),
            CancellationToken.None);

        Assert.Equal(body, result.BodyText);
        Assert.DoesNotContain("Content-Encoding", result.Headers.Keys, StringComparer.OrdinalIgnoreCase);
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
    public async Task ExecuteAsync_AppliesResponsePipelineAndRemovesContentLength()
    {
        const string token = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
        const string secret = "secret\n\"quoted\"";
        var responsePipeline = new ReplacingResponsePipeline(token, secret);
        var responseBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = token } } },
        });
        using var httpClient = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.OK,
            responseBody,
            "application/json"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline(responsePipeline: responsePipeline);

        var result = await pipeline.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);

        Assert.True(result.WasNormalized);
        Assert.DoesNotContain(token, result.BodyText, StringComparison.Ordinal);
        Assert.Contains("secret", result.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Content-Length", result.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_RestoresPlaceholderSplitAcrossSseContentEvents()
    {
        const string token = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
        const string secret = "restored-secret";
        var first = token[..23];
        var second = token[23..];
        var body =
            "data: " + System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[] { new { index = 0, delta = new { content = first } } },
            }) + "\n\n" +
            "data: " + System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[] { new { index = 0, delta = new { content = second } } },
            }) + "\n\n" +
            "data: [DONE]\n\n";
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, body, "text/event-stream"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline(responsePipeline: new ReplacingResponsePipeline(token, secret));

        await using var result = await pipeline.ExecuteStreamingAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);
        using var reader = new StreamReader(result.Body, Encoding.UTF8);
        var restored = await reader.ReadToEndAsync();

        Assert.Contains(secret, restored, StringComparison.Ordinal);
        Assert.DoesNotContain(token, restored, StringComparison.Ordinal);
        Assert.EndsWith("data: [DONE]\n\n", restored, StringComparison.Ordinal);
        var events = restored.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, events.Length);
        foreach (var item in events.Take(2))
            System.Text.Json.JsonDocument.Parse(item["data: ".Length..]).Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_RestoresPlaceholderSplitAcrossBufferedSseEvents()
    {
        const string token = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
        const string secret = "buffered-secret";
        var body =
            "data: " + System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[] { new { index = 0, delta = new { content = token[..20] } } },
            }) + "\n\n" +
            "data: " + System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[] { new { index = 0, delta = new { content = token[20..] } } },
            }) + "\n\n" +
            "data: [DONE]\n\n";
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, body, "text/event-stream"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline(responsePipeline: new ReplacingResponsePipeline(token, secret));

        var result = await pipeline.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);

        Assert.Contains(secret, result.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.BodyText, StringComparison.Ordinal);
        Assert.Equal(3, result.BodyText.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_RestoresPlaceholderSplitAcrossToolArgumentEvents()
    {
        const string token = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
        const string secret = "tool-secret";
        var first = token[..19];
        var second = token[19..];
        var body =
            "data: " + System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { index = 0, delta = new { tool_calls = new[] { new { index = 0, function = new { arguments = first } } } } },
                },
            }) + "\n\n" +
            "data: " + System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { index = 0, delta = new { tool_calls = new[] { new { index = 0, function = new { arguments = second } } } } },
                },
            }) + "\n\n" +
            "data: [DONE]\n\n";
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, body, "text/event-stream"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions");
        var pipeline = new ProviderExecutionPipeline(responsePipeline: new ReplacingResponsePipeline(token, secret));

        await using var result = await pipeline.ExecuteStreamingAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);
        using var reader = new StreamReader(result.Body, Encoding.UTF8);
        var restored = await reader.ReadToEndAsync();

        Assert.Contains(secret, restored, StringComparison.Ordinal);
        Assert.DoesNotContain(token, restored, StringComparison.Ordinal);
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

    private sealed class ReplacingResponsePipeline(string token, string secret) : IPipeline
    {
        public string PipelineId => "response";
        public ExtensionKind Kind => ExtensionKind.Response;

        public ValueTask<PipelineResult> ExecuteAsync(
            string payload,
            CancellationToken cancellationToken = default)
        {
            var replaced = payload.Replace(token, secret, StringComparison.Ordinal);
            return ValueTask.FromResult(string.Equals(payload, replaced, StringComparison.Ordinal)
                ? PipelineResult.Pass(payload)
                : PipelineResult.Modify(replaced));
        }
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

    private sealed class StaticBinaryResponseHandler(
        HttpStatusCode statusCode,
        byte[] body,
        string mediaType,
        string contentEncoding) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            content.Headers.ContentEncoding.Add(contentEncoding);
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
        }
    }

    private static byte[] CompressGzip(string body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
        {
            writer.Write(body);
        }

        return output.ToArray();
    }

    private sealed class CapturingPipeline : IProviderExecutionPipeline
    {
        public ProviderExecutionContext? Context { get; private set; }

        public async Task<ProviderStreamingResult> ExecuteStreamingAsync(
            HttpClient httpClient, HttpRequestMessage request, ProviderExecutionContext context, CancellationToken cancellationToken)
        {
            var result = await ExecuteAsync(httpClient, request, context, cancellationToken);
            var response = new HttpResponseMessage(result.StatusCode) { Content = new ByteArrayContent(result.Body) };
            return new ProviderStreamingResult(response, await response.Content.ReadAsStreamAsync(cancellationToken));
        }

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
