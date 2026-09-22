using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// 小助手诊断测试器：Provider / Model / Endpoint 的结构化连通性测试。
/// Phase 2 基础版（DNS/TLS/HTTP/Auth/Models/Chat）；完整 Diagnostic Subagent 属 Phase 3。
/// 输出只含安全摘要，绝不包含 API Key 或响应正文。
/// </summary>
public sealed class AssistantTester
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient httpClient;
    private readonly ConfigurationManagementService configuration;
    private readonly IDbContextFactory<ConfigurationDbContext> dbContextFactory;
    private readonly ILogger<AssistantTester>? logger;

    public AssistantTester(
        HttpClient httpClient,
        ConfigurationManagementService configuration,
        IDbContextFactory<ConfigurationDbContext> dbContextFactory,
        ILogger<AssistantTester>? logger = null)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
        this.dbContextFactory = dbContextFactory;
        this.logger = logger;
    }

    public async Task<JsonObject> TestProviderAsync(string idOrBusinessId, CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        var provider = FindProvider(providers, idOrBusinessId)
            ?? throw new KeyNotFoundException($"Provider '{idOrBusinessId}' 不存在。");

        var endpoint = string.IsNullOrWhiteSpace(provider.ModelListUrl)
            ? provider.BaseUrl.TrimEnd('/') + "/models"
            : provider.ModelListUrl;
        var result = new JsonObject
        {
            ["provider"] = provider.BusinessId,
            ["display_name"] = provider.DisplayName,
            ["api_mode"] = provider.ApiMode,
        };

        var headers = ParseHeaders(provider.HeadersJson);
        var probe = await ProbeAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
                }
                foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                return request;
            },
            cancellationToken);

        result["reachable"] = probe.Reachable;
        result["authenticated"] = probe.StatusCode is not (401 or 403);
        if (probe.StatusCode is int statusCode) result["status_code"] = statusCode;
        if (probe.LatencyMs is long latencyMs) result["latency_ms"] = latencyMs;

        var (modelsFound, modelsParseOk) = probe.Reachable && probe.StatusCode is >= 200 and < 300
            ? CountModels(probe.Body)
            : (0, false);
        result["models_found"] = modelsFound;

        result["diagnosis"] = probe switch
        {
            { Failure: "timeout" } => "timeout",
            { Failure: "unreachable" } => "unreachable",
            { StatusCode: 401 or 403 } => "auth_failed",
            { StatusCode: 404 or 405 } => "endpoint_not_found",
            { StatusCode: 429 } => "rate_limited",
            { StatusCode: >= 500 } => "upstream_error",
            { StatusCode: >= 200 and < 300 } when !modelsParseOk => "invalid_model_list",
            { StatusCode: >= 200 and < 300 } => "provider_ok",
            _ => "request_rejected",
        };
        logger?.LogInformation("AI 助手测试 Provider {ProviderId} {Diagnosis} {StatusCode}", provider.BusinessId, result["diagnosis"]?.GetValue<string>(), probe.StatusCode);
        return result;
    }

    public async Task<JsonObject> TestModelAsync(Guid modelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.Models.AsNoTracking().Include(item => item.Provider).SingleOrDefaultAsync(item => item.Id == modelId, cancellationToken)
            ?? throw new KeyNotFoundException("模型不存在。");
        var provider = model.Provider;

        var baseUrl = (model.BaseUrl ?? provider.BaseUrl).TrimEnd('/');
        var apiMode = (model.ApiMode ?? provider.ApiMode).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        var apiKey = ReadApiKey(model.ProtectedApiKey) ?? ReadApiKey(provider.ProtectedApiKey);
        var headers = ParseHeaders(provider.HeadersJson);

        var result = new JsonObject
        {
            ["model"] = model.ModelId,
            ["provider"] = provider.BusinessId,
            ["api_mode"] = apiMode,
        };

        var probe = await ProbeAsync(
            () => apiMode switch
            {
                "anthropic" => BuildAnthropicChatRequest(baseUrl, model.ModelId, apiKey, headers),
                "ollama" => BuildOllamaChatRequest(baseUrl, model.ModelId, apiKey, headers),
                _ => BuildOpenAiChatRequest(baseUrl, model.ModelId, apiKey, headers),
            },
            cancellationToken);

        result["chat_test"] = probe is { Reachable: true, StatusCode: >= 200 and < 300 };
        result["reachable"] = probe.Reachable;
        if (probe.StatusCode is int statusCode) result["status_code"] = statusCode;
        if (probe.LatencyMs is long latencyMs) result["latency_ms"] = latencyMs;
        result["diagnosis"] = probe switch
        {
            { Failure: "timeout" } => "timeout",
            { Failure: "unreachable" } => "unreachable",
            { StatusCode: 401 or 403 } => "auth_failed",
            { StatusCode: 404 } => "model_not_found",
            { StatusCode: 429 } => "rate_limited",
            { StatusCode: >= 500 } => "upstream_error",
            { StatusCode: >= 200 and < 300 } => "model_ok",
            _ => "request_rejected",
        };
        logger?.LogInformation("AI 助手测试模型 {ProviderId}/{ModelId} {Diagnosis} {StatusCode}", provider.BusinessId, model.ModelId, result["diagnosis"]?.GetValue<string>(), probe.StatusCode);
        return result;
    }

    public async Task<JsonObject> TestEndpointAsync(string key, CancellationToken cancellationToken)
    {
        var endpoints = await configuration.ListGatewayEndpointsAsync(cancellationToken);
        var endpoint = endpoints.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Endpoint '{key}' 不存在。");

        var enabledCombos = endpoint.Combos.Count(combo => combo is { ComboEnabled: true, Enabled: true });
        var requiresApiKey = GatewayEndpointSettings.RequiresApiKey(endpoint.Key);
        var hasApiKey = !string.IsNullOrWhiteSpace(endpoint.ApiKey);

        var result = new JsonObject
        {
            ["endpoint"] = endpoint.Key,
            ["display_name"] = endpoint.DisplayName,
            ["public_path"] = endpoint.PublicPath,
            ["enabled"] = endpoint.Enabled,
            ["combos_bound"] = endpoint.Combos.Count,
            ["combos_enabled"] = enabledCombos,
            ["api_key"] = SecretBoundary.Describe(hasApiKey, SecretBoundary.EndpointApiKeyRef(endpoint.Key)),
        };
        result["diagnosis"] = endpoint switch
        {
            { Enabled: false } => "endpoint_disabled",
            _ when requiresApiKey && !hasApiKey => "api_key_missing",
            _ when enabledCombos == 0 => "no_enabled_combo",
            _ => "endpoint_ok",
        };
        logger?.LogInformation("AI 助手测试 Endpoint {EndpointKey} {Diagnosis}", endpoint.Key, result["diagnosis"]?.GetValue<string>());
        return result;
    }

    private static ProviderResponse? FindProvider(IReadOnlyList<ProviderResponse> providers, string idOrBusinessId) =>
        Guid.TryParse(idOrBusinessId, out var id)
            ? providers.FirstOrDefault(item => item.Id == id)
            : providers.FirstOrDefault(item => string.Equals(item.BusinessId, idOrBusinessId, StringComparison.OrdinalIgnoreCase));

    private static HttpRequestMessage BuildOpenAiChatRequest(string baseUrl, string modelId, string? apiKey, IReadOnlyDictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent(new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "ping" }),
                ["max_tokens"] = 1,
                ["stream"] = false,
            }),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return request;
    }

    private static HttpRequestMessage BuildAnthropicChatRequest(string baseUrl, string modelId, string? apiKey, IReadOnlyDictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent(new JsonObject
            {
                ["model"] = modelId,
                ["max_tokens"] = 1,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "ping" }),
            }),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return request;
    }

    private static HttpRequestMessage BuildOllamaChatRequest(string baseUrl, string modelId, string? apiKey, IReadOnlyDictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
        {
            Content = JsonContent(new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "ping" }),
                ["stream"] = false,
            }),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return request;
    }

    private static StringContent JsonContent(JsonObject payload) =>
        new(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    private static string? ReadApiKey(string? protectedValue) =>
        string.IsNullOrWhiteSpace(protectedValue) || !ProtectedApiKeyStore.TryUnprotect(protectedValue, out var plainText) ? null : plainText;

    private static IReadOnlyDictionary<string, string> ParseHeaders(string headersJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static (int ModelCount, bool ParseOk) CountModels(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            JsonElement items = root.ValueKind switch
            {
                JsonValueKind.Array => root,
                JsonValueKind.Object when root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data,
                JsonValueKind.Object when root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array => models,
                _ => default,
            };
            return items.ValueKind == JsonValueKind.Array ? (items.GetArrayLength(), true) : (0, false);
        }
        catch (JsonException)
        {
            return (0, false);
        }
    }

    private async Task<ProbeResult> ProbeAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultProbeTimeout);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var request = requestFactory();
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return new ProbeResult(true, (int)response.StatusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, null, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(false, null, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, "timeout", []);
        }
        catch (HttpRequestException)
        {
            return new ProbeResult(false, null, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, "unreachable", []);
        }
    }

    private sealed record ProbeResult(bool Reachable, int? StatusCode, long? LatencyMs, string? Failure, byte[] Body);
}
