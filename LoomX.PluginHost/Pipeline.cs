using Microsoft.Extensions.Logging;

namespace LoomX.Plugins.Host;

/// <summary>Pipeline 中的一个有序 Entry。禁用后不参与执行，重新启用按配置顺序恢复。</summary>
public sealed class PipelineEntry(IPipelineExtension extension, string pluginId)
{
    public IPipelineExtension Extension { get; } = extension;

    public string PluginId { get; } = pluginId;

    public bool Enabled { get; internal set; } = true;
}

/// <summary>
/// 有序 Pipeline：Entry 按配置顺序执行，上一 Entry 的输出是下一 Entry 的输入。
/// 异常隔离：普通 Entry 失败记录诊断并继续；数据安全类（FailClosed）Entry 失败返回 Blocked，
/// 原始数据不得放行。插件之间互不感知。
/// </summary>
public sealed class Pipeline : IPipeline
{
    private readonly List<PipelineEntry> entries = [];
    private readonly ILogger logger;

    public Pipeline(string pipelineId, ExtensionKind kind, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        ArgumentNullException.ThrowIfNull(logger);
        PipelineId = pipelineId;
        Kind = kind;
        this.logger = logger;
    }

    public string PipelineId { get; }

    public ExtensionKind Kind { get; }

    /// <summary>配置顺序的 Entry 列表（含禁用项）。</summary>
    public IReadOnlyList<PipelineEntry> Entries => entries;

    internal void AddEntry(PipelineEntry entry) => entries.Add(entry);

    /// <summary>按配置顺序重排 Entry（只调整顺序，不增删）。</summary>
    internal void Reorder(IReadOnlyList<PipelineEntry> configuredOrder)
    {
        entries.Clear();
        entries.AddRange(configuredOrder);
    }

    public async ValueTask<PipelineResult> ExecuteAsync(string payload, CancellationToken cancellationToken = default)
    {
        var current = payload ?? string.Empty;
        var modified = false;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.Enabled) continue;

            var context = new PipelineContext(PipelineId, entry.PluginId, entry.Extension.ExtensionId);
            PipelineResult result;
            try
            {
                result = entry.Extension switch
                {
                    IToolResultExtension toolResult =>
                        await toolResult.ProcessToolResultAsync(context, current, cancellationToken),
                    IPersistenceExtension persistence =>
                        await persistence.ProcessPersistenceAsync(context, current, cancellationToken),
                    IRequestExtension request =>
                        await request.ProcessRequestAsync(context, current, cancellationToken),
                    _ => PipelineResult.Pass(current),
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (entry.Extension.FailurePolicy == ExtensionFailurePolicy.FailClosed)
                {
                    // 数据安全类 Entry 失败：fail closed，原始数据不得进入后续组件。
                    logger.LogError(
                        exception,
                        "Pipeline Entry 执行失败，已 fail closed {PipelineId} {PluginId} {EntryId} {ExceptionType}",
                        PipelineId, entry.PluginId, entry.Extension.ExtensionId, exception.GetType().Name);
                    return PipelineResult.Block("数据安全处理失败，已阻止原始数据继续流动。");
                }

                logger.LogWarning(
                    exception,
                    "Pipeline Entry 执行失败，已跳过 {PipelineId} {PluginId} {EntryId} {ExceptionType}",
                    PipelineId, entry.PluginId, entry.Extension.ExtensionId, exception.GetType().Name);
                continue;
            }

            if (result.Outcome == PipelineOutcome.Blocked)
            {
                logger.LogWarning(
                    "Pipeline Entry 阻止数据继续流动 {PipelineId} {PluginId} {EntryId} {Diagnostic}",
                    PipelineId, entry.PluginId, entry.Extension.ExtensionId, result.Diagnostic);
                return result;
            }

            modified |= result.Outcome == PipelineOutcome.Modified;
            current = result.Payload;
        }

        return modified ? PipelineResult.Modify(current) : PipelineResult.Pass(current);
    }
}
