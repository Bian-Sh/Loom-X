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
    private readonly List<AgentEvent> activities = [];

    public AgentSession(AgentSessionOptions? options = null)
    {
        Options = options ?? new AgentSessionOptions();
        if (Options.SystemPrompt is { Length: > 0 } systemPrompt)
        {
            messages.Add(ChatMessage.System(systemPrompt));
        }
    }

    public string Id { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>从持久化恢复会话标识，保证再次保存时写回同一文件。</summary>
    internal void RestoreId(string id) => Id = id;

    public AgentSessionOptions Options { get; }

    public AgentSessionState State { get; private set; } = AgentSessionState.Created;

    public IReadOnlyList<ChatMessage> Messages => messages;
    public IReadOnlyList<AgentEvent> Activities => activities;

    internal void AddMessage(ChatMessage message) => messages.Add(message);

    /// <summary>从持久化恢复消息（不清空系统提示之外的校验，内容由存储层保证安全）。</summary>
    internal void RestoreMessage(ChatMessage message) => messages.Add(message);
    internal void RecordActivity(AgentEvent activity) => activities.Add(activity);

    /// <summary>从持久化恢复状态；Running 属于崩溃残留，恢复为 Cancelled。</summary>
    internal void RestoreState(AgentSessionState state) =>
        State = state == AgentSessionState.Running ? AgentSessionState.Cancelled : state;

    internal void MarkRunning() => State = AgentSessionState.Running;

    internal void MarkCompleted() => State = AgentSessionState.Completed;

    internal void MarkFailed() => State = AgentSessionState.Failed;

    internal void MarkCancelled() => State = AgentSessionState.Cancelled;
}
