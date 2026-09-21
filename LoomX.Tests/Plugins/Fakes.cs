using LoomX.Plugins;

namespace LoomX.Tests.Plugins;

/// <summary>可配置行为的 Tool Result 测试 Extension。</summary>
internal sealed class TestToolResultExtension(
    string extensionId,
    ExtensionFailurePolicy failurePolicy = ExtensionFailurePolicy.ContinueOnError,
    Func<string, string>? transform = null,
    List<string>? executionLog = null,
    string? tag = null) : IToolResultExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.ToolResult;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public int CallCount { get; private set; }

    public ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        CallCount++;
        executionLog?.Add(tag ?? extensionId);
        var output = transform?.Invoke(payload);
        return ValueTask.FromResult(output is null || output == payload
            ? PipelineResult.Pass(payload)
            : PipelineResult.Modify(output));
    }
}

/// <summary>Persistence 类别测试 Extension。</summary>
internal sealed class TestPersistenceExtension(
    string extensionId,
    ExtensionFailurePolicy failurePolicy = ExtensionFailurePolicy.ContinueOnError,
    Func<string, string>? transform = null) : IPersistenceExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Persistence;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public int CallCount { get; private set; }

    public ValueTask<PipelineResult> ProcessPersistenceAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        CallCount++;
        var output = transform?.Invoke(payload);
        return ValueTask.FromResult(output is null || output == payload
            ? PipelineResult.Pass(payload)
            : PipelineResult.Modify(output));
    }
}

/// <summary>执行时抛异常的测试 Extension，可切换类别与失败策略。</summary>
internal sealed class ThrowingExtension(
    string extensionId,
    ExtensionKind kind,
    ExtensionFailurePolicy failurePolicy) : IToolResultExtension, IPersistenceExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => kind;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("测试异常。");

    public ValueTask<PipelineResult> ProcessPersistenceAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("测试异常。");
}

/// <summary>返回 Blocked 的测试 Extension。</summary>
internal sealed class BlockingToolResultExtension(string extensionId) : IToolResultExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.ToolResult;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        ValueTask.FromResult(PipelineResult.Block("测试阻止。"));
}
