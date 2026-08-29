using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using OllamaHub.Configuration;

namespace OllamaHub.Services;

public interface IProtocolPassthroughClient
{
    Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken);
    Task<bool> ProxyGatewayAttemptAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken);
}

public sealed class ProtocolPassthroughClient(HttpClient httpClient, ILogger<ProtocolPassthroughClient> logger) : IProtocolPassthroughClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var upstreamRequest = BuildRequestMessage(httpContext, model, apiMode, upstreamPath, payload);
            using var upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, httpContext.Response);

            await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using var buffer = new MemoryStream();
            await responseStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var contentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                ?? httpContext.Response.ContentType
                ?? "application/octet-stream";
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (upstreamResponse.IsSuccessStatusCode)
            {
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
        try
        {
            using var upstreamRequest = BuildRequestMessage(httpContext, model, apiMode, upstreamPath, payload);
            using var upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using var buffer = new MemoryStream();
            await responseStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            var retryable = (int)upstreamResponse.StatusCode is 408 or 429 or >= 500;
            if (retryable)
            {
                logger.LogWarning("网关路由尝试可转移 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ResponseBytes}B {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, (int)upstreamResponse.StatusCode, buffer.Length, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                return false;
            }
            httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, httpContext.Response);
            await buffer.CopyToAsync(httpContext.Response.Body, cancellationToken);
            logger.LogInformation("网关路由尝试完成 {ProviderId}/{ModelId} {ApiMode} {Path} {StatusCode} {ResponseBytes}B {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, (int)upstreamResponse.StatusCode, buffer.Length, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "网关路由尝试异常，将尝试下一条路由 {ProviderId}/{ModelId} {ApiMode} {Path} {ElapsedMs:F0}ms", model.ProviderId, model.ModelId, apiMode, upstreamPath, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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
}
