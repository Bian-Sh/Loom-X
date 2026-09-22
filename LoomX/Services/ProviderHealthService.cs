using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Services;

public enum ProviderHealthState
{
    Unknown,
    Checking,
    Healthy,
    HealthyEmptyModels,
    AuthFailed,
    ConfigurationError,
    EndpointError,
    Unavailable,
    RateLimited,
    RequestRejected,
    UpstreamError,
    ProtocolError,
    Disabled
}

public enum ProviderHealthFailureKind
{
    None,
    Configuration,
    Authentication,
    Endpoint,
    Unavailable,
    RateLimited,
    RequestRejected,
    Upstream,
    Protocol
}

public sealed record ProviderHealthCheckRequest(
    string ProviderId,
    bool Enabled,
    string BaseUrl,
    string? ModelListUrl,
    string? ApiKey,
    IReadOnlyDictionary<string, string> Headers,
    int IncompleteHeaderCount,
    bool UseProxy);

public sealed record ProviderHealthResult(
    ProviderHealthState State,
    ProviderHealthFailureKind FailureKind = ProviderHealthFailureKind.None,
    int? StatusCode = null,
    long? LatencyMs = null,
    int? DiscoveredModelCount = null,
    string? FailureCode = null);

public interface IProviderHealthService
{
    Task<ProviderHealthResult> CheckAsync(ProviderHealthCheckRequest request, CancellationToken cancellationToken = default);
}

public sealed class ProviderHealthService : IProviderHealthService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<ProviderHealthService> logger;

    public ProviderHealthService(HttpClient httpClient, ILogger<ProviderHealthService>? logger = null)
    {
        this.httpClient = httpClient;
        this.logger = logger ?? NullLogger<ProviderHealthService>.Instance;
    }

    public async Task<ProviderHealthResult> CheckAsync(ProviderHealthCheckRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Enabled)
            return new(ProviderHealthState.Disabled);

        if (request.IncompleteHeaderCount > 0)
            return new(ProviderHealthState.ConfigurationError, ProviderHealthFailureKind.Configuration, FailureCode: "incomplete_headers");

        if (!TryCreateEndpoint(request, out var endpoint))
            return new(ProviderHealthState.ConfigurationError, ProviderHealthFailureKind.Configuration, FailureCode: "invalid_url");

        logger.LogInformation("Provider 验证开始 {ProviderId} {UseProxy}", request.ProviderId, request.UseProxy);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());

            foreach (var header in request.Headers)
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            var statusCode = (int)response.StatusCode;
            var result = !response.IsSuccessStatusCode
                ? MapHttpFailure(response.StatusCode, stopwatch.ElapsedMilliseconds)
                : ParseSuccess(responseBytes, stopwatch.ElapsedMilliseconds);

            logger.LogInformation(
                "Provider 验证完成 {ProviderId} {State} {StatusCode} {ElapsedMs}ms {ModelCount}",
                request.ProviderId,
                result.State,
                statusCode,
                result.LatencyMs,
                result.DiscoveredModelCount);
            return result with { StatusCode = statusCode };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogWarning("Provider 验证超时 {ProviderId} {ElapsedMs}ms", request.ProviderId, stopwatch.ElapsedMilliseconds);
            return new(ProviderHealthState.Unavailable, ProviderHealthFailureKind.Unavailable, LatencyMs: stopwatch.ElapsedMilliseconds, FailureCode: "timeout");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            logger.LogWarning(exception, "Provider 验证无法访问 {ProviderId} {ElapsedMs}ms", request.ProviderId, stopwatch.ElapsedMilliseconds);
            return new(ProviderHealthState.Unavailable, ProviderHealthFailureKind.Unavailable, LatencyMs: stopwatch.ElapsedMilliseconds, FailureCode: "unavailable");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(exception, "Provider 验证异常 {ProviderId} {ElapsedMs}ms", request.ProviderId, stopwatch.ElapsedMilliseconds);
            return new(ProviderHealthState.Unavailable, ProviderHealthFailureKind.Unavailable, LatencyMs: stopwatch.ElapsedMilliseconds, FailureCode: "unexpected");
        }
    }

    private static bool TryCreateEndpoint(ProviderHealthCheckRequest request, out Uri endpoint)
    {
        if (!Uri.TryCreate(request.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(baseUri.Host))
        {
            endpoint = null!;
            return false;
        }

        var value = string.IsNullOrWhiteSpace(request.ModelListUrl)
            ? request.BaseUrl.TrimEnd('/') + "/models"
            : request.ModelListUrl.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out endpoint!)
            && endpoint.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(endpoint.Host);
    }

    private static ProviderHealthResult MapHttpFailure(HttpStatusCode statusCode, long elapsedMs) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(ProviderHealthState.AuthFailed, ProviderHealthFailureKind.Authentication, LatencyMs: elapsedMs, FailureCode: "auth_failed"),
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => new(ProviderHealthState.EndpointError, ProviderHealthFailureKind.Endpoint, LatencyMs: elapsedMs, FailureCode: "endpoint_error"),
        (HttpStatusCode)429 => new(ProviderHealthState.RateLimited, ProviderHealthFailureKind.RateLimited, LatencyMs: elapsedMs, FailureCode: "rate_limited"),
        >= HttpStatusCode.InternalServerError => new(ProviderHealthState.UpstreamError, ProviderHealthFailureKind.Upstream, LatencyMs: elapsedMs, FailureCode: "upstream_error"),
        _ => new(ProviderHealthState.RequestRejected, ProviderHealthFailureKind.RequestRejected, LatencyMs: elapsedMs, FailureCode: "request_rejected")
    };

    private static ProviderHealthResult ParseSuccess(byte[] responseBytes, long elapsedMs)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBytes);
            var items = FindModelArray(document.RootElement);
            if (items is null)
                return new(ProviderHealthState.ProtocolError, ProviderHealthFailureKind.Protocol, LatencyMs: elapsedMs, FailureCode: "invalid_response");

            var itemCount = items.Value.GetArrayLength();
            if (itemCount == 0)
                return new(ProviderHealthState.HealthyEmptyModels, LatencyMs: elapsedMs, DiscoveredModelCount: 0);

            var validCount = items.Value.EnumerateArray().Count(IsModelItem);
            return validCount == 0
                ? new(ProviderHealthState.ProtocolError, ProviderHealthFailureKind.Protocol, LatencyMs: elapsedMs, FailureCode: "invalid_models")
                : new(ProviderHealthState.Healthy, LatencyMs: elapsedMs, DiscoveredModelCount: validCount);
        }
        catch (JsonException)
        {
            return new(ProviderHealthState.ProtocolError, ProviderHealthFailureKind.Protocol, LatencyMs: elapsedMs, FailureCode: "invalid_json");
        }
    }

    private static JsonElement? FindModelArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array) return data;
        if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array) return models;
        return null;
    }

    private static bool IsModelItem(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && (HasTextProperty(value, "id") || HasTextProperty(value, "name") || HasTextProperty(value, "model"));

    private static bool HasTextProperty(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(property.GetString());
}
