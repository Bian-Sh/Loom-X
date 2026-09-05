using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OllamaHub.Activity;
using OllamaHub.Configuration;

namespace OllamaHub.Services;

public interface IProtocolPassthroughClient
{
    Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken);
    Task<bool> ProxyGatewayAttemptAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken);
    Task<bool> ProxyOpenAiResponsesGatewayAttemptAsync(HttpContext httpContext, ResolvedModelConfig model, JsonObject payload, CancellationToken cancellationToken);
}

public sealed class ProtocolPassthroughClient(HttpClient httpClient, ILogger<ProtocolPassthroughClient> logger, RequestTelemetryHub? telemetryHub = null) : IProtocolPassthroughClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestContext = httpContext.Items[ActivityContextKeys.Request] as ActivityRequestContext;
        var attemptIndex = requestContext?.AttemptIndex ?? 0;
        if (requestContext is not null) telemetryHub?.EdgeAttemptStarted(requestContext, model.ProviderId, model.ModelId, attemptIndex);
        try
        {
            using var upstreamRequest = BuildRequestMessage(httpContext, model, apiMode, upstreamPath, payload);
            using var upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, httpContext.Response);

            await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using var buffer = new MemoryStream();
            await responseStream.CopyToAsync(buffer, cancellationToken);
            var responseNormalized = NormalizeOpenAiFinishReasons(buffer, upstreamResponse.Content.Headers.ContentType, apiMode);
            if (responseNormalized) httpContext.Response.Headers.ContentLength = null;
            buffer.Position = 0;

            var contentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                ?? httpContext.Response.ContentType
                ?? "application/octet-stream";
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (upstreamResponse.IsSuccessStatusCode)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)elapsedMs, attemptIndex);
                logger.LogInformation(
                    "代理请求完成 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    apiMode,
                    upstreamPath,
                    (int)upstreamResponse.StatusCode,
                    contentType,
                    buffer.Length,
                    elapsedMs);
            }
            else
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)elapsedMs, false, attemptIndex);
                logger.LogError(
                    "代理请求失败 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    apiMode,
                    upstreamPath,
                    (int)upstreamResponse.StatusCode,
                    contentType,
                    buffer.Length,
                    elapsedMs);
            }

            await buffer.CopyToAsync(httpContext.Response.Body, cancellationToken);
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
            using var upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using var buffer = new MemoryStream();
            await responseStream.CopyToAsync(buffer, cancellationToken);
            var responseNormalized = NormalizeOpenAiFinishReasons(buffer, upstreamResponse.Content.Headers.ContentType, apiMode);
            buffer.Position = 0;
            var retryable = (int)upstreamResponse.StatusCode is 408 or 429 or >= 500;
            if (retryable)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, true, attemptIndex);
                logger.LogWarning("网关路由尝试可转移 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ResponseBytes}B {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, (int)upstreamResponse.StatusCode, buffer.Length, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                return false;
            }
            httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, httpContext.Response);
            if (responseNormalized) httpContext.Response.Headers.ContentLength = null;
            await buffer.CopyToAsync(httpContext.Response.Body, cancellationToken);
            if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, attemptIndex);
            logger.LogInformation("网关路由尝试完成 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ResponseBytes}B {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, (int)upstreamResponse.StatusCode, buffer.Length, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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
            using var upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using var buffer = new MemoryStream();
            await responseStream.CopyToAsync(buffer, cancellationToken);
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var responseBytes = buffer.Length;
            var retryable = (int)upstreamResponse.StatusCode is 408 or 429 or >= 500;
            if (retryable)
            {
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)elapsedMs, true, attemptIndex);
                logger.LogWarning(
                    "Responses 协议桥接上游可转移 {ProviderId}/{ModelId} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    (int)upstreamResponse.StatusCode,
                    upstreamResponse.Content.Headers.ContentType?.MediaType ?? "unknown",
                    responseBytes,
                    elapsedMs);
                return false;
            }

            httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
            if (!upstreamResponse.IsSuccessStatusCode)
            {
                CopyResponseHeaders(upstreamResponse, httpContext.Response);
                buffer.Position = 0;
                await buffer.CopyToAsync(httpContext.Response.Body, cancellationToken);
                if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)elapsedMs, attemptIndex);
                logger.LogInformation(
                    "Responses 协议桥接上游拒绝 {ProviderId}/{ModelId} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    (int)upstreamResponse.StatusCode,
                    upstreamResponse.Content.Headers.ContentType?.MediaType ?? "unknown",
                    responseBytes,
                    elapsedMs);
                return true;
            }

            var contentType = upstreamResponse.Content.Headers.ContentType?.MediaType;
            string downstreamPayload;
            string downstreamContentType;
            try
            {
                var responseBody = Encoding.UTF8.GetString(buffer.ToArray());
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
                if (requestContext is not null) telemetryHub?.EdgeAttemptFailed(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)elapsedMs, true, attemptIndex, exception.GetType().Name);
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

            CopyResponseHeaders(upstreamResponse, httpContext.Response);
            httpContext.Response.ContentType = downstreamContentType;
            httpContext.Response.ContentLength = null;
            httpContext.Response.Headers.Remove("content-encoding");
            await httpContext.Response.WriteAsync(downstreamPayload, Encoding.UTF8, cancellationToken);
            if (requestContext is not null) telemetryHub?.EdgeAttemptCompleted(requestContext, model.ProviderId, model.ModelId, (int)upstreamResponse.StatusCode, (long)elapsedMs, attemptIndex);
            logger.LogInformation(
                "Responses 协议桥接完成 {ProviderId}/{ModelId} {StatusCode} {SourceContentType} {OutputContentType} {ResponseBytes}B {OutputBytes}B {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                (int)upstreamResponse.StatusCode,
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

    private static void CopyResponseHeaders(HttpResponseMessage upstreamResponse, HttpResponse downstreamResponse)
    {
        foreach (var header in upstreamResponse.Headers)
        {
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            downstreamResponse.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            downstreamResponse.Headers[header.Key] = header.Value.ToArray();
        }

        downstreamResponse.Headers.Remove("transfer-encoding");
    }

    private static bool NormalizeOpenAiFinishReasons(MemoryStream buffer, MediaTypeHeaderValue? contentType, string apiMode)
    {
        if (!string.Equals(apiMode, "openai", StringComparison.OrdinalIgnoreCase) || buffer.Length == 0)
        {
            return false;
        }

        var mediaType = contentType?.MediaType;
        if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var original = Encoding.UTF8.GetString(buffer.ToArray());
        var normalized = string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            ? NormalizeOpenAiSse(original)
            : NormalizeOpenAiJson(original);
        if (normalized is null || string.Equals(original, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        buffer.SetLength(0);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        buffer.Write(bytes, 0, bytes.Length);
        return true;
    }

    private static string? NormalizeOpenAiSse(string body)
    {
        var lines = body.Split('\n');
        var changed = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineEnding = line.EndsWith('\r') ? "\r" : string.Empty;
            var content = lineEnding.Length == 0 ? line : line[..^1];
            if (!content.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = content[5..].Trim();
            if (payload.Length == 0 || string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(payload);
            }
            catch (JsonException)
            {
                continue;
            }

            if (!NormalizeOpenAiFinishReasons(node))
            {
                continue;
            }

            var leadingWhitespaceLength = content[5..].Length - content[5..].TrimStart().Length;
            lines[index] = $"{content[..5]}{content.Substring(5, leadingWhitespaceLength)}{node!.ToJsonString(JsonOptions)}{lineEnding}";
            changed = true;
        }

        return changed ? string.Join('\n', lines) : null;
    }

    private static string? NormalizeOpenAiJson(string body)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        return NormalizeOpenAiFinishReasons(node) ? node!.ToJsonString(JsonOptions) : null;
    }

    private static bool NormalizeOpenAiFinishReasons(JsonNode? node)
    {
        if (node is not JsonObject response || response["choices"] is not JsonArray choices)
        {
            return false;
        }

        var changed = false;
        foreach (var choiceNode in choices)
        {
            if (choiceNode is not JsonObject choice
                || choice["finish_reason"] is not JsonValue finishReason
                || !finishReason.TryGetValue<string>(out var value)
                || value.Length != 0)
            {
                continue;
            }

            choice["finish_reason"] = null;
            changed = true;
        }

        return changed;
    }
}
