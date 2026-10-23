using Microsoft.Extensions.Logging;

namespace LoomX.Plugins.Host;

public enum PluginRuntimeStartupPhase
{
    Compiling,
    Loading,
    Completed
}

/// <summary>Runtime 配置：插件目录、插件数据根目录、Pipeline 顺序与启停。</summary>
public sealed class PluginRuntimeOptions
{
    /// <summary>插件根目录（每个子目录是一个插件包）。</summary>
    public required string PluginDirectory { get; init; }

    /// <summary>插件自有数据根目录（每插件一个子目录，规则等 Plugin-owned 数据落在这里）。</summary>
    public required string DataRootDirectory { get; init; }

    /// <summary>宿主组合根按插件创建能力作用域；未装配的能力不会隐式可用。</summary>
    public Func<PluginManifest, string, PluginBridgeScope>? BridgeFactory { get; init; }
    /// <summary>宿主随附离线编译器；不搜索用户本机 SDK。</summary>
    public SourcePluginCompiler? SourceCompiler { get; init; }

    /// <summary>报告首次启动时的编译、加载和完成阶段。</summary>
    public Action<PluginRuntimeStartupPhase>? StartupProgress { get; init; }

    /// <summary>Pipeline 执行顺序：pipelineId → 按序的 "pluginId/extensionId"。未列出的 Entry 追加在发现顺序之后。</summary>
    public IDictionary<string, IReadOnlyList<string>> PipelineOrder { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>初始禁用的插件 id。</summary>
    public ISet<string> DisabledPlugins { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>初始禁用的 Entry（"pluginId/extensionId"）。</summary>
    public ISet<string> DisabledEntries { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 启动期插件准备（验证 + 源码编译）的并行度；0（默认）= min(处理器核数, 4)，1 = 串行。
    /// 只影响执行调度，不改变诊断与加载的确定性顺序。
    /// </summary>
    internal int CompileParallelism { get; init; }
}

/// <summary>已加载插件的安全摘要（用于日志与诊断，不含任何敏感值）。</summary>
public sealed record LoadedPluginInfo(
    string Id,
    string Version,
    IReadOnlyList<string> Capabilities,
    int ExtensionCount,
    bool Enabled,
    bool HasCardUi = false,
    bool HasDetailUi = false,
    IReadOnlyList<string>? InteractiveUiContributions = null,
    string RuntimeKind = "assembly");

/// <summary>插件 UI 快照失效通知，只携带安全的插件标识。</summary>
public sealed class PluginUiInvalidatedEventArgs(string pluginId) : EventArgs
{
    public string PluginId { get; } = pluginId;
}

/// <summary>
/// 插件运行时：发现 → Manifest 验证 → ALC 加载 → Extension 注册 → Pipeline 编排。
/// 单个插件失败只记录诊断，不影响其余插件与宿主主流程。
/// </summary>
public sealed partial class PluginRuntime
{
    private sealed class LoadedPlugin
    {
        public required PluginManifest Manifest { get; init; }
        public required ILoomXPlugin Instance { get; init; }
        public required PluginLoadContext LoadContext { get; init; }
        public required List<PipelineEntry> Entries { get; init; }
        public required PluginBridgeScope Bridge { get; init; }
        public IPluginUiContributionProvider? UiProvider { get; init; }
        public IPluginUiCommandHandler? CommandHandler { get; init; }
        public EventHandler? UiInvalidatedHandler { get; init; }
        public bool Enabled { get; set; } = true;
        public int Leases { get; set; }
        public bool Retiring { get; set; }
        public bool Released { get; set; }
    }

    private readonly List<LoadedPlugin> plugins = [];
    private readonly List<string> diagnostics = [];
    private readonly List<PluginDiagnostic> structuredDiagnostics = [];
    private readonly HashSet<string> uiDiagnosticKeys = new(StringComparer.Ordinal);
    private readonly object diagnosticsGate = new();
    private PipelineRegistry registry = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PluginRuntime> logger;

    private PluginRuntime(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<PluginRuntime>();
    }

    public IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (diagnosticsGate)
                return diagnostics.ToArray();
        }
    }

    public IReadOnlyList<PluginDiagnostic> StructuredDiagnostics
    {
        get { lock (diagnosticsGate) return structuredDiagnostics.ToArray(); }
    }

    public event EventHandler<PluginUiInvalidatedEventArgs>? PluginUiInvalidated;

    public IReadOnlyDictionary<string, Pipeline> Pipelines
    {
        get { lock (runtimeGate) return new System.Collections.ObjectModel.ReadOnlyDictionary<string, Pipeline>(registry.Pipelines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)); }
    }

    public IReadOnlyList<LoadedPluginInfo> PluginInfos
    {
        get { lock (runtimeGate) return plugins
        .Select(plugin => new LoadedPluginInfo(
            plugin.Manifest.Id,
            plugin.Manifest.Version,
            plugin.Manifest.Capabilities,
            plugin.Entries.Count,
            plugin.Enabled,
            plugin.UiProvider is not null && HasUiSlot(plugin.Manifest, PluginUiSlot.CardBody),
            plugin.UiProvider is not null && HasUiSlot(plugin.Manifest, PluginUiSlot.DetailBody),
            plugin.Manifest.Ui?.Contributions
                .Where(item => item.Interactive)
                .Select(item => item.Id)
                .ToArray() ?? [],
            plugin.Manifest.Runtime?.Kind ?? "assembly"))
        .ToArray(); }
    }

    /// <summary>按 id 取 Pipeline；不存在（无 Entry 注册）时返回 null，调用方保持原行为。</summary>
    public IPipeline? GetPipeline(string pipelineId) =>
        GetLivePipeline(pipelineId);

    /// <summary>
    /// 向插件索取当前文化下的展示信息。展示信息是 UI 贡献契约的默认方法，
    /// 与 <see cref="GetUiContributions"/> 共用同一个 Provider 与同一份失效信号，
    /// 因此这里不再有独立的 Provider 分支。插件未实现或返回空时返回 null，
    /// 由调用方回落插件 id 或宿主自身的通用文案。
    /// </summary>
    public PluginDisplayInfo? GetPluginDisplayInfo(string pluginId, string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);
        using var operation = AcquirePluginOperation(pluginId);
        var plugin = operation?.Plugin;
        if (plugin?.UiProvider is null) return null;

        try
        {
            var info = plugin.UiProvider.GetDisplayInfo(new PluginUiContext(cultureName));
            return info is null || info.IsEmpty ? null : info;
        }
        catch (Exception exception)
        {
            AddUiDiagnostic(pluginId, "display-info", "PROVIDER_FAILURE");
            logger.LogWarning(
                new InvalidOperationException("插件 UI 操作失败，异常内容已隐藏。"),
                "插件展示信息获取失败 {PluginId} {ExceptionType}",
                pluginId,
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>读取插件在指定 Slot 的已验证声明式 UI 快照。</summary>
    public IReadOnlyList<PluginUiContribution> GetUiContributions(
        string pluginId,
        PluginUiSlot slot,
        string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);
        using var operation = AcquirePluginOperation(pluginId);
        var plugin = operation?.Plugin;
        if (plugin?.UiProvider is null || plugin.Manifest.Ui is null) return [];

        IReadOnlyList<PluginUiContribution> provided;
        try
        {
            provided = plugin.UiProvider.GetUiContributions(new PluginUiContext(cultureName)) ?? [];
        }
        catch (Exception exception)
        {
            AddUiDiagnostic(pluginId, "provider", "PROVIDER_FAILURE");
            logger.LogWarning(
                new InvalidOperationException("插件 UI 操作失败，异常内容已隐藏。"),
                "插件 UI 快照获取失败 {PluginId} {ExceptionType}",
                pluginId,
                exception.GetType().Name);
            return [];
        }

        var declarations = plugin.Manifest.Ui.Contributions
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PluginUiContribution>();
        foreach (var contribution in provided)
        {
            if (contribution is null)
            {
                AddUiDiagnostic(pluginId, "unknown", "NULL_CONTRIBUTION");
                continue;
            }
            if (!seen.Add(contribution.Id))
            {
                AddUiDiagnostic(pluginId, contribution.Id, "DUPLICATE_RUNTIME_ID");
                continue;
            }
            if (!declarations.TryGetValue(contribution.Id, out var declared)
                || declared.Slot != contribution.Slot)
            {
                AddUiDiagnostic(pluginId, contribution.Id, "UNDECLARED_CONTRIBUTION");
                continue;
            }

            var validation = PluginUiValidator.Validate(contribution);
            if (!validation.IsValid)
            {
                AddUiDiagnostic(pluginId, contribution.Id, validation.ReasonCode);
                continue;
            }
            if (contribution.Slot == slot)
                result.Add(contribution);
        }
        return result;
    }

    /// <summary>
    /// 把插件声明式 UI 上的一次交互回传给插件。命令只接受 Manifest 声明为交互的 Contribution；
    /// 任何非法输入或插件异常都只记录安全诊断并返回未处理，不影响插件 Pipeline。
    /// </summary>
    public async Task<PluginUiCommandResult> ExecuteUiCommandAsync(
        string pluginId,
        PluginUiCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 128)
        {
            logger.LogWarning("插件 UI 命令被拒绝：命令标识非法 {PluginId}", pluginId);
            return PluginUiCommandResult.Unhandled;
        }
        if (command.Arguments is { Count: > 32 })
        {
            logger.LogWarning("插件 UI 命令被拒绝：参数过多 {PluginId} {CommandId}", pluginId, command.CommandId);
            return PluginUiCommandResult.Unhandled;
        }
        foreach (var value in command.Arguments.Values)
        {
            if (value is null || value.Length > 4096)
            {
                logger.LogWarning("插件 UI 命令被拒绝：参数超长 {PluginId} {CommandId}", pluginId, command.CommandId);
                return PluginUiCommandResult.Unhandled;
            }
        }

        await using var operation = AcquirePluginOperation(pluginId);
        var plugin = operation?.Plugin;
        if (plugin is null)
        {
            logger.LogWarning("插件 UI 命令被拒绝：未找到插件 {PluginId}", pluginId);
            return PluginUiCommandResult.Unhandled;
        }
        if (plugin.CommandHandler is null)
        {
            logger.LogWarning("插件 UI 命令被拒绝：插件未实现命令处理器 {PluginId}", pluginId);
            return PluginUiCommandResult.Unhandled;
        }
        if (plugin.Manifest.Ui?.Contributions.FirstOrDefault(
                item => string.Equals(item.Id, command.ContributionId, StringComparison.Ordinal))
            is not { Interactive: true })
        {
            logger.LogWarning(
                "插件 UI 命令被拒绝：Contribution 未声明为交互 {PluginId} {ContributionId}",
                pluginId,
                command.ContributionId);
            return PluginUiCommandResult.Unhandled;
        }

        try
        {
            var result = await plugin.CommandHandler
                .ExecuteUiCommandAsync(command, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "插件 UI 命令执行完成 {PluginId} {ContributionId} {CommandId} {Handled}",
                pluginId,
                command.ContributionId,
                command.CommandId,
                result?.Handled ?? false);
            return result ?? PluginUiCommandResult.Unhandled;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            AddUiDiagnostic(pluginId, command.ContributionId, "COMMAND_FAILURE");
            logger.LogWarning(
                new InvalidOperationException("插件 UI 操作失败，异常内容已隐藏。"),
                "插件 UI 命令执行失败 {PluginId} {CommandId} {ExceptionType}",
                pluginId,
                command.CommandId,
                exception.GetType().Name);
            return PluginUiCommandResult.Unhandled;
        }
    }

    public static PluginRuntime Start(PluginRuntimeOptions options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var runtime = new PluginRuntime(loggerFactory) { options = options };
        options.StartupProgress?.Invoke(PluginRuntimeStartupPhase.Compiling);
        runtime.DiscoverAndLoad(options);
        options.StartupProgress?.Invoke(PluginRuntimeStartupPhase.Completed);
        runtime.ApplyOrdering(options);
        runtime.ApplyDisabled(options);
        runtime.diagnostics.AddRange(runtime.registry.Diagnostics);
        runtime.registry = runtime.BuildSnapshot(runtime.plugins);
        runtime.LogSummary();
        return runtime;
    }

    /// <summary>
    /// 启用/禁用插件。默认把其全部 Entry 同步启停；实现
    /// <see cref="IPluginEnableStateAware"/> 的插件改为只回调其自身状态，
    /// 由插件决定停用期间各 Extension 的行为（Entry 保持启用可继续运行）。
    /// </summary>
    public bool SetPluginEnabled(string pluginId, bool enabled)
    {
        lock (runtimeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var plugin = plugins.FirstOrDefault(item => item.Manifest.Id == pluginId);
            if (plugin is null)
            {
                logger.LogWarning("插件启停被忽略：未找到插件 {PluginId}", pluginId);
                return false;
            }
            plugin.Enabled = enabled;
            if (plugin.Instance is IPluginEnableStateAware stateAware)
            {
                // 语义停用保持 Entry；历史引用恢复能力不随普通禁用释放。
                try { stateAware.OnPluginEnabledChanged(enabled); }
                catch (Exception exception)
                {
                    logger.LogError(new InvalidOperationException("插件启停回调失败，异常内容已隐藏。"),
                        "插件启停回调失败 {PluginId} {Enabled} {ExceptionType}", pluginId, enabled, exception.GetType().Name);
                }
            }
            else foreach (var entry in plugin.Entries) entry.Enabled = enabled;
            registry = BuildSnapshot(plugins);
            logger.LogInformation("插件启停完成 {PluginId} {Enabled}", pluginId, enabled);
            return true;
        }
    }

    /// <summary>启用/禁用单个 Entry，发布新快照，不修改在途租约。</summary>
    public void SetEntryEnabled(string pluginId, string entryId, bool enabled)
    {
        lock (runtimeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var plugin = plugins.FirstOrDefault(item => item.Manifest.Id == pluginId);
            var entry = plugin?.Entries.FirstOrDefault(item => item.Extension.ExtensionId == entryId);
            if (plugin is null || entry is null) return;
            entry.Enabled = enabled && plugin.Enabled;
            registry = BuildSnapshot(plugins);
            logger.LogInformation("Pipeline Entry 启停完成 {PluginId} {EntryId} {Enabled}", pluginId, entryId, enabled);
        }
    }
    private void DiscoverAndLoad(PluginRuntimeOptions options)
    {
        var discoverStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var discovered = PluginCatalog.Discover(options.PluginDirectory, diagnostics);
        logger.LogInformation("插件发现完成 {PluginCount} {ElapsedMs}", discovered.Count,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(discoverStartedAt).TotalMilliseconds);
        if (discovered.Count > 0)
            options.StartupProgress?.Invoke(PluginRuntimeStartupPhase.Loading);
        // 仅并行化源码编译（相互独立且无副作用）；验证与清单漂移检查必须紧邻各自加载回合执行，
        // 保证「能力协商、入口与注册使用同一份已验证清单」的既有防御语义不变。
        var (compilations, compilationErrors) = CompileSourcePlugins(discovered, options);
        foreach (var package in discovered)
        {
            try
            {
                compilationErrors.TryGetValue(package.Manifest.Id, out var compilationError);
                LoadPlugin(package, options, compilation: compilations.GetValueOrDefault(package.Manifest.Id), compilationError: compilationError);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"插件 {package.Manifest.Id} 加载失败：{exception.GetType().Name}");
                logger.LogError(new InvalidOperationException("插件加载失败，异常内容已隐藏。"),
                    "插件加载失败 {PluginId} {ExceptionType}", package.Manifest.Id, exception.GetType().Name);
            }
        }
    }

    /// <summary>
    /// 并行编译上限：源码插件相互独立（编译只读包内输入、只写自身缓存代际），
    /// 并行度随处理器核数但封顶 <see cref="MaxCompileParallelism"/>；诊断仍由各插件加载回合按发现顺序插入，
    /// 加载与 Pipeline 顺序保持既有确定性。
    /// </summary>
    internal const int MaxCompileParallelism = 4;

    private (IReadOnlyDictionary<string, SourcePluginCompilationResult> Results, IReadOnlyDictionary<string, Exception> Errors)
        CompileSourcePlugins(IReadOnlyList<DiscoveredPlugin> discovered, PluginRuntimeOptions options)
    {
        var sourcePackages = discovered.Where(item => item.Manifest.Runtime?.Kind == "csharp-source").ToArray();
        if (sourcePackages.Length == 0) return (new Dictionary<string, SourcePluginCompilationResult>(), new Dictionary<string, Exception>());
        var maxParallelism = options.CompileParallelism > 0
            ? options.CompileParallelism
            : Math.Clamp(Environment.ProcessorCount, 1, MaxCompileParallelism);
        var compiler = options.SourceCompiler ?? new SourcePluginCompiler();
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, SourcePluginCompilationResult>();
        var errors = new System.Collections.Concurrent.ConcurrentDictionary<string, Exception>();
        var compileStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        Parallel.ForEachAsync(sourcePackages, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
            async (package, token) =>
            {
                try { results[package.Manifest.Id] = await compiler.CompileAsync(package.Directory, package.Manifest, token).ConfigureAwait(false); }
                catch (Exception exception) { errors[package.Manifest.Id] = exception; }
            }).ConfigureAwait(false).GetAwaiter().GetResult();
        logger.LogInformation("源码插件并行编译完成 {PluginCount} {MaxParallelism} {ElapsedMs}", sourcePackages.Length, maxParallelism,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(compileStartedAt).TotalMilliseconds);
        return (results, errors);
    }

    private LoadedPlugin? LoadPlugin(DiscoveredPlugin package, PluginRuntimeOptions options, bool candidateOnly = false,
        SourcePluginCompilationResult? compilation = null, Exception? compilationError = null)
    {
        var loadStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var targetRegistry = candidateOnly ? new PipelineRegistry() : registry;
        var manifest = package.Manifest;
        var capabilityErrors = PluginCapabilityCatalog.Validate(manifest.Id, manifest.HostCapabilities);
        if (capabilityErrors.Count > 0)
        {
            lock (diagnosticsGate)
            {
                structuredDiagnostics.AddRange(capabilityErrors);
                diagnostics.Add($"插件 {manifest.Id} 被拒绝：{PluginDiagnosticCodes.CapabilityNotSupported}。");
            }
            logger.LogWarning("插件宿主能力不兼容 {PluginId} {DiagnosticCode}", manifest.Id, PluginDiagnosticCodes.CapabilityNotSupported);
            return null;
        }

        var validationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var validation = PluginPackageValidation.ValidateAsync(package.Directory).ConfigureAwait(false).GetAwaiter().GetResult();
        var validationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(validationStartedAt).TotalMilliseconds;
        if (!validation.Success)
        {
            lock (diagnosticsGate) structuredDiagnostics.AddRange(validation.Diagnostics);
            return null;
        }
        // 能力协商、入口与注册必须使用同一份已验证清单，不能混用发现时的旧快照。
        if (validation.Manifest is null
            || System.Text.Json.JsonSerializer.Serialize(validation.Manifest) != System.Text.Json.JsonSerializer.Serialize(manifest))
        {
            AddLoadDiagnostic(manifest.Id, "LX_MANIFEST_CHANGED", "插件清单在发现后发生变化，本次加载已拒绝。");
            return null;
        }
        string assemblyPath;
        if (manifest.Runtime?.Kind == "csharp-source")
        {
            // 启动期使用并行预编译结果；失败异常在加载回合重抛，与串行路径的降级行为一致。
            // 候选重载等未预热路径仍在加载回合内串行编译。
            if (compilationError is { } failure) throw failure;
            var result = compilation ?? (options.SourceCompiler ?? new SourcePluginCompiler())
                .CompileAsync(package.Directory, manifest).ConfigureAwait(false).GetAwaiter().GetResult();
            lock (diagnosticsGate) structuredDiagnostics.AddRange(result.Diagnostics);
            if (!result.Success || result.AssemblyPath is null) return null;
            assemblyPath = result.AssemblyPath;
        }
        else
        {
            try { assemblyPath = SourcePluginPaths.Resolve(package.Directory, manifest.Assembly); }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                AddLoadDiagnostic(manifest.Id, PluginDiagnosticCodes.InvalidPath, "插件程序集路径非法。");
                return null;
            }
            if (!File.Exists(assemblyPath))
            {
                AddLoadDiagnostic(manifest.Id, PluginDiagnosticCodes.InvalidEntry, "插件程序集不存在。");
                return null;
            }
        }
        PluginLoadContext? loadContext = null;
        ILoomXPlugin? instance = null;
        PluginBridgeScope? bridge = null;
        IPluginUiContributionProvider? uiProvider = null;
        EventHandler? uiInvalidatedHandler = null;
        var accepted = false;
        var failureStage = PluginDiagnosticStage.Load;
        try
        {
            loadContext = new PluginLoadContext(assemblyPath, $"plugin:{manifest.Id}");
            var dataDirectory = Path.Combine(options.DataRootDirectory, manifest.Id);
            System.IO.Directory.CreateDirectory(dataDirectory);
            bridge = options.BridgeFactory?.Invoke(manifest, dataDirectory)
                ?? new PluginBridgeScope(manifest.Id, manifest.HostCapabilities);
            if (!string.Equals(bridge.PluginId, manifest.Id, StringComparison.Ordinal)
                || !new HashSet<string>(manifest.HostCapabilities, StringComparer.Ordinal).SetEquals(bridge.DeclaredCapabilities))
                throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "宿主能力作用域身份不匹配。");
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var pluginType = assembly.GetType(manifest.PluginType, throwOnError: false);
            if (pluginType is null || !typeof(ILoomXPlugin).IsAssignableFrom(pluginType))
            {
                AddLoadDiagnostic(manifest.Id, PluginDiagnosticCodes.InvalidEntry, "插件入口未实现 ILoomXPlugin。");
                return null;
            }
            instance = Activator.CreateInstance(pluginType) as ILoomXPlugin;
            if (instance is null || instance.Id != manifest.Id)
            {
                AddLoadDiagnostic(manifest.Id, PluginDiagnosticCodes.InvalidEntry, "插件入口实例与 Manifest 不一致。");
                return null;
            }

            failureStage = PluginDiagnosticStage.Initialize;
            instance.Initialize(new PluginInitializationContext(manifest.Id, dataDirectory) { Bridge = bridge });
            failureStage = PluginDiagnosticStage.Load;

            var extensions = instance.CreateExtensions().ToArray();
            var manifestExtensions = manifest.Extensions.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var entries = new List<PipelineEntry>();
            var validated = new List<(IPipelineExtension Extension, PluginManifestExtension Declared)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var invalidExtensions = false;
            foreach (var extension in extensions)
            {
                if (extension is null || !seen.Add(extension.ExtensionId)
                    || !manifestExtensions.TryGetValue(extension.ExtensionId, out var declared)
                    || declared.Kind != extension.Kind || declared.FailurePolicy != extension.FailurePolicy)
                {
                    AddLoadDiagnostic(manifest.Id, "LX_EXTENSION_INVALID", "插件扩展重复、未声明或与清单契约不一致。");
                    invalidExtensions = true;
                    continue;
                }
                validated.Add((extension, declared));
            }
            if (manifestExtensions.Keys.Any(id => !seen.Contains(id)))
            {
                AddLoadDiagnostic(manifest.Id, "LX_EXTENSION_INVALID", "插件缺少清单声明的扩展。");
                invalidExtensions = true;
            }
            // 启动保留既有降级行为；重载必须整体拒绝残缺候选，不退休旧保护代际。
            if (candidateOnly && invalidExtensions) return null;

            uiProvider = instance as IPluginUiContributionProvider;
            // 重载只发布完整候选；启动阶段仍保留既有 UI 降级行为。
            if (candidateOnly && !ValidateCandidateUi(manifest, uiProvider))
            {
                AddLoadDiagnostic(manifest.Id, "LX_UI_INVALID", "候选声明式 UI 未通过生产结构验证。");
                return null;
            }
            if (manifest.Ui is { Contributions.Count: > 0 } && uiProvider is null)
                AddUiDiagnostic(manifest.Id, "provider", "PROVIDER_MISSING");
            if (uiProvider is not null)
            {
                uiInvalidatedHandler = (_, _) => NotifyUiInvalidated(manifest.Id);
                uiProvider.UiInvalidated += uiInvalidatedHandler;
            }
            var plugin = new LoadedPlugin
            {
                Manifest = manifest, Instance = instance, LoadContext = loadContext, Bridge = bridge,
                Entries = entries, UiProvider = uiProvider, CommandHandler = instance as IPluginUiCommandHandler,
                UiInvalidatedHandler = uiInvalidatedHandler,
            };
            foreach (var (extension, declared) in validated)
            {
                var pipeline = targetRegistry.GetOrCreatePipeline(declared.Pipeline, declared.Kind, loggerFactory.CreateLogger<Pipeline>());
                var entry = new PipelineEntry(extension, manifest.Id);
                if (targetRegistry.Register(pipeline, entry)) entries.Add(entry);
                else
                {
                    AddLoadDiagnostic(manifest.Id, "LX_EXTENSION_INVALID", "插件扩展注册失败。");
                    if (candidateOnly) return null;
                }
            }
            if (!candidateOnly) plugins.Add(plugin);
            accepted = true;
            if (!candidateOnly)
                logger.LogInformation("插件加载完成 {PluginId} {ValidationMs} {ElapsedMs}", manifest.Id, validationMs,
                    (long)System.Diagnostics.Stopwatch.GetElapsedTime(loadStartedAt).TotalMilliseconds);
            return plugin;
        }
        catch (Exception exception)
        {
            if (exception is PluginBridgeException { Error: PluginBridgeError.CapabilityNotSupported })
                AddLoadDiagnostic(manifest.Id, PluginDiagnosticCodes.CapabilityNotSupported, "宿主未提供插件必需能力的实现。", PluginDiagnosticStage.Capability);
            else if (failureStage == PluginDiagnosticStage.Initialize)
                AddLoadDiagnostic(manifest.Id, PluginDiagnosticCodes.InitializationFailed, "插件初始化失败，异常内容已隐藏。", PluginDiagnosticStage.Initialize);
            else
                AddLoadDiagnostic(manifest.Id, "LX_LOAD_FAILED", "插件入口加载或扩展注册失败，异常内容已隐藏。");
            throw;
        }
        finally
        {
            if (!accepted)
            {
                targetRegistry.RemovePluginEntries(manifest.Id);
                if (uiProvider is not null && uiInvalidatedHandler is not null)
                {
                    try { uiProvider.UiInvalidated -= uiInvalidatedHandler; }
                    catch (Exception exception)
                    {
                        logger.LogWarning(new InvalidOperationException("候选插件 UI 订阅清理失败，异常内容已隐藏。"),
                            "候选插件 UI 订阅清理异常 {PluginId} {ExceptionType}", manifest.Id, exception.GetType().Name);
                    }
                }
                try
                {
                    if (instance is IAsyncDisposable asyncDisposable) asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    else if (instance is IDisposable disposable) disposable.Dispose();
                }
                catch (Exception exception)
                {
                    LogCandidateCleanupFailure(manifest.Id, exception);
                }
                try { bridge?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception exception) { LogCandidateCleanupFailure(manifest.Id, exception); }
                try { loadContext?.Unload(); }
                catch (Exception exception) { LogCandidateCleanupFailure(manifest.Id, exception); }
            }
        }
    }
    private static bool ValidateCandidateUi(PluginManifest manifest, IPluginUiContributionProvider? provider)
    {
        if (manifest.Ui is not { Contributions.Count: > 0 }) return true;
        if (provider is null) return false;
        var declarations = manifest.Ui.Contributions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        // 与产品四语言负载一致，避免只在非默认文化出现的无效快照被发布。
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
        {
            var contributions = provider.GetUiContributions(new PluginUiContext(culture)) ?? [];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contribution in contributions)
            {
                if (contribution is null || !seen.Add(contribution.Id)
                    || !declarations.TryGetValue(contribution.Id, out var declared)
                    || declared.Slot != contribution.Slot
                    || !PluginUiValidator.Validate(contribution).IsValid)
                    return false;
            }
            if (declarations.Keys.Any(id => !seen.Contains(id))) return false;
        }
        return true;
    }

    private void LogCandidateCleanupFailure(string pluginId, Exception exception)
    {
        logger.LogWarning(new InvalidOperationException("候选插件资源清理失败，异常内容已隐藏。"),
            "候选插件资源清理异常 {PluginId} {ExceptionType}", pluginId, exception.GetType().Name);
    }

    private void NotifyUiInvalidated(string pluginId)
    {
        var handlers = PluginUiInvalidated;
        if (handlers is null) return;
        foreach (EventHandler<PluginUiInvalidatedEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, new(pluginId)); }
            catch (Exception exception)
            {
                logger.LogWarning(new InvalidOperationException("插件 UI 观察者失败，异常内容已隐藏。"),
                    "插件 UI 观察者通知异常 {PluginId} {ExceptionType}", pluginId, exception.GetType().Name);
            }
        }
    }

    private void AddLoadDiagnostic(string pluginId, string code, string message, PluginDiagnosticStage stage = PluginDiagnosticStage.Load)
    {
        lock (diagnosticsGate)
        {
            structuredDiagnostics.Add(new(pluginId, stage, PluginDiagnosticSeverity.Error, code, message));
            diagnostics.Add($"插件 {pluginId} 被拒绝：{code}。");
        }
    }
    private static bool HasUiSlot(PluginManifest manifest, PluginUiSlot slot) =>
        manifest.Ui?.Contributions.Any(item => item.Slot == slot) == true;

    private void AddUiDiagnostic(string pluginId, string contributionId, string reasonCode)
    {
        var key = $"{pluginId}|{contributionId}|{reasonCode}";
        lock (diagnosticsGate)
        {
            if (!uiDiagnosticKeys.Add(key)) return;
            diagnostics.Add($"插件 {pluginId} UI Contribution {contributionId} 已忽略：{reasonCode}");
        }
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
