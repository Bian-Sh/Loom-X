using System.Text.Json.Nodes;

namespace LoomX.Assistant;

/// <summary>
/// Secret 边界：API Key 只允许以 secret_ref 形式进入模型上下文。
/// 模型可见的永远只有 {"configured": true, "secret_ref": "secret://..."}，
/// Secret 明文不得进入 Tool Result、AgentEvent、日志与异常消息。
/// </summary>
public static class SecretBoundary
{
    public static string ProviderApiKeyRef(string businessId) => $"secret://provider/{businessId}/apikey";

    public static string ModelApiKeyRef(Guid modelId) => $"secret://model/{modelId}/apikey";

    public static string EndpointApiKeyRef(string endpointKey) => $"secret://endpoint/{endpointKey}/apikey";

    /// <summary>
    /// 生成模型可见的 Secret 描述。未配置时 secret_ref 为 null。
    /// </summary>
    public static JsonObject Describe(bool configured, string secretRef) => new()
    {
        ["configured"] = configured,
        ["secret_ref"] = configured ? secretRef : null,
    };
}
