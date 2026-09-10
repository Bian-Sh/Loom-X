using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using LoomX.Activity;
using LoomX.Configuration;

namespace LoomX.Services;

public interface IProtocolPassthroughClient
{
    Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken);
    Task<bool> ProxyGatewayAttemptAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken);
    Task<bool> ProxyOpenAiResponsesGatewayAttemptAsync(HttpContext httpContext, ResolvedModelConfig model, JsonObject payload, CancellationToken cancellationToken);
}

public sealed class ProtocolPassthroughClient : IProtocolPassthroughClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly ILogger<ProtocolPassthroughClient> logger;
    private readonly RequestTelemetryHub? telemetryHub;
    private readonly IProviderExecutionPipeline executionPipeline;

    public ProtocolPassthroughClient(
        HttpClient httpClient,
        ILogger<ProtocolPassthroughClient> logger,
        RequestTelemetryHub? telemetryHub = null,
        IProviderExecutionPipeline? executionPipeline = null)
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.telemetryHub = telemetryHub;
        this.executionPipeline = executionPipeline ?? new ProviderExecutionPipeline();
    }

    public async Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestContext = httpContext.Items[ActivityContextKeys.Request] as ActivityRequestContext;
        var attemptIndex = requestContext?.AttemptIndex ?? 0;
        if (requestContext is not null) telemetryHub?.EdgeAttemptStarted(requestContext, model.ProviderId, model.ModelId, attemptIndex);
        try
        {
            using var upstreamRequest = BuildRequestMessage(httpContext, model, apiMode, upstreamPath, payload);
            var result = await executionPipeline.ExecuteAsync(
                httpClient,
                upstreamRequest,
                new ProviderExecutionContext(model.ProviderId, model.ModelId, apiMode, upstreamPath, NormalizeOpenAiFinishReasons: true),
                cancellationToken);

            httpContext.Response.StatusCode = (int)result.StatusCode;
            CopyResponseHeaders(result, httpContext.Response);
            if (result.WasNormalized) httpContext.Response.Headers.ContentLength = null;

            var contentType = result.ContentType
                ?? httpContext.Response.ContentType
                ?? "application/octet-stream";
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (result.IsSuccess)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)elapsedMs, attemptIndex);
                logger.LogInformation(
                    "代理请求完成 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    apiMode,
                    upstreamPath,
                    (int)result.StatusCode,
                    contentType,
                    result.Body.Length,
                    elapsedMs);
            }
            else
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)elapsedMs, false, attemptIndex);
                logger.LogError(
                    "代理请求失败 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    apiMode,
                    upstreamPath,
                    (int)result.StatusCode,
                    contentType,
                    result.Body.Length,
                    elapsedMs);
            }

            await httpContext.Response.Body.WriteAsync(result.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (requestContext is not null) telemetryHub?.EdgeAttemptCancelled(requestContext, model.ProviderId, model.ModelId, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attemptIndex);
            logger.LogDebug(
                "代理请求已取消 {ProviderId}/{ModelId} {ApiMode} {Path} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                apiMode,
                upstreamPath,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, null, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, false, attemptIndex, exception.GetType().Name);
            logger.LogError(
                exception,
                "代理请求异常 {ProviderId}/{ModelId} {ApiMode} {Path} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                apiMode,
                upstreamPath,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    public async Task<bool> ProxyGatewayAttemptAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestContext = httpContext.Items[ActivityContextKeys.Request] as ActivityRequestContext;
        var attemptIndex = requestContext?.AttemptIndex ?? 0;
        if (requestContext is not null) telemetryHub?.EdgeAttemptStarted(requestContext, model.ProviderId, model.ModelId, attemptIndex);
        try
        {
            using var upstreamRequest = BuildRequestMessage(httpContext, model, apiMode, upstreamPath, payload);
            var result = await executionPipeline.ExecuteAsync(
                httpClient,
                upstreamRequest,
                new ProviderExecutionContext(model.ProviderId, model.ModelId, apiMode, upstreamPath, NormalizeOpenAiFinishReasons: true),
                cancellationToken);
            var retryable = result.IsRetryable;
            if (retryable)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, true, attemptIndex);
                logger.LogWarning("网关路由尝试可转移 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ResponseBytes}B {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, (int)result.StatusCode, result.Body.Length, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                return false;
            }
            httpContext.Response.StatusCode = (int)result.StatusCode;
            CopyResponseHeaders(result, httpContext.Response);
            if (result.WasNormalized) httpContext.Response.Headers.ContentLength = null;
            await httpContext.Response.Body.WriteAsync(result.Body, cancellationToken);
            if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attemptIndex);
            logger.LogInformation("网关路由尝试完成 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ResponseBytes}B {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, (int)result.StatusCode, result.Body.Length, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (requestContext is not null) telemetryHub?.EdgeAttemptCancelled(requestContext, model.ProviderId, model.ModelId, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attemptIndex);
            throw;
        }
        catch (Exception exception)
        {
            if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, null, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, true, attemptIndex, exception.GetType().Name);
            logger.LogWarning(exception, "网关路由尝试异常，将尝试下一条路由 {ProviderId}/{ModelId} {ApiMode} {Path} {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return false;
        }
    }

    public async Task<bool> ProxyOpenAiResponsesGatewayAttemptAsync(HttpContext httpContext, ResolvedModelConfig model, JsonObject payload, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestContext = httpContext.Items[ActivityContextKeys.Request] as ActivityRequestContext;
        var attemptIndex = requestContext?.AttemptIndex ?? 0;
        if (requestContext is not null) telemetryHub?.EdgeAttemptStarted(requestContext, model.ProviderId, model.ModelId, attemptIndex);

        try
        {
            var responsesRequest = OpenAiResponsesBridge.CreateResponsesRequest(payload);
            using var upstreamRequest = BuildRequestMessage(httpContext, model, "openai", "/responses", responsesRequest);
            var result = await executionPipeline.ExecuteAsync(
                httpClient,
                upstreamRequest,
                new ProviderExecutionContext(model.ProviderId, model.ModelId, "openai", "/responses"),
                cancellationToken);
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var responseBytes = result.Body.Length;
            var retryable = result.IsRetryable;
            if (retryable)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)elapsedMs, true, attemptIndex);
                logger.LogWarning(
                    "Responses 协议桥接上游可转移 {ProviderId}/{ModelId} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    (int)result.StatusCode,
                    result.ContentType ?? "unknown",
                    responseBytes,
                    elapsedMs);
                return false;
            }

            httpContext.Response.StatusCode = (int)result.StatusCode;
            if (!result.IsSuccess)
            {
                CopyResponseHeaders(result, httpContext.Response);
                await httpContext.Response.Body.WriteAsync(result.Body, cancellationToken);
                if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)elapsedMs, attemptIndex);
                logger.LogInformation(
                    "Responses 协议桥接上游拒绝 {ProviderId}/{ModelId} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    (int)result.StatusCode,
                    result.ContentType ?? "unknown",
                    responseBytes,
                    elapsedMs);
                return true;
            }

            var contentType = result.ContentType;
            string downstreamPayload;
            string downstreamContentType;
            try
            {
                var responseBody = result.BodyText;
                if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    downstreamPayload = OpenAiResponsesBridge.CreateChatCompletionsSse(responseBody, model.ModelId);
                    downstreamContentType = "text/event-stream";
                }
                else if (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
                    && JsonNode.Parse(responseBody) is JsonObject responsesResponse)
                {
                    downstreamPayload = OpenAiResponsesBridge.CreateChatCompletionsResponse(responsesResponse, model.ModelId).ToJsonString(JsonOptions);
                    downstreamContentType = "application/json";
                }
                else
                {
                    throw new InvalidDataException("Responses 协议桥接收到不支持的成功响应内容类型。");
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)elapsedMs, true, attemptIndex, exception.GetType().Name);
                logger.LogWarning(
                    exception,
                    "Responses 协议桥接转换失败，将尝试下一条路由 {ProviderId}/{ModelId} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    contentType,
                    responseBytes,
                    elapsedMs);
                return false;
            }

            CopyResponseHeaders(result, httpContext.Response);
            httpContext.Response.ContentType = downstreamContentType;
            httpContext.Response.ContentLength = null;
            httpContext.Response.Headers.Remove("content-encoding");
            await httpContext.Response.WriteAsync(downstreamPayload, Encoding.UTF8, cancellationToken);
            if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)result.StatusCode, (long)elapsedMs, attemptIndex);
            logger.LogInformation(
                "Responses 协议桥接完成 {ProviderId}/{ModelId} {StatusCode} {SourceContentType} {OutputContentType} {ResponseBytes}B {OutputBytes}B {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                (int)result.StatusCode,
                contentType,
                downstreamContentType,
                responseBytes,
                Encoding.UTF8.GetByteCount(downstreamPayload),
                elapsedMs);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (requestContext is not null) telemetryHub?.EdgeAttemptCancelled(requestContext, model.ProviderId, model.ModelId, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attemptIndex);
            throw;
        }
        catch (Exception exception)
        {
            if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, null, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, true, attemptIndex, exception.GetType().Name);
            logger.LogWarning(
                exception,
                "Responses 协议桥接异常，将尝试下一条路由 {ProviderId}/{ModelId} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return false;
        }
    }

    private static HttpRequestMessage BuildRequestMessage<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload)
    {
        var upstreamUri = $"{model.BaseUrl.TrimEnd('/')}{upstreamPath}{httpContext.Request.QueryString}";
        var upstreamRequest = new HttpRequestMessage(new HttpMethod(httpContext.Request.Method), upstreamUri);

        if (payload is not null)
        {
            var requestBody = JsonSerializer.Serialize(payload, JsonOptions);
            upstreamRequest.Content = new StringContent(requestBody, Encoding.UTF8);
            upstreamRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(httpContext.Request.ContentType ?? "application/json");
        }

        CopyRequestHeaders(httpContext.Request, upstreamRequest);
        ApplyDefaultProtocolHeaders(upstreamRequest, model, apiMode);
        ApplyConfiguredHeaders(upstreamRequest, model.Headers);
        return upstreamRequest;
    }

    private static void CopyRequestHeaders(HttpRequest request, HttpRequestMessage upstreamRequest)
    {
        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && upstreamRequest.Content is not null)
            {
                upstreamRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }
    }

    private static void ApplyDefaultProtocolHeaders(HttpRequestMessage upstreamRequest, ResolvedModelConfig model, string apiMode)
    {
        if (string.Equals(apiMode, "openai", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(model.ApiKey))
        {
            upstreamRequest.Headers.Authorization = null;
            upstreamRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
            return;
        }

        if (string.Equals(apiMode, "anthropic", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(model.ApiKey))
        {
            AddOrReplaceHeader(upstreamRequest.Headers, "x-api-key", model.ApiKey);
            AddOrReplaceHeader(upstreamRequest.Headers, "anthropic-version", "2023-06-01");
        }
    }

    private static void ApplyConfiguredHeaders(HttpRequestMessage upstreamRequest, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value) && upstreamRequest.Content is not null)
            {
                upstreamRequest.Content.Headers.Remove(header.Key);
                upstreamRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                continue;
            }

            if (upstreamRequest.Headers.Contains(header.Key))
            {
                upstreamRequest.Headers.Remove(header.Key);
                upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    private static void AddOrReplaceHeader(HttpRequestHeaders headers, string name, string value)
    {
        headers.Remove(name);
        headers.TryAddWithoutValidation(name, value);
    }

    private static void CopyResponseHeaders(ProviderExecutionResult result, HttpResponse downstreamResponse)
    {
        foreach (var header in result.Headers)
        {
            downstreamResponse.Headers[header.Key] = header.Value;
        }

        downstreamResponse.Headers.Remove("transfer-encoding");
    }
}
