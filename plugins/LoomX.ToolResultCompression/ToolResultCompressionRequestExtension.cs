using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>Router 请求扩展点：压缩历史工具结果，失败放行原文。</summary>
public sealed class ToolResultCompressionRequestExtension(
    ToolResultCompressionOptionsStore optionsStore,
    ToolResultCompressionObservability? observability = null) : IRequestExtension
{
    private readonly ToolResultCompressionObservability observability = observability ?? new();

    public string ExtensionId => "toolresult.request";

    public ExtensionKind Kind => ExtensionKind.Request;

    /// <summary>压缩不是数据安全能力：异常时放行原文，不阻断 Router。</summary>
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;

    public IReadOnlyList<string> Capabilities =>
        ["toolresult.compress", "toolresult.dedupe", "toolresult.truncate", "toolresult.summarize"];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var options = optionsStore.Current;
            if (!options.Enabled)
            {
                observability.RecordRequest();
                return ValueTask.FromResult(PipelineResult.Pass(payload));
            }

            var apiMode = context.Metadata is not null
                && context.Metadata.TryGetValue("api_mode", out var mode)
                    ? mode
                    : null;

            if (!ToolResultRequestRewriter.TryRewrite(payload, apiMode, options, out var rewritten, out var report))
            {
                observability.RecordRequest();
                return ValueTask.FromResult(PipelineResult.Pass(payload));
            }

            observability.RecordRequest(report);
            return ValueTask.FromResult(PipelineResult.Modify(
                rewritten,
                $"已压缩 {report.CompressedToolResults} 条工具结果，节省 {report.SavedChars} 字符（约 {report.SavedTokens} token）。"));
        }
        catch (Exception)
        {
            observability.RecordError();
            return ValueTask.FromResult(PipelineResult.Pass(payload));
        }
    }
}
