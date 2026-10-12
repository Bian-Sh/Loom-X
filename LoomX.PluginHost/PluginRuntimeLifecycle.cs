using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace LoomX.Plugins.Host;

/// <summary>完整 Provider 调用持有同一快照；释放前保留该代际所有插件能力。</summary>
public sealed class PluginRuntimeLease : IAsyncDisposable
{
    private IReadOnlyDictionary<string, Pipeline> pipelines;
    private Func<ValueTask>? release;
    internal PluginRuntimeLease(IReadOnlyDictionary<string, Pipeline> pipelines, Func<ValueTask> release)
    { this.pipelines = pipelines; this.release = release; }
    public IPipeline? GetPipeline(string pipelineId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref release) is null, this);
        return pipelines.TryGetValue(pipelineId, out var pipeline) ? pipeline : null;
    }
    public ValueTask DisposeAsync()
    {
        var action = Interlocked.Exchange(ref release, null);
        if (action is null) return ValueTask.CompletedTask;
        pipelines = new ReadOnlyDictionary<string, Pipeline>(new Dictionary<string, Pipeline>());
        return action();
    }
}

public sealed partial class PluginRuntime : IAsyncDisposable
{
    private readonly object runtimeGate = new();
    private readonly SemaphoreSlim reloadGate = new(1, 1);
    private readonly Dictionary<string, RuntimePipeline> livePipelines = new(StringComparer.Ordinal);
    private PluginRuntimeOptions options = null!;
    private bool disposed;

    public PluginRuntimeLease AcquireLease()
    {
        lock (runtimeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var members = plugins.ToArray();
            foreach (var plugin in members) plugin.Leases++;
            return new(new ReadOnlyDictionary<string, Pipeline>(registry.Pipelines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                () => ReleaseLeaseAsync(members));
        }
    }

    private PluginOperationLease? AcquirePluginOperation(string pluginId)
    {
        lock (runtimeGate)
        {
            if (disposed) return null;
            var plugin = plugins.FirstOrDefault(item => item.Manifest.Id == pluginId);
            if (plugin is null) return null;
            plugin.Leases++;
            return new(plugin, () => ReleaseLeaseAsync([plugin]));
        }
    }

    private sealed class PluginOperationLease(LoadedPlugin plugin, Func<ValueTask> release) : IDisposable, IAsyncDisposable
    {
        private Func<ValueTask>? action = release;
        public LoadedPlugin Plugin => plugin;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref action, null)?.Invoke() ?? ValueTask.CompletedTask;
        public void Dispose() => Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }

    private IPipeline? GetLivePipeline(string pipelineId)
    {
        lock (runtimeGate)
        {
            if (disposed || !registry.Pipelines.TryGetValue(pipelineId, out var pipeline)) return null;
            if (!livePipelines.TryGetValue(pipelineId, out var live))
                livePipelines.Add(pipelineId, live = new(this, pipelineId, pipeline.Kind));
            return live;
        }
    }

    private sealed class RuntimePipeline(PluginRuntime owner, string pipelineId, ExtensionKind kind) : IContextualPipeline
    {
        public string PipelineId => pipelineId;
        public ExtensionKind Kind => kind;
        public ValueTask<PipelineResult> ExecuteAsync(string payload, CancellationToken cancellationToken = default) =>
            ExecuteAsync(payload, new Dictionary<string, string>(), cancellationToken);
        public async ValueTask<PipelineResult> ExecuteAsync(string payload, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default)
        {
            await using var lease = owner.AcquireLease();
            var pipeline = lease.GetPipeline(pipelineId);
            return pipeline is IContextualPipeline contextual
                ? await contextual.ExecuteAsync(payload, metadata, cancellationToken).ConfigureAwait(false)
                : pipeline is not null ? await pipeline.ExecuteAsync(payload, cancellationToken).ConfigureAwait(false) : PipelineResult.Pass(payload);
        }
    }

    private PipelineRegistry BuildSnapshot(IEnumerable<LoadedPlugin> members)
    {
        var snapshot = new PipelineRegistry();
        foreach (var plugin in members)
        {
            var declarations = plugin.Manifest.Extensions.ToDictionary(extension => extension.Id, StringComparer.Ordinal);
            foreach (var original in plugin.Entries)
            {
                var declaration = declarations[original.Extension.ExtensionId];
                var pipeline = snapshot.GetOrCreatePipeline(declaration.Pipeline, declaration.Kind, loggerFactory.CreateLogger<Pipeline>());
                var entry = new PipelineEntry(original.Extension, plugin.Manifest.Id) { Enabled = original.Enabled };
                if (!snapshot.Register(pipeline, entry)) throw new InvalidOperationException("插件注册快照验证失败。");
            }
        }
        foreach (var (pipelineId, order) in options.PipelineOrder)
        {
            if (!snapshot.Pipelines.TryGetValue(pipelineId, out var pipeline)) continue;
            var ordered = pipeline.Entries.OrderBy(entry =>
            {
                var index = order.ToList().IndexOf($"{entry.PluginId}/{entry.Extension.ExtensionId}");
                return index >= 0 ? index : int.MaxValue;
            }).ToArray();
            pipeline.Reorder(ordered);
        }
        return snapshot;
    }

    /// <summary>候选完全验证后一次切换；失败仅退休候选，保留当前实例。</summary>
    public async Task<bool> ReloadAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        LoadedPlugin? candidate = null;
        LoadedPlugin? retired = null;
        try
        {
            lock (runtimeGate) ObjectDisposedException.ThrowIf(disposed, this);
            var discoveryDiagnostics = new List<string>();
            var packages = PluginCatalog.Discover(options.PluginDirectory, discoveryDiagnostics);
            var package = packages.SingleOrDefault(item => item.Manifest.Id == pluginId);
            if (package is null) { AddReloadDiagnostic(pluginId); return false; }
            candidate = await Task.Run(() => LoadPlugin(package, options, candidateOnly: true), cancellationToken).ConfigureAwait(false);
            if (candidate is null) { AddReloadDiagnostic(pluginId); return false; }
            cancellationToken.ThrowIfCancellationRequested();
            lock (runtimeGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                retired = plugins.FirstOrDefault(plugin => plugin.Manifest.Id == pluginId);
                if (retired is not null)
                {
                    candidate.Enabled = retired.Enabled;
                    foreach (var entry in candidate.Entries)
                        entry.Enabled = retired.Entries.FirstOrDefault(old => old.Extension.ExtensionId == entry.Extension.ExtensionId)?.Enabled
                            ?? (candidate.Enabled || candidate.Instance is IPluginEnableStateAware);
                    if (!candidate.Enabled && candidate.Instance is IPluginEnableStateAware stateAware)
                        stateAware.OnPluginEnabledChanged(false);
                }
                var members = plugins.Where(plugin => plugin != retired).Append(candidate).ToArray();
                var snapshot = BuildSnapshot(members);
                plugins.Clear(); plugins.AddRange(members);
                registry = snapshot;
                if (retired is not null) retired.Retiring = true;
                candidate = null;
            }
            if (retired is not null) await DisposeRetiredAsync([retired]).ConfigureAwait(false);
            logger.LogInformation("插件重新加载完成 {PluginId}", pluginId);
            NotifyUiInvalidated(pluginId);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            AddReloadDiagnostic(pluginId);
            logger.LogWarning(new InvalidOperationException("插件重新加载失败，异常内容已隐藏。"),
                "插件重新加载失败 {PluginId} {ExceptionType}", pluginId, exception.GetType().Name);
            return false;
        }
        finally
        {
            if (candidate is not null)
            {
                lock (runtimeGate) candidate.Retiring = true;
                await DisposeRetiredAsync([candidate]).ConfigureAwait(false);
            }
            reloadGate.Release();
        }
    }

    private void AddReloadDiagnostic(string pluginId)
    {
        lock (diagnosticsGate)
            structuredDiagnostics.Add(new(pluginId, PluginDiagnosticStage.Reload, PluginDiagnosticSeverity.Error,
                PluginDiagnosticCodes.ReloadFailed, "候选插件验证失败，当前实例保持不变。"));
    }

    private async ValueTask ReleaseLeaseAsync(LoadedPlugin[] members)
    {
        lock (runtimeGate) foreach (var plugin in members) plugin.Leases--;
        await DisposeRetiredAsync(members).ConfigureAwait(false);
    }

    private async ValueTask DisposeRetiredAsync(IEnumerable<LoadedPlugin> members)
    {
        List<LoadedPlugin> ready = [];
        lock (runtimeGate)
        {
            foreach (var plugin in members)
                if (plugin.Retiring && plugin.Leases == 0 && !plugin.Released)
                { plugin.Released = true; ready.Add(plugin); }
        }
        foreach (var plugin in ready)
        {
            try
            {
                if (plugin.UiProvider is not null && plugin.UiInvalidatedHandler is not null)
                {
                    try { plugin.UiProvider.UiInvalidated -= plugin.UiInvalidatedHandler; }
                    catch (Exception exception)
                    {
                        logger.LogWarning(new InvalidOperationException("插件 UI 订阅释放失败。"),
                            "插件 UI 订阅退休异常 {PluginId} {ExceptionType}", plugin.Manifest.Id, exception.GetType().Name);
                    }
                }
                if (plugin.Instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (plugin.Instance is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception exception)
            {
                logger.LogWarning(new InvalidOperationException("插件资源释放失败，异常内容已隐藏。"),
                    "插件实例退休异常 {PluginId} {ExceptionType}", plugin.Manifest.Id, exception.GetType().Name);
            }
            finally
            {
                try { await plugin.Bridge.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    logger.LogWarning(new InvalidOperationException("插件能力释放失败。"),
                        "插件能力退休异常 {PluginId} {ExceptionType}", plugin.Manifest.Id, exception.GetType().Name);
                }
                try { plugin.LoadContext.Unload(); }
                catch (Exception exception)
                {
                    lock (diagnosticsGate)
                        structuredDiagnostics.Add(new(plugin.Manifest.Id, PluginDiagnosticStage.Load, PluginDiagnosticSeverity.Warning,
                            "LX_ALC_UNLOAD_FAILED", "旧插件加载上下文无法完成卸载，当前实例仍保持有效。"));
                    logger.LogWarning(new InvalidOperationException("插件加载上下文退休失败。"),
                        "插件加载上下文退休异常 {PluginId} {ExceptionType}", plugin.Manifest.Id, exception.GetType().Name);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        LoadedPlugin[] members;
        lock (runtimeGate)
        {
            if (disposed) return;
            disposed = true;
            members = plugins.ToArray();
            foreach (var plugin in members) plugin.Retiring = true;
            plugins.Clear(); registry = new(); livePipelines.Clear();
        }
        await DisposeRetiredAsync(members).ConfigureAwait(false);
    }
}