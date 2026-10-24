using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// 客户端接入目标描述：一个可由 LoomX 写入 Provider 配置的 AI Agent 客户端。
/// </summary>
public sealed record ClientTargetDescriptor(
    string TargetKey,
    string DisplayName,
    /// <summary>该客户端无法自行发现模型，写入时必须携带模型列表。</summary>
    bool RequiresModelList,
    /// <summary>写入后需要重启客户端才生效。</summary>
    bool RestartRequired,
    /// <summary>协议平台：codex / claude / gemini / grok。</summary>
    string Platform);

/// <summary>
/// 写入客户端配置的模型条目。只携带客户端可见的稳定身份与能力摘要，
/// Provider 路由、凭据与故障转移全部由 LoomX Router 内部编排。
/// </summary>
public sealed record ClientModelInfo(
    string Id,
    int ContextWindow,
    int MaxOutputTokens,
    IReadOnlyList<string> ReasoningLevels,
    bool SupportsImageInput);

/// <summary>客户端配置写入入参。ApiKey 是 LoomX Endpoint 的访问 Key，只落盘、不回显。</summary>
public sealed record ClientWriteInput(string BaseUrl, string ApiKey, IReadOnlyList<ClientModelInfo> Models);

/// <summary>配置文件状态：missing / unmanaged / managed / invalid。</summary>
public sealed record ClientConfigInspection(string FileStatus, bool Managed);

/// <summary>单次写入结果：RenderedContent 由 Service 统一落盘；BackupPath 由自带落盘逻辑的适配器（codex）回报。</summary>
public sealed record ClientAdapterWriteResult(
    string? RenderedContent,
    string? BackupPath,
    bool Changed,
    string? CatalogPath = null);

/// <summary>Service 层写入结果（面向工具输出，不含凭据）。</summary>
public sealed record ClientConfigWriteOutcome(
    string TargetKey,
    string ConfigPath,
    string? BackupPath,
    bool Changed,
    string? CatalogPath,
    bool RestartRequired,
    IReadOnlyList<string> ModelsWritten);

/// <summary>既有配置非法、拒绝覆写时抛出；调用方据此返回安全失败。</summary>
public sealed class ClientConfigException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public const string ExistingInvalidCode = "validation.route_config_existing_invalid";
    public const string GeneratedInvalidCode = "config.generated_invalid";
}

public static class ClientConfigContent
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    /// <summary>代理根地址：去尾斜杠。客户端自行追加版本段（Anthropic 命中 {base}/v1/messages）。</summary>
    public static string BaseUrlRoot(string baseUrl) =>
        baseUrl.Trim().TrimEnd('/');

    /// <summary>代理地址以 /v1 结尾：客户端直接拼接裸端点（{base}/chat/completions、{base}/responses）。幂等。</summary>
    public static string BaseUrlWithV1(string baseUrl)
    {
        var trimmed = BaseUrlRoot(baseUrl);
        if (trimmed.RsplitSegment() is { } segment && segment.Equals("v1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/v1";
    }

    /// <summary>解析既有 JSON 配置；空文件返回空对象，非法内容拒绝覆写而不是销毁用户配置。</summary>
    public static JsonObject ParseExistingObject(string path, string? existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return new JsonObject();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(existing);
        }
        catch (JsonException)
        {
            throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"既有 JSON 配置语法非法，拒绝覆写：{path}");
        }

        return node as JsonObject
            ?? throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"既有 JSON 配置根节点必须是对象，拒绝覆写：{path}");
    }

    public static JsonObject GetOrCreateObject(JsonObject parent, string key, string path)
    {
        if (parent[key] is not JsonObject child)
        {
            if (parent.ContainsKey(key))
            {
                throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"键 '{key}' 已存在且不是对象，拒绝覆写：{path}");
            }

            child = new JsonObject();
            parent[key] = child;
        }

        return child;
    }

    public static string ToPrettyJson(JsonObject root) =>
        root.ToJsonString(OutputJsonOptions);
}

public static class ClientConfigUrlExtensions
{
    /// <summary>取 URL 最后一个路径段，等价 ai-switch 的 base_last_path_segment。</summary>
    public static string? RsplitSegment(this string url)
    {
        var afterScheme = url.Split("://", 2) is { Length: 2 } parts ? parts[1] : url;
        var path = afterScheme.Contains('/') ? afterScheme[(afterScheme.IndexOf('/') + 1)..] : null;
        if (path is null)
        {
            return null;
        }

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[^1] : null;
    }
}
