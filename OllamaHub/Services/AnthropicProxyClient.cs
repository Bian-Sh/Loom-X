using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using OllamaHub.Configuration;
using OllamaHub.Contracts;

namespace OllamaHub.Services;

public interface IAnthropicProxyClient
{
    Task<(HttpStatusCode StatusCode, AnthropicMessagesResponse? Response, string? Error)> SendAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken);

    Task<(HttpStatusCode StatusCode, Stream? Stream, string? Error)> SendStreamAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken);
}

public sealed class AnthropicProxyClient(HttpClient httpClient, ILogger<AnthropicProxyClient> logger) : IAnthropicProxyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(HttpStatusCode StatusCode, AnthropicMessagesResponse? Response, string? Error)> SendAsync(ResolvedModelConfig model, AnthropicMessagesRequest request, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var message = BuildRequestMessage(model, request);
            using var response = await httpClient.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            var responseBytes = Encoding.UTF8.GetByteCount(body);
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Anthropic 请求失败 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    message.RequestUri?.AbsolutePath ?? "/v1/messages",
                    (int)response.StatusCode,
                    contentType,
                    responseBytes,
                    elapsedMs);
                return (response.StatusCode, null, ReadError(body, response.StatusCode));
            }

            var result = JsonSerializer.Deserialize<AnthropicMessagesResponse>(body, JsonOptions);
            if (result is null)
            {
                logger.LogError(
                    "Anthropic 响应解析失败 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    message.RequestUri?.AbsolutePath ?? "/v1/messages",
                    (int)response.StatusCode,
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
                (int)response.StatusCode,
                contentType,
                responseBytes,
                elapsedMs);

            return (response.StatusCode, result, null);
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
            var message = BuildRequestMessage(model, request);
            var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var path = message.RequestUri?.AbsolutePath ?? "/v1/messages";
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/event-stream";

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Anthropic 流式请求失败 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ResponseBytes}B {ElapsedMs:F0}ms",
                    model.ProviderId,
                    model.ModelId,
                    path,
                    (int)response.StatusCode,
                    contentType,
                    Encoding.UTF8.GetByteCount(body),
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

                var error = ReadError(body, response.StatusCode);
                response.Dispose();
                message.Dispose();
                return (response.StatusCode, null, error);
            }

            logger.LogInformation(
                "Anthropic 流式请求已连接 {ProviderId}/{ModelId} {Path} {StatusCode} {ContentType} {ElapsedMs:F0}ms",
                model.ProviderId,
                model.ModelId,
                path,
                (int)response.StatusCode,
                contentType,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            message.Dispose();
            return (response.StatusCode, await response.Content.ReadAsStreamAsync(cancellationToken), null);
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

    private static HttpRequestMessage BuildRequestMessage(ResolvedModelConfig model, AnthropicMessagesRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"{model.BaseUrl}/v1/messages");
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
