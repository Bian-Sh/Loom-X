namespace LoomX.Assistant;

/// <summary>
/// 一次模型请求：完整消息历史 + 可用工具列表。
/// </summary>
public sealed record ModelRequest(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyCollection<ToolDefinition> Tools);

/// <summary>
/// 模型流式输出事件。ToolCall 在参数完整累积后一次性发出。
/// </summary>
public abstract record ModelStreamEvent;

public sealed record TextDeltaEvent(string Text) : ModelStreamEvent;

public sealed record ModelToolCallEvent(ToolCall ToolCall) : ModelStreamEvent;

public sealed record ModelCompletedEvent(string FinishReason) : ModelStreamEvent;

/// <summary>
/// 模型客户端抽象。Phase 1 仅要求 streaming + tool calling。
/// </summary>
public interface IModelClient
{
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 模型调用失败。Message 只允许安全摘要，禁止包含 Secret 或请求/响应正文。
/// </summary>
public sealed class ModelClientException : Exception
{
    public ModelClientException(string message) : base(message)
    {
    }

    public ModelClientException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
