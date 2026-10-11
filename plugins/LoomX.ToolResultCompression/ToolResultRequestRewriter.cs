using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.ToolResultCompression;

/// <summary>一次请求正文重写的安全摘要：只含条数、字符数与估算 token，不含工具结果内容。</summary>
public sealed record ToolResultRewriteReport(
    int TotalToolResults,
    int CompressedToolResults,
    int OriginalChars,
    int CompressedChars,
    IReadOnlyList<string> Strategies,
    int EstimatedOriginalTokens = 0,
    int EstimatedCompressedTokens = 0)
{
    public int SavedChars => Math.Max(0, OriginalChars - CompressedChars);

    public int SavedTokens => Math.Max(0, EstimatedOriginalTokens - EstimatedCompressedTokens);
}

/// <summary>
/// 请求正文重写器：定位各协议下的工具结果文本槽位并压缩。
/// 只处理工具结果，不触碰 system / user / assistant 内容；末尾若干条结果保留原文。
/// </summary>
public static partial class ToolResultRequestRewriter
{
    /// <summary>
    /// 请求正文重新序列化选项：只改工具结果内容，不缩进，且不把非 ASCII 字符转义为 \uXXXX，
    /// 避免重新序列化让中文内容膨胀而抵消压缩收益。
    /// </summary>
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static bool TryRewrite(
        string payload,
        string? apiMode,
        ToolResultCompressionOptions options,
        out string rewritten,
        out ToolResultRewriteReport report)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);

        rewritten = payload;
        report = new ToolResultRewriteReport(0, 0, 0, 0, []);

        if (string.IsNullOrWhiteSpace(payload) || !options.Enabled) return false;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is not JsonObject request) return false;

        var slots = new List<(JsonObject Owner, string Key)>();
        CollectSlots(request, apiMode, slots);
        if (slots.Count == 0) return false;

        var settings = options.Normalized();
        // 保真窗口：末尾若干条工具结果最可能被模型继续引用，原样保留。
        var compressible = Math.Max(0, slots.Count - settings.KeepRecentToolResults);

        var originalChars = 0;
        var compressedChars = 0;
        var originalTokens = 0;
        var compressedTokens = 0;
        var compressed = 0;
        var changed = false;
        var strategies = new List<string>();

        for (var index = 0; index < compressible; index++)
        {
            var (owner, key) = slots[index];
            var text = ReadString(owner, key);
            if (text is null) continue;

            var result = ToolResultCompressor.Compress(text, settings);
            originalChars += text.Length;
            compressedChars += result.Report.CompressedChars;
            originalTokens += CompressionTokenEstimator.Estimate(text);
            compressedTokens += CompressionTokenEstimator.Estimate(result.Text);
            if (!result.Changed) continue;

            owner[key] = result.Text;
            compressed++;
            changed = true;
            foreach (var strategy in result.Report.Strategies)
                if (!strategies.Contains(strategy))
                    strategies.Add(strategy);
        }

        if (!changed) return false;

        rewritten = request.ToJsonString(RequestJsonOptions);
        report = new ToolResultRewriteReport(
            slots.Count,
            compressed,
            originalChars,
            compressedChars,
            strategies,
            originalTokens,
            compressedTokens);
        return true;
    }
}
