using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using LoomX.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// 助手模型的安全摘要（可入日志/事件）。
/// </summary>
public sealed record AssistantModelInfo(string ProviderBusinessId, string ModelId, string BaseUrl);

/// <summary>可选助手模型的安全摘要（按 Provider 分组，只走 Provider 模型，不走 combo）。</summary>
public sealed record AssistantModelOption(string ProviderBusinessId, string ModelId, string DisplayName);

public sealed record AssistantProviderModelGroup(
    string ProviderBusinessId,
    string ProviderDisplayName,
    IReadOnlyList<AssistantModelOption> Models);

/// <summary>
/// 助手模型客户端工厂：从 LoomX 现有配置中挑选可用模型并构建 IModelClient。
/// Key 在服务端内部解析（DPAPI），只进请求头，绝不外泄。
/// 选择规则：优先用户指定（偏好中的 ProviderBusinessId + ModelId，仍需启用且 openai 兼容）；
/// 首次没有偏好时仅将可用列表首个模型作为初始选择并持久化；已指定模型失效时不自动回落；
/// 跳过 anthropic/ollama 原生模式（助手需要 tool calling，走 OpenAI 兼容协议）。
/// 请求遵循 Provider 配置：use_proxy 时按全局代理设置走代理，Provider/模型自定义头（含 UA）注入请求。
/// </summary>
public class AssistantModelClientFactory
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ConfigurationManagementService configuration;
    private readonly IDbContextFactory<ConfigurationDbContext>? dbContextFactory;
    private readonly AssistantPreferencesStore? preferencesStore;
    private readonly ILogger<OpenAiCompatibleModelClient> clientLogger;
    private readonly ILogger<AssistantModelClientFactory> logger;
    private readonly IProviderExecutionPipeline executionPipeline;

    private readonly object proxyClientSync = new();
    private HttpClient? proxyClient;
    private string? proxyClientSignature;

    public AssistantModelClientFactory(
        IHttpClientFactory httpClientFactory,
        ConfigurationManagementService configuration,
        ILogger<OpenAiCompatibleModelClient> clientLogger,
        ILogger<AssistantModelClientFactory> logger,
        IDbContextFactory<ConfigurationDbContext>? dbContextFactory = null,
        AssistantPreferencesStore? preferencesStore = null,
        IProviderExecutionPipeline? executionPipeline = null)
    {
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.clientLogger = clientLogger;
        this.logger = logger;
        this.executionPipeline = executionPipeline ?? new ProviderExecutionPipeline();
        this.dbContextFactory = dbContextFactory;
        this.preferencesStore = preferencesStore;
    }

    /// <summary>当前可用助手模型的安全摘要；无可用模型时为 null。</summary>
    public virtual async Task<AssistantModelInfo?> DescribeAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectModelAsync(cancellationToken);
        return selection is null
            ? null
            : new AssistantModelInfo(selection.Provider.BusinessId, selection.Model.ModelId, selection.ModelBaseUrl);
    }

    /// <summary>当前选定的模型标识；首次使用前可能尚未持久化。</summary>
    public virtual (string? ProviderBusinessId, string? ModelId) GetPreferredSelection()
    {
        var preferences = preferencesStore?.Load();
        return (preferences?.ProviderBusinessId, preferences?.ModelId);
    }

    /// <summary>列出可选助手模型（启用 Provider 下启用且 openai 兼容的模型，按 Provider 分组）。</summary>
    public virtual async Task<IReadOnlyList<AssistantProviderModelGroup>> ListAvailableAsync(CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        var groups = new List<AssistantProviderModelGroup>();
        foreach (var provider in providers.Where(item => item.Enabled))
        {
            var models = provider.Models
                .Where(item => item.Enabled && SupportsOpenAi(item.ApiMode ?? provider.ApiMode))
                .Select(item => new AssistantModelOption(provider.BusinessId, item.ModelId, item.DisplayName))
                .ToArray();
            if (models.Length > 0)
            {
                groups.Add(new AssistantProviderModelGroup(provider.BusinessId, provider.DisplayName, models));
            }
        }

        return groups;
    }

    /// <summary>记住用户选定的助手模型（持久化到偏好）。</summary>
    public virtual void SetPreferredSelection(string providerBusinessId, string modelId)
    {
        preferencesStore?.Update(current => current with
        {
            ProviderBusinessId = providerBusinessId,
            ModelId = modelId,
        });
        logger.LogInformation("AI 助手模型已指定 {ProviderId}/{ModelId}", providerBusinessId, modelId);
    }

    /// <summary>清除指定模型偏好；下一次创建客户端时会重新使用可用列表首项作为初始值。</summary>
    public virtual void ClearPreferredSelection()
    {
        preferencesStore?.Update(current => current with
        {
            ProviderBusinessId = null,
            ModelId = null,
        });
        logger.LogInformation("AI 助手模型偏好已清除");
    }

    /// <summary>构建助手模型客户端；无可用模型时返回 null（调用方应给出明确错误而非假装可用）。</summary>
    public virtual async Task<IModelClient?> TryCreateAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectModelAsync(cancellationToken);
        if (selection is null)
        {
            logger.LogWarning("AI 助手无可用模型（需要启用中的 openai 兼容 Provider 与模型）");
            return null;
        }

        logger.LogInformation(
            "AI 助手模型已选定 {ProviderId}/{ModelId} 走代理 {UseProxy}",
            selection.Provider.BusinessId,
            selection.Model.ModelId,
            selection.Provider.UseProxy);

        var httpClient = await ResolveHttpClientAsync(selection.Provider.UseProxy, cancellationToken);
        return new OpenAiCompatibleModelClient(
            httpClient,
            selection.ModelBaseUrl,
            selection.Model.ModelId,
            selection.ApiKey,
            clientLogger,
            MergeHeaders(selection.Provider, selection.Model),
            ResolveReasoningEffort(),
            selection.Provider.EndpointFormat,
            executionPipeline,
            selection.Provider.BusinessId);
    }

    private async Task<ModelSelection?> SelectModelAsync(CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        var candidates = providers.Where(item => item.Enabled).ToArray();

        // 用户指定的模型优先；失效（删除/禁用/不再 openai 兼容）时明确失败，不回落到其他模型。
        var preferences = preferencesStore?.Load();
        if (preferences?.ProviderBusinessId is { Length: > 0 } preferredProvider
            && preferences.ModelId is { Length: > 0 } preferredModel)
        {
            var provider = candidates.FirstOrDefault(item =>
                string.Equals(item.BusinessId, preferredProvider, StringComparison.OrdinalIgnoreCase));
            var model = provider?.Models.FirstOrDefault(item =>
                item.Enabled
                && string.Equals(item.ModelId, preferredModel, StringComparison.OrdinalIgnoreCase)
                && SupportsOpenAi(item.ApiMode ?? provider.ApiMode));
            if (provider is not null && model is not null)
            {
                return CreateSelection(provider, model);
            }

            logger.LogWarning("AI 助手指定模型失效，不自动切换 {ProviderId}/{ModelId}", preferredProvider, preferredModel);
            return null;
        }

        foreach (var provider in candidates)
        {
            var model = provider.Models.FirstOrDefault(item =>
                item.Enabled && SupportsOpenAi(item.ApiMode ?? provider.ApiMode));
            if (model is null) continue;
            var initialSelection = CreateSelection(provider, model);
            preferencesStore?.Update(current => current with
            {
                ProviderBusinessId = provider.BusinessId,
                ModelId = model.ModelId,
            });
            logger.LogInformation("AI 助手首次使用默认模型 {ProviderId}/{ModelId}", provider.BusinessId, model.ModelId);
            return initialSelection;
        }

        return null;
    }

    private static ModelSelection CreateSelection(ProviderResponse provider, ModelResponse model)
    {
        // 模型级覆盖优先，回落到 Provider 级
        var baseUrl = (model.BaseUrl ?? provider.BaseUrl).TrimEnd('/');
        var apiKey = !string.IsNullOrWhiteSpace(provider.ApiKey) ? provider.ApiKey : null;
        return new ModelSelection(provider, model, baseUrl, apiKey);
    }

    /// <summary>合并 Provider 与模型级自定义头（模型级覆盖同名 Provider 头，与网关解析规则一致）。</summary>
    private static IReadOnlyDictionary<string, string> MergeHeaders(ProviderResponse provider, ModelResponse model)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ParseHeaders(provider.HeadersJson)) headers[pair.Key] = pair.Value;
        foreach (var pair in ParseHeaders(model.HeadersJson)) headers[pair.Key] = pair.Value;
        return headers;
    }

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

    /// <summary>偏好中的思考等级；default 表示不下发 reasoning_effort 字段。</summary>
    private string? ResolveReasoningEffort()
    {
        var value = AssistantPreferences.NormalizeReasoningEffort(preferencesStore?.Load().ReasoningEffort);
        return value == AssistantPreferences.DefaultReasoningEffort ? null : value;
    }

    /// <summary>
    /// Provider 勾选走代理时，按全局代理设置构建带代理的 HttpClient（缓存复用，设置变化时重建）。
    /// 未勾选时复用默认命名客户端。代理密码只在内存中解密，不入日志。
    /// </summary>
    private async Task<HttpClient> ResolveHttpClientAsync(bool useProxy, CancellationToken cancellationToken)
    {
        if (!useProxy || dbContextFactory is null)
        {
            return httpClientFactory.CreateClient("loomx-assistant");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        if (string.Equals(settings.ProxyMode, "direct", StringComparison.OrdinalIgnoreCase))
        {
            return httpClientFactory.CreateClient("loomx-assistant");
        }

        string? password = null;
        if (!string.IsNullOrWhiteSpace(settings.ProtectedProxyPassword)
            && ProtectedApiKeyStore.TryUnprotect(settings.ProtectedProxyPassword, out var plainText))
        {
            password = plainText;
        }

        var signature = string.Join('|',
            settings.ProxyMode,
            settings.ProxyHost,
            settings.ProxyPort,
            settings.ProxyUsername ?? string.Empty,
            password is null ? "0" : "1");
        lock (proxyClientSync)
        {
            if (proxyClient is not null && proxyClientSignature == signature) return proxyClient;
        }

        var handler = new HttpClientHandler { UseProxy = true };
        if (string.Equals(settings.ProxyMode, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(settings.ProxyHost?.Trim(), UriKind.Absolute, out var proxyUri)
                || proxyUri.Scheme is not ("http" or "https")
                || settings.ProxyPort is < 1 or > 65535)
            {
                logger.LogWarning("AI 助手自定义代理配置无效，本次直连");
                return httpClientFactory.CreateClient("loomx-assistant");
            }

            var proxy = new WebProxy($"{proxyUri.Scheme}://{proxyUri.Host}:{settings.ProxyPort}");
            if (!string.IsNullOrWhiteSpace(settings.ProxyUsername) || !string.IsNullOrWhiteSpace(password))
            {
                proxy.Credentials = new NetworkCredential(settings.ProxyUsername ?? string.Empty, password ?? string.Empty);
            }

            handler.Proxy = proxy;
        }
        // system 模式：UseProxy = true 且不显式设置 Proxy，走系统默认代理

        var client = new HttpClient(handler);
        lock (proxyClientSync)
        {
            proxyClient?.Dispose();
            proxyClient = client;
            proxyClientSignature = signature;
        }

        return client;
    }

    private static bool SupportsOpenAi(string apiMode) =>
        apiMode.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("openai", StringComparer.OrdinalIgnoreCase);

    private sealed record ModelSelection(ProviderResponse Provider, ModelResponse Model, string ModelBaseUrl, string? ApiKey);
}
