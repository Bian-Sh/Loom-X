using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.Assistant;

public static class ToolArgumentSafety
{
    private const string HiddenArgumentsJson = "{\"summary\":\"参数已隐藏\"}";

    public static ToolCall Project(ToolCall rawCall, ToolDefinition? tool)
    {
        ArgumentNullException.ThrowIfNull(rawCall);
        if (tool?.SafeArgumentsProjector is null)
        {
            return Hide(rawCall);
        }

        try
        {
            var rawArguments = string.IsNullOrWhiteSpace(rawCall.ArgumentsJson)
                ? null
                : JsonNode.Parse(rawCall.ArgumentsJson);
            var projection = tool.SafeArgumentsProjector(rawArguments);
            return rawCall with
            {
                ArgumentsJson = projection?.ToJsonString() ?? "{}",
                ArgumentsAreSafe = true,
            };
        }
        catch (Exception)
        {
            return Hide(rawCall);
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

    private static ToolCall Hide(ToolCall call) => call with
    {
        ArgumentsJson = HiddenArgumentsJson,
        ArgumentsAreSafe = true,
    };
}
