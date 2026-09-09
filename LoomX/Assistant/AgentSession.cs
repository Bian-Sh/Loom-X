namespace LoomX.Assistant;

public enum AgentSessionState
{
    Created,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record AgentSessionOptions
{
    /// <summary>
    /// 最大模型调用步数，防止 Agent 无限循环。
    /// </summary>
    public int MaxSteps { get; init; } = 8;

    /// <summary>
    /// 单步模型请求超时。
    /// </summary>
    public TimeSpan ModelTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public string? SystemPrompt { get; init; }
}

/// <summary>
/// 小助手会话：保存消息历史与生命周期状态。历史永不包含 Secret 值。
/// </summary>
public sealed class AgentSession
{
    private readonly List<ChatMessage> messages = [];

    public AgentSession(AgentSessionOptions? options = null)
    {
        Options = options ?? new AgentSessionOptions();
        if (Options.SystemPrompt is { Length: > 0 } systemPrompt)
        {
            messages.Add(ChatMessage.System(systemPrompt));
        }
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public AgentSessionOptions Options { get; }

    public AgentSessionState State { get; private set; } = AgentSessionState.Created;

    public IReadOnlyList<ChatMessage> Messages => messages;

    internal void AddMessage(ChatMessage message) => messages.Add(message);

    internal void MarkRunning() => State = AgentSessionState.Running;

    internal void MarkCompleted() => State = AgentSessionState.Completed;

    internal void MarkFailed() => State = AgentSessionState.Failed;

    internal void MarkCancelled() => State = AgentSessionState.Cancelled;
}
