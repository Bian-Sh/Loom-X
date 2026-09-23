using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using LoomX.Configuration;
using LoomX.Contracts;

namespace LoomX.Services;

public interface IAnthropicProxyClient
{
    Task<(HttpStatusCode StatusCode, AnthropicMessagesResponse? Response, string? Error)> SendAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken);

    Task<(HttpStatusCode StatusCode, Stream? Stream, string? Error)> SendStreamAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken);
}

public sealed class AnthropicProxyClient : IAnthropicProxyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly ILogger<AnthropicProxyClient> logger;
    private readonly IProviderExecutionPipeline executionPipeline;

    public AnthropicProxyClient(
        HttpClient httpClient,
        ILogger<AnthropicProxyClient> logger,
        IProviderExecutionPipeline? executionPipeline = null)
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.executionPipeline = executionPipeline ?? new ProviderExecutionPipeline();
    }

    public async Task<(HttpStatusCode StatusCode, AnthropicMessagesResponse? Response, string? Error)> SendAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var message = BuildRequestMessage(model, request);
            LogEndpointAnomaly(model, message.RequestUri);
            var execution = await executionPipeline.ExecuteAsync(
                httpClient,
                message,
                new ProviderExecutionContext(model.ProviderId, model.ModelId, "anthropic", "/v1/messages"),
                cancellationToken);
            var body = execution.BodyText;
            var contentType = execution.ContentType ?? "application/json";
            var responseBytes = execution.Body.Length;
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (!execution.IsSuccess)
            {
                logger.LogError(
                    "Anthropic 请求失败 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    message.RequestUri?.AbsolutePath ?? "/v1/messages",
                    (int)execution.StatusCode,
                    contentType,
                    responseBytes,
                    elapsedMs);
                return (execution.StatusCode, null, ReadError(body, execution.StatusCode));
            }

            var result = JsonSerializer.Deserialize<AnthropicMessagesResponse>(body, JsonOptions);
            if (result is null)
            {
                logger.LogError(
                    "Anthropic 响应解析失败 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    message.RequestUri?.AbsolutePath ?? "/v1/messages",
                    (int)execution.StatusCode,
                    contentType,
                    responseBytes,
                    elapsedMs);
                return (HttpStatusCode.BadGateway, null, "Anthropic 返回了空响应。");
            }

            logger.LogInformation(
                "Anthropic 请求完成 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                message.RequestUri?.AbsolutePath ?? "/v1/messages",
                (int)execution.StatusCode,
                contentType,
                responseBytes,
                elapsedMs);

            return (execution.StatusCode, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Anthropic 请求已取消 {ProviderId}/{ModelId} {Path} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                "/v1/messages",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Anthropic 请求异常 {ProviderId}/{ModelId} {Path} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                "/v1/messages",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    public async Task<(HttpStatusCode StatusCode, Stream? Stream, string? Error)> SendStreamAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var message = BuildRequestMessage(model, request);
            LogEndpointAnomaly(model, message.RequestUri);
            var execution = await executionPipeline.ExecuteStreamingAsync(
                httpClient,
                message,
                new ProviderExecutionContext(model.ProviderId, model.ModelId, "anthropic", "/v1/messages"),
                cancellationToken);
            var path = message.RequestUri?.AbsolutePath ?? "/v1/messages";
            var contentType = execution.ContentType ?? "text/event-stream";

            if (!execution.IsSuccess)
            {
                await using var failedExecution = execution;
                using var reader = new StreamReader(execution.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(cancellationToken);
                logger.LogError(
                    "Anthropic 流式请求失败 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    path,
                    (int)execution.StatusCode,
                    contentType,
                    Encoding.UTF8.GetByteCount(body),
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

                return (execution.StatusCode, null, ReadError(body, execution.StatusCode));
            }

            logger.LogInformation(
                "Anthropic 流式请求已连接 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                path,
                (int)execution.StatusCode,
                contentType,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return (execution.StatusCode, new ProviderStreamingBodyStream(execution), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Anthropic 流式请求已取消 {ProviderId}/{ModelId} {Path} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                "/v1/messages",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Anthropic 流式请求异常 {ProviderId}/{ModelId} {Path} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                "/v1/messages",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    private void LogEndpointAnomaly(ResolvedModelConfig model, Uri? endpoint)
    {
        if (!ProviderRouteEndpointResolver.HasRepeatedVersionSegment(endpoint)) return;
        logger.LogWarning(
            "Anthropic 真实路由上游地址存在重复版本段 {ProviderId}/{ModelId} {EndpointPath}",
            model.ProviderId,
            model.ModelId,
            endpoint!.AbsolutePath);
    }

    private static HttpRequestMessage BuildRequestMessage(ResolvedModelConfig model, AnthropicMessagesRequest request)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            ProviderRouteEndpointResolver.Resolve(model.BaseUrl, "/v1/messages"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        message.Headers.Add("x-api-key", model.ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");

        var payload = JsonSerializer.Serialize(request, JsonOptions);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        foreach (var header in model.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }

    /// <summary>把 ProviderStreamingResult 的生命周期绑定到返回给协议映射层的响应流。</summary>
    private sealed class ProviderStreamingBodyStream(ProviderStreamingResult owner) : Stream
    {
        private readonly Stream inner = owner.Body;
        private bool disposed;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                owner.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                await owner.DisposeAsync();
            }
            GC.SuppressFinalize(this);
        }
    }

    private static string ReadError(string body, HttpStatusCode statusCode)
    {
        try
        {
            var error = JsonSerializer.Deserialize<AnthropicErrorEnvelope>(body, JsonOptions);
            return error?.Error?.Message ?? $"Anthropic 请求失败，状态码 {(int)statusCode}。";
        }
        catch
        {
            return $"Anthropic 请求失败，状态码 {(int)statusCode}。";
        }
    }
}
