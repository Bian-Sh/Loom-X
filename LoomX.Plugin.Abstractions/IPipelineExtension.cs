namespace LoomX.Plugins;

/// <summary>
/// Pipeline Extension 基接口。每个 Extension 只能归属一个 Pipeline；
/// 插件之间互不感知，不存在 before/after/requires 依赖声明。
/// </summary>
public interface IPipelineExtension
{
    /// <summary>Extension id（插件内唯一），与 Manifest 声明对应。</summary>
    string ExtensionId { get; }

    /// <summary>扩展点类别。</summary>
    ExtensionKind Kind { get; }

    /// <summary>失败策略；数据安全类 Extension 必须为 FailClosed。</summary>
    ExtensionFailurePolicy FailurePolicy { get; }

    /// <summary>能力声明（如 credential.detect / credential.mask）。</summary>
    IReadOnlyList<string> Capabilities { get; }
}

/// <summary>Router Provider 请求正文扩展点：完整请求构造后、发送给外部 Provider 前执行。</summary>
public interface IRequestExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}

/// <summary>Router Provider 响应正文扩展点：上游响应返回给 Router 客户前执行。</summary>
public interface IResponseExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessResponseAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}

/// <summary>Router 结构化 Tool Result 扩展点（候选契约，生产挂载留待后续 change）。</summary>
public interface IToolResultExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}

/// <summary>Router 持久化扩展点（候选契约，待 Router audit/cache/trace 存储出现后挂载）。</summary>
public interface IPersistenceExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessPersistenceAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}
