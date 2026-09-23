namespace LoomX.Plugins;

/// <summary>Pipeline 执行结果：原样通过、已修改、被阻止（fail closed）。</summary>
public enum PipelineOutcome
{
    Passed,
    Modified,
    Blocked,
}

/// <summary>
/// Pipeline 处理结果。Blocked 时 Payload 为空且调用方必须映射为安全失败，
/// 原始数据不得继续流动。Diagnostic 只允许安全摘要，不得包含原始敏感值。
/// </summary>
public sealed record PipelineResult(PipelineOutcome Outcome, string Payload, string? Diagnostic = null)
{
    public static PipelineResult Pass(string payload) => new(PipelineOutcome.Passed, payload);

    public static PipelineResult Modify(string payload, string? diagnostic = null) =>
        new(PipelineOutcome.Modified, payload, diagnostic);

    public static PipelineResult Block(string diagnostic) =>
        new(PipelineOutcome.Blocked, string.Empty, diagnostic);
}

/// <summary>单次 Pipeline 执行的上下文。</summary>
public sealed record PipelineContext(
    string PipelineId,
    string? PluginId = null,
    string? EntryId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Router 宿主侧 Pipeline 抽象。具体 Router 生命周期边界只依赖本接口；
/// Pipeline 为空时保持原行为。
/// </summary>
public interface IPipeline
{
    string PipelineId { get; }

    ExtensionKind Kind { get; }

    /// <summary>按配置顺序执行启用的 Entry，返回最终处理结果。</summary>
    ValueTask<PipelineResult> ExecuteAsync(string payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// 可接收 Router Provider 执行元数据的 Pipeline。元数据只包含 Provider/协议/路径等安全摘要，
/// 不得包含认证 Header、请求正文、响应正文或其他敏感值。
/// </summary>
public interface IContextualPipeline : IPipeline
{
    ValueTask<PipelineResult> ExecuteAsync(
        string payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);
}
