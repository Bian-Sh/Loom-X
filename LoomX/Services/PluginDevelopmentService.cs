using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Services;

/// <summary>主会话指定开发根；不隐式使用安装目录或真实插件数据根。</summary>
public sealed record PluginDevelopmentOptions(string DevelopmentRoot);

/// <summary>由组合根绑定生产验证与路径入口；应用服务不持有另一套验证规则。</summary>
public sealed record PluginDevelopmentValidation(
    Func<string, CancellationToken, Task<PluginDevelopmentValidationResult>> ValidateAsync,
    Func<string, string, string> ResolvePackageDirectory,
    Func<string, string, CancellationToken, Task<byte[]>> ReadPackageFileAsync);

public sealed record PluginDevelopmentValidationResult(
    bool Success, PluginManifest? Manifest, IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>工具只返回安全摘要，不返回源码、绝对缓存路径、测试正文或异常原值。</summary>
public sealed record PluginDevelopmentResult(
    bool Success, string Operation, string? PluginId, IReadOnlyList<PluginDiagnostic> Diagnostics,
    string PluginApiVersion = "1", string ManifestVersion = "runtime-v1",
    IReadOnlyList<string>? HostCapabilities = null, string? CacheKey = null, bool? CacheHit = null,
    string? ArtifactStatus = null, int? TestsPassed = null, string? Isolation = null);

public sealed record PluginDevelopmentTestCase(string Pipeline, string Payload, string Expected,
    PipelineOutcome Outcome = PipelineOutcome.Passed);

/// <summary>源码开发操作共享入口；生产运行时与测试临时运行时严格分开。</summary>
public sealed class PluginDevelopmentService
{
    public const string TestIsolation = "trusted-in-process-temporary-data-not-os-sandbox";
    public static int PluginApiMajor => typeof(ILoomXPlugin).Assembly.GetName().Version?.Major ?? 1;
    private readonly PluginDevelopmentOptions options;
    private readonly SourcePluginCompiler compiler;
    private readonly PluginRuntime runtime;
    private readonly PluginDevelopmentValidation validation;
    private readonly ILogger<PluginDevelopmentService> logger;
    private readonly ConcurrentDictionary<string, PluginDevelopmentResult> latest = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim createGate = new(1, 1);

    public PluginDevelopmentService(PluginDevelopmentOptions options, SourcePluginCompiler compiler,
        PluginRuntime runtime, ILoggerFactory loggerFactory, PluginDevelopmentValidation validation)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DevelopmentRoot);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(validation);
        this.options = options with { DevelopmentRoot = Path.GetFullPath(options.DevelopmentRoot) };
        this.compiler = compiler;
        this.runtime = runtime;
        this.validation = validation;
        logger = loggerFactory.CreateLogger<PluginDevelopmentService>();
    }

    public Task<PluginDevelopmentResult> CreateAsync(string pluginId, string entryType = "Development.Plugin", int pluginApiMajor = 1, CancellationToken cancellationToken = default) =>
        GuardAsync("create", pluginId, pluginApiMajor, cancellationToken, async directory =>
        {
            if (!ValidEntry(entryType)) return Failure("create", pluginId, "LX_ENTRY_INVALID", "入口类型必须是有效的 C# 类型全名。", relative: "entry_type");
            var manifest = TemplateManifest(pluginId, entryType);
            if (ManifestParser.Parse(manifest, new List<string>()) is null)
                return Failure("create", pluginId, PluginDiagnosticCodes.InvalidPath, "插件标识不符合生产清单规则。", relative: "plugin_id");
            await createGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Directory.Exists(directory) || File.Exists(directory)) return Failure("create", pluginId, "LX_PACKAGE_EXISTS", "已有插件包不得覆盖。");
                Directory.CreateDirectory(directory);
                // 创建后再次调用生产路径入口；所有文件使用 CreateNew，不覆盖竞态中出现的文件。
                directory = validation.ResolvePackageDirectory(options.DevelopmentRoot, pluginId);
                foreach (var file in PluginDevelopmentTemplate.Files(pluginId, entryType, manifest))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = TemplatePath(directory, file.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    path = TemplatePath(directory, file.Key);
                    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(file.Value), cancellationToken).ConfigureAwait(false);
                }
                return Result("create", pluginId, [], artifactStatus: "created");
            }
            finally { createGate.Release(); }
        });

    public Task<PluginDevelopmentResult> ValidateAsync(string pluginId, int pluginApiMajor = 1, CancellationToken cancellationToken = default) =>
        GuardAsync("validate", pluginId, pluginApiMajor, cancellationToken, async directory =>
        {
            var result = await ReadValidatedAsync(directory, pluginId, cancellationToken).ConfigureAwait(false);
            return Result("validate", pluginId, result.Diagnostics, result.Manifest?.HostCapabilities);
        });

    public Task<PluginDevelopmentResult> CompileAsync(string pluginId, int pluginApiMajor = 1, CancellationToken cancellationToken = default) =>
        GuardAsync("compile", pluginId, pluginApiMajor, cancellationToken, async directory =>
        {
            var input = await ReadValidatedAsync(directory, pluginId, cancellationToken).ConfigureAwait(false);
            if (!input.Success || input.Manifest is null) return Result("compile", pluginId, input.Diagnostics);
            if (input.Manifest.Runtime?.Kind != "csharp-source") return Failure("compile", pluginId, "LX_SOURCE_RUNTIME_REQUIRED", "此操作要求 csharp-source 加载入口。");
            var compilation = await compiler.CompileAsync(directory, input.Manifest, cancellationToken).ConfigureAwait(false);
            return Result("compile", pluginId, input.Diagnostics.Concat(compilation.Diagnostics), input.Manifest.HostCapabilities,
                compilation.CacheKey, compilation.CacheHit, compilation.Success ? "compiled" : "failed");
        });

    public Task<PluginDevelopmentResult> DiagnosticsAsync(string pluginId, int pluginApiMajor = 1, CancellationToken cancellationToken = default) =>
        GuardAsync("diagnostics", pluginId, pluginApiMajor, cancellationToken, async directory =>
        {
            if (latest.TryGetValue(pluginId, out var result)) return result with { Operation = "diagnostics" };
            var input = await ReadValidatedAsync(directory, pluginId, cancellationToken).ConfigureAwait(false);
            return Result("diagnostics", pluginId, input.Diagnostics, input.Manifest?.HostCapabilities);
        });

    public Task<PluginDevelopmentResult> TestAsync(string pluginId, bool trustCode = false, IReadOnlyList<PluginDevelopmentTestCase>? cases = null, int pluginApiMajor = 1, CancellationToken cancellationToken = default) =>
        GuardAsync("test", pluginId, pluginApiMajor, cancellationToken, async directory =>
        {
            if (!trustCode) return TrustRequired("test", pluginId);
            if (cases?.Count > 100 || cases?.Any(item => item is null || string.IsNullOrWhiteSpace(item.Pipeline) || item.Payload is null || item.Expected is null || !Enum.IsDefined(item.Outcome)) == true)
                return Failure("test", pluginId, "LX_TEST_CASE_INVALID", "测试用例不符合受控格式。", PluginDiagnosticStage.Test);
            var input = await ReadValidatedAsync(directory, pluginId, cancellationToken).ConfigureAwait(false);
            if (!input.Success || input.Manifest is null) return Result("test", pluginId, input.Diagnostics);
            if (input.Manifest.Runtime?.Kind != "csharp-source") return Failure("test", pluginId, "LX_SOURCE_RUNTIME_REQUIRED", "此操作要求 csharp-source 加载入口。", PluginDiagnosticStage.Test);
            return await TestPackageAsync(directory, input.Manifest, input.Diagnostics.ToList(), cases ?? [], cancellationToken).ConfigureAwait(false);
        });

    public Task<PluginDevelopmentResult> ReloadAsync(string pluginId, bool trustCode = false, int pluginApiMajor = 1, CancellationToken cancellationToken = default) =>
        GuardAsync("reload", pluginId, pluginApiMajor, cancellationToken, async _ =>
        {
            if (!trustCode) return TrustRequired("reload", pluginId);
            if (!runtime.PluginInfos.Any(item => item.Id == pluginId))
                return Failure("reload", pluginId, "LX_PLUGIN_NOT_INSTALLED", "reload 只操作已安装实例，不隐式安装。", PluginDiagnosticStage.Reload);
            var count = runtime.StructuredDiagnostics.Count;
            // 不用开发包路径替换安装路径：生产 Runtime 自己完成验证、编译、候选发布和租约退休。
            var success = await runtime.ReloadAsync(pluginId, cancellationToken).ConfigureAwait(false);
            var diagnostics = runtime.StructuredDiagnostics.Skip(count).Where(item => item.PluginId == pluginId).ToList();
            if (!success && diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error))
                diagnostics.Add(Diagnostic(pluginId, PluginDiagnosticStage.Reload, PluginDiagnosticCodes.ReloadFailed, "候选未生效，旧实例保留。"));
            return Result("reload", pluginId, diagnostics, artifactStatus: success ? "reloaded" : "previous-instance-retained");
        });

    private async Task<PluginDevelopmentValidationResult> ReadValidatedAsync(string directory, string id, CancellationToken ct)
    {
        var result = await validation.ValidateAsync(directory, ct).ConfigureAwait(false);
        var diagnostics = result.Diagnostics.ToList();
        if (result.Manifest is { } manifest && manifest.Id != id)
            diagnostics.Add(Diagnostic(id, PluginDiagnosticStage.Manifest, PluginDiagnosticCodes.InvalidManifest, "清单身份与开发目录不一致。"));
        if (!result.Success && diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error) || result.Manifest is null)
            diagnostics.Add(Diagnostic(id, PluginDiagnosticStage.Manifest, PluginDiagnosticCodes.InvalidManifest, "生产验证未得到有效清单。"));
        return new(diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error), result.Manifest, diagnostics);
    }

    private async Task<PluginDevelopmentResult> TestPackageAsync(string directory, PluginManifest manifest,
        List<PluginDiagnostic> diagnostics, IReadOnlyList<PluginDevelopmentTestCase> cases, CancellationToken ct)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "LoomX-development-test-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(testRoot, "plugins");
        var package = Path.Combine(pluginRoot, manifest.Id);
        var dataRoot = Path.Combine(testRoot, "data");
        Directory.CreateDirectory(package);
        try
        {
            var files = new[] { ManifestParser.ManifestFileName }
                .Concat(manifest.Runtime!.Sources!)
                .Concat(new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" }.Select(culture => $"i18n/{culture}.json"));
            foreach (var relative in files)
            {
                // 生产 ReadAsync 先检查并读取路径，之后才生成本次临时副本。
                var bytes = await validation.ReadPackageFileAsync(directory, relative, ct).ConfigureAwait(false);
                var destination = Path.Combine(package, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, bytes, ct).ConfigureAwait(false);
            }
            var compiled = await compiler.CompileAsync(package, manifest, ct).ConfigureAwait(false);
            diagnostics.AddRange(compiled.Diagnostics);
            if (!compiled.Success || compiled.AssemblyPath is null) return Result("test", manifest.Id, diagnostics);
            // 本测试没有 Provider 客户端/核心配置服务。日志和 Toast 也不连接桌面生产实例。
            var bridgeFactory = new PluginHostBridgeFactory(dataRoot, new ToastService(), NullLoggerFactory.Instance);
            await using var testRuntime = PluginRuntime.Start(new PluginRuntimeOptions
            {
                PluginDirectory = pluginRoot, DataRootDirectory = dataRoot, SourceCompiler = compiler,
                BridgeFactory = bridgeFactory.CreateScope,
            }, NullLoggerFactory.Instance);
            diagnostics.AddRange(testRuntime.StructuredDiagnostics);
            var info = testRuntime.PluginInfos.SingleOrDefault(item => item.Id == manifest.Id);
            if (info is null || info.ExtensionCount != manifest.Extensions.Count)
                diagnostics.Add(Diagnostic(manifest.Id, PluginDiagnosticStage.Test, "LX_TEST_REGISTRATION_FAILED", "入口或 Extension 注册与清单不一致。"));
            if (info is not null)
            {
                foreach (var group in (manifest.Ui?.Contributions ?? []).GroupBy(item => item.Slot))
                {
                    var contributions = testRuntime.GetUiContributions(manifest.Id, group.Key, "zh-CN");
                    if (!group.Select(item => item.Id).Order(StringComparer.Ordinal).SequenceEqual(contributions.Select(item => item.Id).Order(StringComparer.Ordinal)))
                        diagnostics.Add(Diagnostic(manifest.Id, PluginDiagnosticStage.Test, "LX_TEST_UI_FAILED", "声明式 UI 未通过生产结构验证。"));
                }
            }
            if (testRuntime.Diagnostics.Count > 0)
                diagnostics.Add(Diagnostic(manifest.Id, PluginDiagnosticStage.Test, "LX_TEST_RUNTIME_DIAGNOSTIC", "生产 Runtime 忽略了未声明或无效贡献；测试不能视为通过。"));
            var passed = 0;
            if (diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error))
            {
                await using var lease = testRuntime.AcquireLease();
                foreach (var test in cases)
                {
                    ct.ThrowIfCancellationRequested();
                    var pipeline = lease.GetPipeline(test.Pipeline);
                    if (pipeline is null)
                    {
                        diagnostics.Add(Diagnostic(manifest.Id, PluginDiagnosticStage.Test, "LX_TEST_PIPELINE_MISSING", "用例引用了未注册 Pipeline。"));
                        continue;
                    }
                    var actual = await pipeline.ExecuteAsync(test.Payload, ct).ConfigureAwait(false);
                    if (actual.Outcome != test.Outcome || actual.Payload != test.Expected)
                        diagnostics.Add(Diagnostic(manifest.Id, PluginDiagnosticStage.Test, "LX_TEST_ASSERTION_FAILED", "Pipeline 结果与用例预期不一致，正文已隐藏。"));
                    else passed++;
                }
                passed += await RunSelfTestAsync(manifest, compiled.AssemblyPath, dataRoot, bridgeFactory, ct).ConfigureAwait(false);
            }
            return Result("test", manifest.Id, diagnostics, manifest.HostCapabilities, testsPassed: passed, isolation: TestIsolation);
        }
        finally
        {
            // 仅删除本次明确创建的、位于临时目录内的绝对根；不清理任何共享工作树产物。
            var absolute = Path.GetFullPath(testRoot);
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("LoomX-development-test-", StringComparison.Ordinal))
            {
                try { Directory.Delete(absolute, true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { logger.LogWarning("插件测试临时资源释放降级 {ErrorType}", exception.GetType().Name); }
            }
        }
    }

    private static async Task<int> RunSelfTestAsync(PluginManifest manifest, string assemblyPath, string dataRoot,
        PluginHostBridgeFactory factory, CancellationToken ct)
    {
        var load = new PluginLoadContext(assemblyPath, "development-self-test");
        try
        {
            var assembly = load.LoadFromAssemblyPath(assemblyPath);
            var entry = manifest.Runtime!.EntryType;
            var last = entry.LastIndexOf('.');
            var testType = assembly.GetType((last < 0 ? "" : entry[..(last + 1)]) + "PluginTests", false);
            if (testType is null) return 0;
            var method = testType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, [typeof(PluginInitializationContext), typeof(CancellationToken)]);
            if (method is null || method.ReturnType != typeof(ValueTask)) throw new InvalidDataException("自测试签名无效。");
            var data = Path.Combine(dataRoot, manifest.Id);
            await using var bridge = factory.CreateScope(manifest, data);
            var context = new PluginInitializationContext(manifest.Id, data) { Bridge = bridge };
            try { await ((ValueTask)method.Invoke(null, [context, ct])!).ConfigureAwait(false); }
            catch (TargetInvocationException exception) when (exception.InnerException is OperationCanceledException)
            { throw new OperationCanceledException(ct); }
            return 1;
        }
        finally { load.Unload(); }
    }

    private async Task<PluginDevelopmentResult> GuardAsync(string operation, string id, int major, CancellationToken ct, Func<string, Task<PluginDevelopmentResult>> action)
    {
        ct.ThrowIfCancellationRequested();
        if (major != PluginApiMajor) return Failure(operation, null, "LX_PLUGIN_API_INCOMPATIBLE", "Skill 与宿主 Plugin API 主版本不兼容；停止，不猜测契约。");
        string directory;
        try { directory = validation.ResolvePackageDirectory(options.DevelopmentRoot, id); }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or PluginBridgeException)
        { return Failure(operation, null, PluginDiagnosticCodes.InvalidPath, "插件标识或开发包路径非法。", relative: "plugin_id"); }
        logger.LogInformation("插件开发操作开始 {Operation} {PluginId}", operation, id);
        PluginDevelopmentResult result;
        try { result = await action(directory).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // 原始异常可能包含源码/正文；日志仍保留异常参数与安全的事件定位字段。
            logger.LogError(new InvalidOperationException("插件开发操作异常，原值已隐藏。"), "插件开发操作失败 {Operation} {PluginId} {ErrorType}", operation, id, exception.GetType().Name);
            result = Failure(operation, id, operation == "test" ? "LX_TEST_FAILED" : "LX_DEVELOPMENT_FAILED", "开发操作失败；异常原值已隐藏。", operation == "test" ? PluginDiagnosticStage.Test : PluginDiagnosticStage.Manifest);
        }
        result = result with { PluginApiVersion = PluginApiMajor.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        latest[id] = result;
        if (result.Success) logger.LogInformation("插件开发操作完成 {Operation} {PluginId} {DiagnosticCount}", operation, id, result.Diagnostics.Count);
        else logger.LogWarning("插件开发操作未完成 {Operation} {PluginId} {DiagnosticCount}", operation, id, result.Diagnostics.Count);
        return result;
    }

    private string TemplatePath(string directory, string relative)
    {
        // 模板路径是固定常量；每一段仍交给生产入口解析，不另写路径规则。
        foreach (var segment in relative.Split('/')) directory = validation.ResolvePackageDirectory(directory, segment);
        return directory;
    }
    private static bool ValidEntry(string entry) => !string.IsNullOrWhiteSpace(entry) && entry.Split('.').All(part =>
        SyntaxFacts.IsValidIdentifier(part) && SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None);

    private static PluginDevelopmentResult TrustRequired(string operation, string id) =>
        Failure(operation, id, "LX_TRUST_REQUIRED", "必须先确认执行当前用户权限的可信进程内 C#；临时 Bridge/数据隔离不是 OS 沙箱。", operation == "test" ? PluginDiagnosticStage.Test : PluginDiagnosticStage.Reload);

    private static PluginDevelopmentResult Failure(string operation, string? id, string code, string message,
        PluginDiagnosticStage stage = PluginDiagnosticStage.Manifest, string? relative = null) =>
        new(false, operation, id, [Diagnostic(id ?? "", stage, code, message, relative)]);

    private static PluginDiagnostic Diagnostic(string id, PluginDiagnosticStage stage, string code, string message, string? relative = null) =>
        new(id, stage, PluginDiagnosticSeverity.Error, code, message, relative);

    private static PluginDevelopmentResult Result(string operation, string id, IEnumerable<PluginDiagnostic> diagnostics,
        IReadOnlyList<string>? hostCapabilities = null, string? cacheKey = null, bool? cacheHit = null,
        string? artifactStatus = null, int? testsPassed = null, string? isolation = null)
    {
        // 不相信编译器、运行时或测试异常中的原始消息；代码、相对位置保留，消息由阶段控制。
        var safe = diagnostics.Select(item => item with { Message = item.Stage switch
        {
            PluginDiagnosticStage.Compile => "源码编译诊断，请检查标记位置。",
            PluginDiagnosticStage.SourceRead => "源码或本地化负载未通过生产输入验证。",
            PluginDiagnosticStage.Capability => "宿主能力声明不兼容。",
            PluginDiagnosticStage.Test => "受控测试未通过；原始正文与异常已隐藏。",
            PluginDiagnosticStage.Reload => "候选未通过生产 reload，旧实例保留。",
            _ => "插件未通过对应阶段验证；原始数据已隐藏。",
        } }).ToArray();
        return new(safe.All(item => item.Severity != PluginDiagnosticSeverity.Error), operation, id, safe,
            HostCapabilities: hostCapabilities?.Where(capability => PluginCapabilityCatalog.Supported.Contains(capability, StringComparer.Ordinal)).ToArray(), CacheKey: cacheKey, CacheHit: cacheHit, ArtifactStatus: artifactStatus,
            TestsPassed: testsPassed, Isolation: isolation);
    }

    private static string TemplateManifest(string id, string entryType) => JsonSerializer.Serialize(new
    {
        id, version = "0.1.0", runtime = new { kind = "csharp-source", entry_type = entryType, sources = new[] { "src/Plugin.cs", "tests/PluginTests.cs" } },
        host_capabilities = Array.Empty<string>(), capabilities = Array.Empty<string>(), extensions = Array.Empty<string>(),
    }, new JsonSerializerOptions { WriteIndented = true });

}
