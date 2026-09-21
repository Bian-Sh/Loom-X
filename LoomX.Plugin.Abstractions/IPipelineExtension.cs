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

/// <summary>Provider 请求扩展点（首版只声明不挂载）。</summary>
public interface IRequestExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}

/// <summary>工具结果扩展点：ToolResult 进入会话历史前执行。</summary>
public interface IToolResultExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}

/// <summary>持久化扩展点：会话内容写入存储前执行。</summary>
public interface IPersistenceExtension : IPipelineExtension
{
    ValueTask<PipelineResult> ProcessPersistenceAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken);
}
