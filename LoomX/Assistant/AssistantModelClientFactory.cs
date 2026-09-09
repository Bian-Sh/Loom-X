using System.Text.Json.Nodes;
using LoomX.Configuration;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// 助手模型的安全摘要（可入日志/事件）。
/// </summary>
public sealed record AssistantModelInfo(string ProviderBusinessId, string ModelId, string BaseUrl);

/// <summary>
/// 助手模型客户端工厂：从 LoomX 现有配置中挑选可用模型并构建 IModelClient。
/// Key 在服务端内部解析（DPAPI），只进请求头，绝不外泄。
/// 选择规则：第一个启用且 api_mode 含 openai 的 Provider 中第一个启用的模型；
/// 跳过 anthropic/ollama 原生模式（助手需要 tool calling，走 OpenAI 兼容协议）。
/// </summary>
public class AssistantModelClientFactory
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ConfigurationManagementService configuration;
    private readonly ILogger<OpenAiCompatibleModelClient> clientLogger;
    private readonly ILogger<AssistantModelClientFactory> logger;

    public AssistantModelClientFactory(
        IHttpClientFactory httpClientFactory,
        ConfigurationManagementService configuration,
        ILogger<OpenAiCompatibleModelClient> clientLogger,
        ILogger<AssistantModelClientFactory> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.clientLogger = clientLogger;
        this.logger = logger;
    }

    /// <summary>当前可用助手模型的安全摘要；无可用模型时为 null。</summary>
    public virtual async Task<AssistantModelInfo?> DescribeAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectModelAsync(cancellationToken);
        return selection is null
            ? null
            : new AssistantModelInfo(selection.Provider.BusinessId, selection.Model.ModelId, selection.ModelBaseUrl);
    }

    /// <summary>构建助手模型客户端；无可用模型时返回 null（调用方应给出明确错误而非假装可用）。</summary>
    public virtual async Task<IModelClient?> TryCreateAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectModelAsync(cancellationToken);
        if (selection is null)
        {
            logger.LogWarning("小助手无可用模型（需要启用中的 openai 兼容 Provider 与模型）");
            return null;
        }

        logger.LogInformation(
            "小助手模型已选定 {ProviderId}/{ModelId}",
            selection.Provider.BusinessId,
            selection.Model.ModelId);
        return new OpenAiCompatibleModelClient(
            httpClientFactory.CreateClient("loomx-assistant"),
            selection.ModelBaseUrl,
            selection.Model.ModelId,
            selection.ApiKey,
            clientLogger);
    }

    private async Task<ModelSelection?> SelectModelAsync(CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        foreach (var provider in providers.Where(item => item.Enabled))
        {
            var model = provider.Models.FirstOrDefault(item =>
                item.Enabled && SupportsOpenAi(item.ApiMode ?? provider.ApiMode));
            if (model is null) continue;

            // 模型级覆盖优先，回落到 Provider 级
            var baseUrl = (model.BaseUrl ?? provider.BaseUrl).TrimEnd('/');
            var apiKey = !string.IsNullOrWhiteSpace(provider.ApiKey) ? provider.ApiKey : null;
            return new ModelSelection(provider, model, baseUrl, apiKey);
        }

        return null;
    }

    private static bool SupportsOpenAi(string apiMode) =>
        apiMode.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("openai", StringComparer.OrdinalIgnoreCase);

    private sealed record ModelSelection(ProviderResponse Provider, ModelResponse Model, string ModelBaseUrl, string? ApiKey);
}
