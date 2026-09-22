using LoomX.Plugins;

namespace LoomX.Tests.Plugins;

/// <summary>可配置行为的 Router Request 测试 Extension。</summary>
internal sealed class TestRequestExtension(
    string extensionId,
    ExtensionFailurePolicy failurePolicy = ExtensionFailurePolicy.ContinueOnError,
    Func<string, string>? transform = null,
    List<string>? executionLog = null,
    string? tag = null) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public int CallCount { get; private set; }

    public ValueTask<PipelineResult> ProcessRequestAsync(
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

/// <summary>可配置行为的 Router Response 测试 Extension。</summary>
internal sealed class TestResponseExtension(
    string extensionId,
    Func<string, string>? transform = null) : IResponseExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Response;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessResponseAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        var output = transform?.Invoke(payload);
        return ValueTask.FromResult(output is null || output == payload
            ? PipelineResult.Pass(payload)
            : PipelineResult.Modify(output));
    }
}

/// <summary>执行时抛异常的 Router Request 测试 Extension。</summary>
internal sealed class ThrowingRequestExtension(
    string extensionId,
    ExtensionFailurePolicy failurePolicy) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("测试异常。");
}

/// <summary>返回 Blocked 的 Router Request 测试 Extension。</summary>
internal sealed class BlockingRequestExtension(string extensionId) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        ValueTask.FromResult(PipelineResult.Block("测试阻止。"));
}
