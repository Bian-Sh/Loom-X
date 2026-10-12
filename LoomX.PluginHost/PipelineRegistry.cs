namespace LoomX.Plugins.Host;

/// <summary>
/// Extension 注册与 Pipeline 归属约束：同一 Extension 实例只能归属一个 Pipeline，
/// 重复挂载到不同 Pipeline 被拒绝；Extension 的 Kind 必须与 Pipeline 的 Kind 一致。
/// </summary>
public sealed class PipelineRegistry
{
    private readonly Dictionary<string, Pipeline> pipelines = new(StringComparer.Ordinal);
    private readonly Dictionary<IPipelineExtension, string> ownership = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> diagnostics = [];

    public IReadOnlyDictionary<string, Pipeline> Pipelines => pipelines;

    public IReadOnlyList<string> Diagnostics => diagnostics;

    /// <summary>候选注册失败时撤销其全部归属，不能留下引用已释放资源的 Entry。</summary>
    internal void RemovePluginEntries(string pluginId)
    {
        foreach (var pipeline in pipelines.Values)
        {
            foreach (var entry in pipeline.Entries.Where(entry => entry.PluginId == pluginId).ToArray())
                ownership.Remove(entry.Extension);
            pipeline.RemovePluginEntries(pluginId);
        }
        foreach (var empty in pipelines.Where(pair => pair.Value.Entries.Count == 0).Select(pair => pair.Key).ToArray())
            pipelines.Remove(empty);
    }

    public Pipeline GetOrCreatePipeline(string pipelineId, ExtensionKind kind, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (pipelines.TryGetValue(pipelineId, out var existing))
        {
            if (existing.Kind != kind)
                throw new InvalidOperationException($"Pipeline {pipelineId} 已以类别 {existing.Kind} 存在，不能复用为 {kind}。");
            return existing;
        }

        var pipeline = new Pipeline(pipelineId, kind, logger);
        pipelines.Add(pipelineId, pipeline);
        return pipeline;
    }

    /// <summary>注册 Entry；归属冲突或 Kind 不匹配时拒绝并记录诊断，返回 false。</summary>
    public bool Register(Pipeline pipeline, PipelineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(entry);

        if (ownership.TryGetValue(entry.Extension, out var ownerPipelineId))
        {
            diagnostics.Add(
                $"Extension {entry.PluginId}/{entry.Extension.ExtensionId} 已归属 Pipeline {ownerPipelineId}，" +
                $"拒绝重复挂载到 {pipeline.PipelineId}。");
            return false;
        }

        if (entry.Extension.Kind != pipeline.Kind)
        {
            diagnostics.Add(
                $"Extension {entry.PluginId}/{entry.Extension.ExtensionId} 类别 {entry.Extension.Kind} " +
                $"与 Pipeline {pipeline.PipelineId} 类别 {pipeline.Kind} 不匹配，已拒绝。");
            return false;
        }

        ownership.Add(entry.Extension, pipeline.PipelineId);
        pipeline.AddEntry(entry);
        return true;
    }
}
