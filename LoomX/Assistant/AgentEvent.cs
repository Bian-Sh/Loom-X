namespace LoomX.Assistant;

/// <summary>
/// 小助手结构化事件类型。UI 通过 Activity Projection 消费这些事件，不直接绑定 Harness 内部状态。
/// </summary>
public enum AgentEventKind
{
    SessionStarted,
    TextDelta,
    ToolCallStarted,
    ToolCallCompleted,
    SkillLoaded,
    SubagentStarted,
    SubagentCompleted,
    TaskCompleted,
    TaskFailed,
    WaitingForUser,
}

/// <summary>
/// 小助手事件。只允许携带安全摘要，禁止包含 Secret、请求/响应正文等敏感内容。
/// </summary>
public sealed record AgentEvent(
    string SessionId,
    AgentEventKind Kind,
    DateTimeOffset Timestamp)
{
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public bool? Success { get; init; }
    public string? Detail { get; init; }

    public static AgentEvent Create(string sessionId, AgentEventKind kind) =>
        new(sessionId, kind, DateTimeOffset.UtcNow);
}
