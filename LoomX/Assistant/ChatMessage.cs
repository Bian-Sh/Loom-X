namespace LoomX.Assistant;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>
/// 一次模型工具调用。ArgumentsJson 为模型输出的原始 JSON 参数。
/// </summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// 会话消息。禁止写入 Secret 值，只能出现 secret_ref 形式的安全引用。
/// </summary>
public sealed record ChatMessage(ChatRole Role, string? Content)
{
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }

    public static ChatMessage System(string content) => new(ChatRole.System, content);

    public static ChatMessage User(string content) => new(ChatRole.User, content);

    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);

    public static ChatMessage AssistantToolCalls(IReadOnlyList<ToolCall> toolCalls, string? content = null) =>
        new(ChatRole.Assistant, content) { ToolCalls = toolCalls };

    public static ChatMessage ToolResult(ToolCall call, string content) =>
        new(ChatRole.Tool, content) { ToolCallId = call.Id, ToolName = call.Name };
}
