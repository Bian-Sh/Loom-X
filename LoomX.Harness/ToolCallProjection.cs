using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.Assistant;

/// <summary>
/// 将原始工具调用投影为可进入历史、事件、UI 与批准流程的公开协议形态。
/// 该组件不负责凭据检测；凭据保护统一由 Credential Protection Pipeline 完成。
/// </summary>
public static class ToolCallProjection
{
    private const string HiddenArgumentsJson = "{\"summary\":\"参数已隐藏\"}";
    internal const string UnknownToolName = "unknown.tool";

    public static ToolCall Project(ToolCall rawCall, ToolDefinition? tool)
    {
        ArgumentNullException.ThrowIfNull(rawCall);
        if (tool is null)
        {
            return Hide(rawCall, UnknownToolName);
        }

        if (tool.SafeArgumentsProjector is null)
        {
            return Hide(rawCall, tool.Name);
        }

        try
        {
            var rawArguments = string.IsNullOrWhiteSpace(rawCall.ArgumentsJson)
                ? null
                : JsonNode.Parse(rawCall.ArgumentsJson);
            var projection = tool.SafeArgumentsProjector(rawArguments);
            return rawCall with
            {
                Name = tool.Name,
                ArgumentsJson = projection?.ToJsonString() ?? "{}",
                ArgumentsAreSafe = true,
            };
        }
        catch (Exception)
        {
            return Hide(rawCall, tool.Name);
        }
    }

    public static ToolCall EnsureSafe(ToolCall call) => call.ArgumentsAreSafe ? call : Hide(call);

    public static ChatMessage EnsureSafe(ChatMessage message)
    {
        var calls = message.ToolCalls.Select(EnsureSafe).ToArray();
        var blocks = message.Blocks.Select(block => block.ToolCall is null
            ? block
            : block with { ToolCall = EnsureSafe(block.ToolCall) }).ToArray();
        return message with { ToolCalls = calls, Blocks = blocks };
    }

    private static ToolCall Hide(ToolCall call, string? safeName = null) => call with
    {
        Name = safeName ?? call.Name,
        ArgumentsJson = HiddenArgumentsJson,
        ArgumentsAreSafe = true,
    };
}
