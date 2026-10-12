using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using LoomX.Services;
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
    private readonly IProviderTestService modelTestService;

    public AssistantTester(
        HttpClient httpClient,
        ConfigurationManagementService configuration,
        IDbContextFactory<ConfigurationDbContext> dbContextFactory,
        ILogger<AssistantTester>? logger = null,
        IProviderTestService? modelTestService = null)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
        this.dbContextFactory = dbContextFactory;
        this.logger = logger;
        this.modelTestService = modelTestService ?? new ProviderTestService(httpClient);
    }

    public async Task<JsonObject> TestUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("url 必须是无内嵌凭据的 HTTP/HTTPS 绝对地址。");
        var result = new JsonObject { ["host"] = uri.Host, ["scheme"] = uri.Scheme, ["port"] = uri.Port };
        logger?.LogInformation("AI 助手开始测试网址 {Host} {Scheme} {Port}", uri.Host, uri.Scheme, uri.Port);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultProbeTimeout);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await httpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = response.StatusCode;
            if (status is System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, uri);
                using var fallback = await httpClient.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                status = fallback.StatusCode;
            }
            result["reachable"] = true;
            result["http_success"] = (int)status is >= 200 and < 300;
            result["status_code"] = (int)status;
            result["diagnosis"] = (int)status is >= 200 and < 300 ? "url_ok" : "http_error";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result["reachable"] = false;
            result["http_success"] = false;
            result["diagnosis"] = "timeout";
        }
        catch (HttpRequestException)
        {
            result["reachable"] = false;
            result["http_success"] = false;
            result["diagnosis"] = "unreachable";
        }
        result["latency_ms"] = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result["http_success"]!.GetValue<bool>())
            logger?.LogInformation("AI 助手网址测试完成 {Host} {StatusCode} {ElapsedMs}ms", uri.Host, result["status_code"]?.GetValue<int>(), result["latency_ms"]!.GetValue<long>());
        else
            logger?.LogWarning("AI 助手网址测试未通过 {Host} {Diagnosis} {StatusCode}", uri.Host, result["diagnosis"]!.GetValue<string>(), result["status_code"]?.GetValue<int>());
        return result;
    }

    public async Task<JsonObject> GetModelApiAsync(Guid modelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.Models.AsNoTracking().Include(item => item.Provider).SingleOrDefaultAsync(item => item.Id == modelId, cancellationToken)
            ?? throw new KeyNotFoundException("模型不存在。");
        var apiMode = EffectiveApiMode(model);
        var uri = ResolveModelRequestUri(model);
        var headers = EffectiveHeaders(model);
        logger?.LogInformation("AI 助手读取模型接口 {ProviderId}/{ModelId} {Protocol} {Path}", model.Provider.BusinessId, model.ModelId, apiMode, uri.AbsolutePath);
        return new JsonObject
        {
            ["id"] = model.Id.ToString(), ["model"] = model.ModelId, ["provider"] = model.Provider.BusinessId,
            ["api_mode"] = apiMode, ["endpoint_format"] = model.Provider.EndpointFormat,
            ["http_method"] = "POST", ["scheme"] = uri.Scheme, ["host"] = uri.Host, ["port"] = uri.Port,
            ["request_path"] = uri.AbsolutePath, ["use_proxy"] = model.Provider.UseProxy,
            ["api_key_configured"] = (!string.IsNullOrWhiteSpace(model.ProtectedApiKey) || !string.IsNullOrWhiteSpace(model.Provider.ProtectedApiKey)),
            ["header_names"] = new JsonArray(headers.Keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
        };
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
        result["models"] = ReadModelIds(probe.Body);

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

    public async Task<JsonObject> GetProviderModelsAsync(string id, bool synchronize, CancellationToken cancellationToken)
    {
        var provider = FindProvider(await configuration.ListProvidersAsync(cancellationToken), id) ?? throw new KeyNotFoundException("Provider 不存在。");
        var endpoint = ProviderModelCatalog.BuildEndpoint(provider.BaseUrl, provider.ModelListUrl);
        var result = new JsonObject { ["provider"] = provider.BusinessId, ["host"] = endpoint.Host, ["synced"] = false, ["models"] = new JsonArray() };
        logger?.LogInformation("AI 助手开始获取模型列表 {ProviderId} {Host} {Synchronize}", provider.BusinessId, endpoint.Host, synchronize);
        var startedAt = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultProbeTimeout);
        try
        {
            var catalog = await ProviderModelCatalog.FetchAsync(httpClient, endpoint, provider.ApiKey, ParseHeaders(provider.HeadersJson), timeout.Token);
            result["status_code"] = catalog.StatusCode;
            result["reachable"] = true;
            result["http_success"] = catalog.StatusCode is >= 200 and < 300;
            result["diagnosis"] = catalog.StatusCode is < 200 or >= 300 ? "request_rejected"
                : !catalog.Parsed ? "invalid_model_list" : catalog.Models.Count == 0 ? "empty_model_list" : "models_ok";
            result["models"] = new JsonArray(catalog.Models.Select(model => (JsonNode?)new JsonObject
            {
                ["id"] = model.ModelId, ["owned_by"] = model.OwnedBy, ["family"] = model.Family,
                ["context_length"] = model.ContextLength, ["max_tokens"] = model.MaxTokens, ["vision"] = model.Vision,
            }).ToArray());
            if (synchronize && catalog.StatusCode is >= 200 and < 300 && catalog.Parsed && catalog.Models.Count > 0)
            {
                // 读取最新配置；只更新远端元数据，不删除现有模型或清除本地覆盖和凭据。
                provider = FindProvider(await configuration.ListProvidersAsync(cancellationToken), provider.Id.ToString())!;
                var existing = provider.Models.ToDictionary(model => model.ModelId, StringComparer.OrdinalIgnoreCase);
                var added = 0;
                var updated = 0;
                foreach (var descriptor in catalog.Models)
                {
                    if (existing.TryGetValue(descriptor.ModelId, out var current))
                    {
                        var input = new ModelInput(current.ModelId, current.DisplayName, current.ConfigId, current.Family, current.BaseUrl, current.ApiMode,
                            current.ContextLength, current.MaxTokens, current.Vision, current.Temperature, current.TopP, current.Enabled, null, false,
                            ParseHeaders(current.HeadersJson).ToDictionary(item => item.Key, item => item.Value), JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.ExtraJson),
                            current.OwnedBy, current.RemoteFamily, current.RemoteContextLength, current.RemoteMaxTokens, current.RemoteVision, current.SortOrder);
                        await configuration.UpdateModelAsync(current.Id, ProviderModelCatalog.ApplyMetadata(input, descriptor), cancellationToken);
                        updated++;
                    }
                    else
                    {
                        await configuration.CreateModelAsync(provider.Id, ProviderModelCatalog.CreateInput(provider.ApiMode, descriptor), cancellationToken);
                        added++;
                    }
                }
                result["synced"] = true; result["added"] = added; result["updated"] = updated;
            }
            logger?.LogInformation("AI 助手模型列表获取完成 {ProviderId} {StatusCode} {ModelCount} {Synchronize}", provider.BusinessId, catalog.StatusCode, catalog.Models.Count, synchronize);
            if (catalog.StatusCode is < 200 or >= 300 || !catalog.Parsed || catalog.Models.Count == 0)
                logger?.LogWarning("AI 助手模型列表未通过验证 {ProviderId} {StatusCode} {Parsed} {ModelCount}", provider.BusinessId, catalog.StatusCode, catalog.Parsed, catalog.Models.Count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result["diagnosis"] = "timeout"; result["reachable"] = false;
            logger?.LogWarning("AI 助手模型列表获取超时 {ProviderId}", provider.BusinessId);
        }
        catch (HttpRequestException exception)
        {
            result["diagnosis"] = "unreachable"; result["reachable"] = false;
            // 异常消息可能含完整 URL，只记录安全类型摘要。
            logger?.LogWarning(new HttpRequestException("模型列表网络请求失败。", null, exception.StatusCode), "AI 助手模型列表网络失败 {ProviderId} {ExceptionType}", provider.BusinessId, exception.GetType().Name);
        }
        result["latency_ms"] = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        return result;
    }

    public async Task<JsonObject> TestModelAsync(Guid modelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.Models.AsNoTracking().Include(item => item.Provider).SingleOrDefaultAsync(item => item.Id == modelId, cancellationToken)
            ?? throw new KeyNotFoundException("模型不存在。");
        var provider = model.Provider;
        var baseUrl = (model.BaseUrl ?? provider.BaseUrl).TrimEnd('/');
        var apiMode = EffectiveApiMode(model);
        var apiKey = ReadApiKey(model.ProtectedApiKey) ?? ReadApiKey(provider.ProtectedApiKey);
        var headers = EffectiveHeaders(model);
        var result = new JsonObject { ["model"] = model.ModelId, ["provider"] = provider.BusinessId,
            ["api_mode"] = apiMode, ["endpoint_format"] = provider.EndpointFormat,
            ["request_path"] = ResolveModelRequestUri(model).AbsolutePath };
        logger?.LogInformation("AI 助手开始测试模型 {ProviderId}/{ModelId} {Protocol} {Path}", provider.BusinessId, model.ModelId, apiMode, result["request_path"]!.GetValue<string>());
        if (apiMode == "ollama")
        {
            var probe = await ProbeAsync(() => BuildOllamaChatRequest(baseUrl, model.ModelId, apiKey, headers), cancellationToken);
            result["chat_test"] = probe is { Reachable: true, StatusCode: >= 200 and < 300 };
            result["reachable"] = probe.Reachable;
            result["status_code"] = probe.StatusCode;
            result["latency_ms"] = probe.LatencyMs;
            result["diagnosis"] = ModelDiagnosis(probe.StatusCode, result["chat_test"]!.GetValue<bool>(), probe.Failure);
        }
        else
        {
            var tested = await modelTestService.ExecuteAsync(new ProviderTestRequest(Guid.NewGuid().ToString("N"),
                provider.BusinessId, model.ModelId, baseUrl, apiMode, provider.EndpointFormat, apiKey, headers,
                provider.UseProxy, "ping", ProviderTestMode.Regular, 0), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            result["chat_test"] = tested.IsSuccess;
            result["reachable"] = tested.StatusCode is not null;
            result["status_code"] = tested.StatusCode;
            result["latency_ms"] = tested.ElapsedMs;
            result["use_proxy"] = tested.Summary.UseProxy;
            result["diagnosis"] = ModelDiagnosis(tested.StatusCode, tested.IsSuccess, tested.ErrorCode);
        }
        if (result["chat_test"]!.GetValue<bool>())
            logger?.LogInformation("AI 助手测试模型完成 {ProviderId}/{ModelId} {Diagnosis} {StatusCode}", provider.BusinessId, model.ModelId, result["diagnosis"]!.GetValue<string>(), result["status_code"]?.GetValue<int>());
        else
            logger?.LogWarning("AI 助手模型测试未通过 {ProviderId}/{ModelId} {Diagnosis} {StatusCode}", provider.BusinessId, model.ModelId, result["diagnosis"]!.GetValue<string>(), result["status_code"]?.GetValue<int>());
        return result;
    }

    private static string ModelDiagnosis(int? statusCode, bool success, string? failure) => success ? "model_ok" : statusCode switch
    {
        401 or 403 => "auth_failed", 404 => "model_not_found", 429 => "rate_limited", >= 500 => "upstream_error",
        >= 200 and < 300 => failure is "invalid_json" or "invalid_response" ? failure : "request_rejected",
        null => failure == "timeout" ? "timeout" : "unreachable", _ => "request_rejected",
    };

    private static string EffectiveApiMode(ModelEntity model) =>
        (model.ApiMode ?? model.Provider.ApiMode).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];

    private static IReadOnlyDictionary<string, string> EffectiveHeaders(ModelEntity model)
    {
        var headers = new Dictionary<string, string>(ParseHeaders(model.Provider.HeadersJson), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ParseHeaders(model.HeadersJson)) headers[pair.Key] = pair.Value;
        return headers;
    }

    private static Uri ResolveModelRequestUri(ModelEntity model)
    {
        var baseUrl = model.BaseUrl ?? model.Provider.BaseUrl;
        var apiMode = EffectiveApiMode(model);
        return apiMode == "ollama"
            ? ProviderRouteEndpointResolver.Resolve(baseUrl, "/api/chat")
            : new Uri(ProviderTestService.ResolveEndpoint(baseUrl, apiMode, model.Provider.EndpointFormat));
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
            ["api_key_configured"] = hasApiKey,
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

    private static JsonArray ReadModelIds(byte[] body)
    {
        var result = new JsonArray();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) ? data
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var models) ? models : default;
            if (items.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if ((item.TryGetProperty("id", out var id) || item.TryGetProperty("name", out id)) && id.ValueKind == JsonValueKind.String)
                    result.Add(id.GetString());
            }
        }
        catch (JsonException) { }
        return result;
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
