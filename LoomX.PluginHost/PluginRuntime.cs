using Microsoft.Extensions.Logging;

namespace LoomX.Plugins.Host;

/// <summary>Runtime 配置：插件目录、插件数据根目录、Pipeline 顺序与启停。</summary>
public sealed class PluginRuntimeOptions
{
    /// <summary>插件根目录（每个子目录是一个插件包）。</summary>
    public required string PluginDirectory { get; init; }

    /// <summary>插件自有数据根目录（每插件一个子目录，规则等 Plugin-owned 数据落在这里）。</summary>
    public required string DataRootDirectory { get; init; }

    /// <summary>Pipeline 执行顺序：pipelineId → 按序的 "pluginId/extensionId"。未列出的 Entry 追加在发现顺序之后。</summary>
    public IDictionary<string, IReadOnlyList<string>> PipelineOrder { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>初始禁用的插件 id。</summary>
    public ISet<string> DisabledPlugins { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>初始禁用的 Entry（"pluginId/extensionId"）。</summary>
    public ISet<string> DisabledEntries { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>已加载插件的安全摘要（用于日志与诊断，不含任何敏感值）。</summary>
public sealed record LoadedPluginInfo(
    string Id,
    string Version,
    IReadOnlyList<string> Capabilities,
    int ExtensionCount,
    bool Enabled);

/// <summary>
/// 插件运行时：发现 → Manifest 验证 → ALC 加载 → Extension 注册 → Pipeline 编排。
/// 单个插件失败只记录诊断，不影响其余插件与宿主主流程。
/// </summary>
public sealed class PluginRuntime
{
    private sealed class LoadedPlugin
    {
        public required PluginManifest Manifest { get; init; }
        public required ILoomXPlugin Instance { get; init; }
        public required PluginLoadContext LoadContext { get; init; }
        public required List<PipelineEntry> Entries { get; init; }
        public bool Enabled { get; set; } = true;
    }

    private readonly List<LoadedPlugin> plugins = [];
    private readonly List<string> diagnostics = [];
    private readonly PipelineRegistry registry = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PluginRuntime> logger;

    private PluginRuntime(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<PluginRuntime>();
    }

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public IReadOnlyDictionary<string, Pipeline> Pipelines => registry.Pipelines;

    public IReadOnlyList<LoadedPluginInfo> PluginInfos => plugins
        .Select(plugin => new LoadedPluginInfo(
            plugin.Manifest.Id,
            plugin.Manifest.Version,
            plugin.Manifest.Capabilities,
            plugin.Entries.Count,
            plugin.Enabled))
        .ToArray();

    /// <summary>按 id 取 Pipeline；不存在（无 Entry 注册）时返回 null，调用方保持原行为。</summary>
    public IPipeline? GetPipeline(string pipelineId) =>
        registry.Pipelines.TryGetValue(pipelineId, out var pipeline) ? pipeline : null;

    public static PluginRuntime Start(PluginRuntimeOptions options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var runtime = new PluginRuntime(loggerFactory);
        runtime.DiscoverAndLoad(options);
        runtime.ApplyOrdering(options);
        runtime.ApplyDisabled(options);
        runtime.diagnostics.AddRange(runtime.registry.Diagnostics);
        runtime.LogSummary();
        return runtime;
    }

    /// <summary>启用/禁用插件：其全部 Entry 同步启停，重新启用后按配置顺序恢复执行。</summary>
    public bool SetPluginEnabled(string pluginId, bool enabled)
    {
        var plugin = plugins.FirstOrDefault(item => item.Manifest.Id == pluginId);
        if (plugin is null)
        {
            logger.LogWarning("插件启停被忽略：未找到插件 {PluginId}", pluginId);
            return false;
        }
        plugin.Enabled = enabled;
        foreach (var entry in plugin.Entries)
            entry.Enabled = enabled;
        logger.LogInformation("插件{State} {PluginId}", enabled ? "已启用" : "已禁用", pluginId);
        return true;
    }

    /// <summary>启用/禁用单个 Entry。</summary>
    public void SetEntryEnabled(string pluginId, string entryId, bool enabled)
    {
        var plugin = plugins.FirstOrDefault(item => item.Manifest.Id == pluginId);
        var entry = plugin?.Entries.FirstOrDefault(item => item.Extension.ExtensionId == entryId);
        if (plugin is null || entry is null) return;
        entry.Enabled = enabled && plugin.Enabled;
        logger.LogInformation("Pipeline Entry {State} {PluginId} {EntryId}",
            enabled ? "已启用" : "已禁用", pluginId, entryId);
    }

    private void DiscoverAndLoad(PluginRuntimeOptions options)
    {
        var discovered = PluginCatalog.Discover(options.PluginDirectory, diagnostics);
        foreach (var package in discovered)
        {
            try
            {
                LoadPlugin(package, options.DataRootDirectory);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"插件 {package.Manifest.Id} 加载失败：{exception.GetType().Name}");
                logger.LogError(exception, "插件加载失败 {PluginId}", package.Manifest.Id);
            }
        }
    }

    private void LoadPlugin(DiscoveredPlugin package, string dataRootDirectory)
    {
        var manifest = package.Manifest;
        var assemblyPath = Path.Combine(package.Directory, manifest.Assembly);
        if (!File.Exists(assemblyPath))
        {
            diagnostics.Add($"插件 {manifest.Id} 被拒绝：找不到程序集 {manifest.Assembly}。");
            return;
        }

        var loadContext = new PluginLoadContext(assemblyPath, $"plugin:{manifest.Id}");
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var pluginType = assembly.GetType(manifest.PluginType, throwOnError: false);
        if (pluginType is null || !typeof(ILoomXPlugin).IsAssignableFrom(pluginType))
        {
            diagnostics.Add($"插件 {manifest.Id} 被拒绝：类型 {manifest.PluginType} 不存在或未实现 ILoomXPlugin。");
            return;
        }

        if (Activator.CreateInstance(pluginType) is not ILoomXPlugin instance)
        {
            diagnostics.Add($"插件 {manifest.Id} 被拒绝：无法实例化 {manifest.PluginType}。");
            return;
        }

        if (instance.Id != manifest.Id)
        {
            diagnostics.Add($"插件 {manifest.Id} 被拒绝：实例 id {instance.Id} 与 Manifest 不一致。");
            return;
        }

        var dataDirectory = Path.Combine(dataRootDirectory, manifest.Id);
        System.IO.Directory.CreateDirectory(dataDirectory);
        instance.Initialize(new PluginInitializationContext(manifest.Id, dataDirectory));

        var extensions = instance.CreateExtensions().ToArray();
        var manifestExtensions = manifest.Extensions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var entries = new List<PipelineEntry>();
        var plugin = new LoadedPlugin
        {
            Manifest = manifest,
            Instance = instance,
            LoadContext = loadContext,
            Entries = entries,
        };

        foreach (var extension in extensions)
        {
            if (!manifestExtensions.TryGetValue(extension.ExtensionId, out var declared))
            {
                diagnostics.Add($"插件 {manifest.Id} 的 Extension {extension.ExtensionId} 未在 Manifest 声明，已拒绝。");
                continue;
            }
            if (declared.Kind != extension.Kind)
            {
                diagnostics.Add($"插件 {manifest.Id} 的 Extension {extension.ExtensionId} 类别与 Manifest 声明不一致，已拒绝。");
                continue;
            }
            if (declared.FailurePolicy != extension.FailurePolicy)
            {
                diagnostics.Add($"插件 {manifest.Id} 的 Extension {extension.ExtensionId} 失败策略与 Manifest 声明不一致，已拒绝。");
                continue;
            }

            var pipeline = registry.GetOrCreatePipeline(
                declared.Pipeline, extension.Kind, loggerFactory.CreateLogger<Pipeline>());
            var entry = new PipelineEntry(extension, manifest.Id);
            if (registry.Register(pipeline, entry))
                entries.Add(entry);
        }

        plugins.Add(plugin);
    }

    private void ApplyOrdering(PluginRuntimeOptions options)
    {
        foreach (var (pipelineId, order) in options.PipelineOrder)
        {
            if (!registry.Pipelines.TryGetValue(pipelineId, out var pipeline)) continue;
            var orderList = order.ToList();
            var ordered = pipeline.Entries
                .OrderBy(entry =>
                {
                    var index = orderList.IndexOf($"{entry.PluginId}/{entry.Extension.ExtensionId}");
                    return index >= 0 ? index : int.MaxValue;
                })
                .ToArray();
            pipeline.Reorder(ordered);
        }
    }

    private void ApplyDisabled(PluginRuntimeOptions options)
    {
        foreach (var pluginId in options.DisabledPlugins)
            SetPluginEnabled(pluginId, false);
        foreach (var key in options.DisabledEntries)
        {
            var separator = key.IndexOf('/');
            if (separator <= 0) continue;
            SetEntryEnabled(key[..separator], key[(separator + 1)..], false);
        }
    }

    private void LogSummary()
    {
        foreach (var info in PluginInfos)
        {
            logger.LogInformation(
                "插件已加载 {PluginId} {Version} 扩展数 {ExtensionCount} 能力 {Capabilities}",
                info.Id, info.Version, info.ExtensionCount, string.Join(',', info.Capabilities));
        }
        foreach (var diagnostic in diagnostics)
            logger.LogWarning("插件诊断 {Diagnostic}", diagnostic);
        logger.LogInformation(
            "插件运行时就绪：插件 {PluginCount} 个，Pipeline {PipelineCount} 条",
            plugins.Count, registry.Pipelines.Count);
    }
}
