using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.ToolResultCompression;

/// <summary>
/// JSON 结构化折叠：工具结果常是大段 JSON（文件内容、接口响应、检索结果），
/// 按字符截断会把 JSON 截成无法解析的半截结构，因此改为保留结构的折叠。
/// </summary>
public static partial class ToolResultCompressor
{
    private const int JsonMaxProperties = 40;

    /// <summary>
    /// 折叠后的 JSON 输出选项：紧凑、且不把非 ASCII 字符转义为 \uXXXX，
    /// 避免重新序列化反而让中文内容膨胀。
    /// </summary>
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static bool TryFoldJson(string text, ToolResultCompressionOptions options, out string folded)
    {
        folded = text;
        var trimmed = text.Trim();
        if (trimmed.Length is 0 || trimmed[0] is not ('{' or '[')) return false;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(trimmed);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is null) return false;

        // 渐进收紧：折叠结果仍超过单行上限时减半数组项数与字符串长度，
        // 避免后续长行截断把 JSON 截成无法解析的半截结构。
        var items = options.JsonMaxItems;
        var stringLimit = options.JsonMaxStringLength;
        var json = text;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var attemptOptions = options with { JsonMaxItems = items, JsonMaxStringLength = stringLimit };
            json = FoldNode(root, attemptOptions, 0).ToJsonString(CompactJsonOptions);
            if (json.Length <= options.MaxLineLength) break;
            if (items <= 1 && stringLimit <= 40) break;
            items = Math.Max(1, items / 2);
            stringLimit = Math.Max(40, stringLimit / 2);
        }

        if (json.Length >= text.Length) return false;

        folded = json;
        return true;
    }

    /// <summary>结构化折叠 JSON：数组截断、长字符串截断、超深层级折叠，输出保持合法 JSON。</summary>
    private static JsonNode FoldNode(JsonNode node, ToolResultCompressionOptions options, int depth)
    {
        if (depth > options.JsonMaxDepth)
            return JsonValue.Create($"<已折叠：深度超过 {options.JsonMaxDepth} 层>");

        switch (node)
        {
            case JsonObject jsonObject:
                return FoldObject(jsonObject, options, depth);

            case JsonArray array:
                return FoldArray(array, options, depth);

            case JsonValue value:
            {
                if (value.TryGetValue<string>(out var text) && text.Length > options.JsonMaxStringLength)
                {
                    return JsonValue.Create(
                        SafeHead(text, options.JsonMaxStringLength)
                        + $" …(+{text.Length - options.JsonMaxStringLength} 字符)");
                }

                return value.DeepClone();
            }

            default:
                return node.DeepClone();
        }
    }

    private static JsonObject FoldObject(JsonObject source, ToolResultCompressionOptions options, int depth)
    {
        var result = new JsonObject();
        var index = 0;
        var omitted = 0;
        foreach (var property in source)
        {
            if (index++ >= JsonMaxProperties)
            {
                omitted++;
                continue;
            }

            result[property.Key] = property.Value is null
                ? null
                : FoldNode(property.Value, options, depth + 1);
        }

        if (omitted > 0) result["__omitted_fields__"] = omitted;
        return result;
    }

    private static JsonArray FoldArray(JsonArray source, ToolResultCompressionOptions options, int depth)
    {
        var result = new JsonArray();
        var limit = Math.Min(source.Count, options.JsonMaxItems);
        for (var index = 0; index < limit; index++)
        {
            var item = source[index];
            result.Add(item is null ? null : FoldNode(item, options, depth + 1));
        }

        if (source.Count > limit)
            result.Add($"… 其余 {source.Count - limit} 项已省略");
        return result;
    }
}
