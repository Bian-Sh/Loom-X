using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LoomX.Configuration;

namespace LoomX.Assistant.Browser;

/// <summary>
/// 浏览器侧 Secret 保险库：网页/网络请求中捕获的 API Key 在 Bridge 侧直接
/// DPAPI 加密入库，模型只能看到 secret_ref。进程内存保存，永不写日志。
/// 正确流向：Browser → API Key → Secret Store → secret_ref → LLM。
/// </summary>
public sealed class BrowserSecretVault
{
    private readonly ConcurrentDictionary<string, string> secrets = new(StringComparer.Ordinal);

    /// <summary>存入明文，返回 secret_ref。明文立即加密，不以明文驻留。</summary>
    public string Store(string plainText, string hint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);
        var reference = $"secret://browser/{Guid.NewGuid():N}/{SanitizeHint(hint)}";
        secrets[reference] = ProtectedApiKeyStore.Protect(plainText);
        return reference;
    }

    /// <summary>按 secret_ref 解析出明文（仅供服务端内部鉴权使用）。</summary>
    public bool TryResolve(string secretRef, out string plainText)
    {
        plainText = string.Empty;
        if (!secrets.TryGetValue(secretRef, out var protectedValue))
        {
            return false;
        }

        return ProtectedApiKeyStore.TryUnprotect(protectedValue, out plainText!);
    }

    public bool Contains(string secretRef) => secrets.ContainsKey(secretRef);

    public bool Remove(string secretRef) => secrets.TryRemove(secretRef, out _);

    private static string SanitizeHint(string hint)
    {
        var sanitized = Regex.Replace(hint, "[^a-zA-Z0-9-]", "-").Trim('-').ToLowerInvariant();
        return sanitized.Length == 0 ? "key" : sanitized[..Math.Min(sanitized.Length, 32)];
    }
}

/// <summary>
/// 浏览器结果 Secret 收割器：在 browser.read / browser.network 等结果离开 Bridge 前，
/// 把其中的 API Key（Authorization 头、api-key/token 字段、sk-/key- 形态的值）
/// 收割进 BrowserSecretVault 并替换为 secret_ref。模型永远看不到明文。
/// </summary>
public static partial class BrowserSecretHarvester
{
    public static JsonNode Harvest(JsonNode result, BrowserSecretVault vault)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(vault);
        var clone = result.DeepClone();
        HarvestNode(clone, vault, parentKey: null);
        return clone;
    }

    private static void HarvestNode(JsonNode node, BrowserSecretVault vault, string? parentKey)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var pair in jsonObject.ToArray())
                {
                    if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && LooksSecretKey(pair.Key) && LooksSecretValue(text))
                    {
                        var reference = vault.Store(ExtractSecret(text)!, pair.Key);
                        jsonObject[pair.Key] = SecretBoundary.Describe(true, reference);
                        continue;
                    }

                    if (pair.Value is not null) HarvestNode(pair.Value, vault, pair.Key);
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    if (item is not null) HarvestNode(item, vault, parentKey);
                }

                break;
        }
    }

    private static bool LooksSecretKey(string key) => SecretKeyPattern().IsMatch(key);

    private static bool LooksSecretValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // 过滤过短的占位值与明显非 Secret 的文本
        var trimmed = value.Trim();
        if (trimmed.Length < 8 || trimmed.Contains(' ') && !trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        return SecretValuePattern().IsMatch(trimmed);
    }

    /// <summary>从 "Bearer xxx" / 原始值中提取纯 Secret。</summary>
    public static string? ExtractSecret(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed["Bearer ".Length..].Trim();
        }

        return trimmed;
    }

    [GeneratedRegex("(?i)(authorization|api[-_ ]?key|access[-_ ]?token|x-api-key|auth[-_ ]?token)")]
    private static partial Regex SecretKeyPattern();

    [GeneratedRegex("(?i)^(bearer\\s+)?(sk|key|pk|tok|api|ey|xox|ghp|hf|sk-or|sk-ant)[-_][a-z0-9]{6,}|^bearer\\s+[a-z0-9][a-z0-9._-]{10,}$|^[a-z0-9][a-z0-9._-]{23,}$")]
    private static partial Regex SecretValuePattern();
}
