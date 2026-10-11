using System.Text.Json.Nodes;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 各 Provider 协议下「工具结果文本槽位」的定位。槽位只记录 (宿主对象, 键)，
/// 不复制内容，压缩阶段按同一个引用原地改写。
/// </summary>
public static partial class ToolResultRequestRewriter
{
    private const string SlotKeyContent = "content";

    private static void CollectSlots(JsonObject request, string? apiMode, List<(JsonObject Owner, string Key)> slots)
    {
        var mode = apiMode?.Trim().ToLowerInvariant();
        switch (mode)
        {
            case "anthropic":
                CollectChatSlots(request, slots);
                return;
            case "gemini":
                CollectGeminiSlots(request, slots);
                return;
            case "responses":
                CollectResponsesSlots(request, slots);
                return;
            default:
                // 未知协议：三种形态都尝试，未命中的形态不会产生槽位。
                CollectChatSlots(request, slots);
                CollectResponsesSlots(request, slots);
                CollectGeminiSlots(request, slots);
                return;
        }
    }

    /// <summary>OpenAI Chat Completions（role: tool/function）与 Anthropic（content[].tool_result）。</summary>
    private static void CollectChatSlots(JsonObject request, List<(JsonObject Owner, string Key)> slots)
    {
        if (request["messages"] is not JsonArray messages) return;

        foreach (var item in messages)
        {
            if (item is not JsonObject message) continue;

            var role = ReadString(message, "role");
            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "function", StringComparison.OrdinalIgnoreCase))
            {
                AddStringSlot(slots, message, SlotKeyContent);
                AddTextPartSlots(slots, message[SlotKeyContent] as JsonArray);
                continue;
            }

            if (message[SlotKeyContent] is not JsonArray blocks) continue;
            foreach (var block in blocks)
            {
                if (block is not JsonObject blockObject) continue;
                if (!string.Equals(ReadString(blockObject, "type"), "tool_result", StringComparison.OrdinalIgnoreCase))
                    continue;

                AddStringSlot(slots, blockObject, SlotKeyContent);
                AddTextPartSlots(slots, blockObject[SlotKeyContent] as JsonArray);
            }
        }
    }

    /// <summary>OpenAI Responses API：input[] 中的 function_call_output。</summary>
    private static void CollectResponsesSlots(JsonObject request, List<(JsonObject Owner, string Key)> slots)
    {
        if (request["input"] is not JsonArray inputs) return;

        foreach (var item in inputs)
        {
            if (item is not JsonObject entry) continue;
            if (!string.Equals(ReadString(entry, "type"), "function_call_output", StringComparison.OrdinalIgnoreCase))
                continue;

            AddStringSlot(slots, entry, "output");
            AddTextPartSlots(slots, entry["output"] as JsonArray);
        }
    }

    /// <summary>Gemini：contents[].parts[].functionResponse。</summary>
    private static void CollectGeminiSlots(JsonObject request, List<(JsonObject Owner, string Key)> slots)
    {
        if (request["contents"] is not JsonArray contents) return;

        foreach (var item in contents)
        {
            if (item is not JsonObject content) continue;
            if (content["parts"] is not JsonArray parts) continue;

            foreach (var part in parts)
            {
                if (part is not JsonObject partObject) continue;
                if (partObject["functionResponse"] is not JsonObject response) continue;

                // Gemini 规范是 functionResponse: { name, response }；部分客户端直接内联结果对象。
                var result = response["response"] as JsonObject ?? response;
                if (!AddStringSlot(slots, result, SlotKeyContent)
                    && !AddStringSlot(slots, result, "output")
                    && !AddStringSlot(slots, result, "result"))
                {
                    AddTextPartSlots(slots, result[SlotKeyContent] as JsonArray);
                }
            }
        }
    }

    private static bool AddStringSlot(List<(JsonObject Owner, string Key)> slots, JsonObject owner, string key)
    {
        if (ReadString(owner, key) is null) return false;
        slots.Add((owner, key));
        return true;
    }

    private static void AddTextPartSlots(List<(JsonObject Owner, string Key)> slots, JsonArray? parts)
    {
        if (parts is null) return;

        foreach (var part in parts)
        {
            if (part is not JsonObject partObject) continue;
            var type = ReadString(partObject, "type");
            if (type is not null
                && type is not ("text" or "input_text" or "output_text" or "tool_result"))
            {
                continue;
            }

            AddStringSlot(slots, partObject, "text");
        }
    }

    private static string? ReadString(JsonObject owner, string key) =>
        owner.TryGetPropertyValue(key, out var node)
        && node is JsonValue value
        && value.TryGetValue<string>(out var text)
            ? text
            : null;
}
