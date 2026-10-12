using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using LoomX.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Plugins.Host;

/// <summary>不可变编译快照；加载与 reload 由调用方负责。</summary>
public sealed record SourcePluginCompilationResult(
    bool Success, string? AssemblyPath, string? PdbPath, string? CacheKey,
    bool CacheHit, ImmutableArray<PluginDiagnostic> Diagnostics);

/// <summary>Manifest 驱动的离线源码编译入口；仅接受可信插件，不提供进程安全沙箱。</summary>
public sealed class SourcePluginCompiler
{
    internal const string SafeCompileMessage = "源码编译诊断，请检查标记位置。";
    private readonly string cacheRoot;
    private readonly ILogger<SourcePluginCompiler> logger;
    private readonly Func<SourcePluginReferences> references;
    private readonly SourcePluginCompilerSettings settings;

    public SourcePluginCompiler(string? cacheRoot = null)
        : this(cacheRoot, NullLogger<SourcePluginCompiler>.Instance) { }

    public SourcePluginCompiler(string? cacheRoot, ILogger<SourcePluginCompiler> logger)
        : this(cacheRoot, logger, () => SourcePluginReferences.Bundled.Value, new()) { }

    internal SourcePluginCompiler(string? cacheRoot, ILogger<SourcePluginCompiler> logger,
        Func<SourcePluginReferences> references, SourcePluginCompilerSettings settings)
    {
        this.cacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LoomX", "plugin-cache", "source");
        this.logger = logger;
        this.references = references;
        this.settings = settings;
    }

    public async Task<SourcePluginCompilationResult> CompileAsync(
        string packageDirectory, PluginManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        // 只在输入验证后写事件日志，非法插件标识不进入日志。
        SourcePluginInputs inputs;
        try { inputs = await SourcePluginInputs.ReadAsync(packageDirectory, manifest, cancellationToken).ConfigureAwait(false); }
        catch (SourcePluginInputException exception)
        {
            logger.LogWarning(exception, "源码插件输入验证失败 {Stage} {Code}", exception.Diagnostic.Stage, exception.Diagnostic.Code);
            return new(false, null, null, null, false, [exception.Diagnostic]);
        }
        logger.LogInformation("源码插件编译开始 {PluginId} {SourceCount}", manifest.Id, inputs.Sources.Length);
        SourcePluginReferences referenceSet;
        try { referenceSet = references(); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or BadImageFormatException)
        {
            logger.LogWarning(exception, "源码插件固定引用不可用 {PluginId}", manifest.Id);
            return Failure(manifest.Id, PluginDiagnosticStage.Compile, "LX_REFERENCES_INVALID", "宿主固定编译引用缺失或摘要不匹配。");
        }
        var key = inputs.CacheKey(referenceSet.Digest, CompilerIdentity, settings.Identity);
        var cache = new SourcePluginCache(cacheRoot, manifest.Id, key);
        string? candidate = null;
        try
        {
            // 先隔离并迁移旧根控制文件，避免它们阻塞同名合法插件目录。
            var authenticationKey = await cache.AuthenticationKeyAsync(cancellationToken).ConfigureAwait(false);
            await using var held = await cache.LockAsync(cancellationToken).ConfigureAwait(false);
            var cached = await cache.ReadAsync(authenticationKey, inputs, cancellationToken).ConfigureAwait(false);
            if (cached is { } cacheDiagnostics)
            {
                logger.LogInformation("源码插件编译缓存命中 {PluginId} {CacheKey}", manifest.Id, key);
                return new(true, cache.AssemblyPath, cache.PdbPath, key, true, cacheDiagnostics);
            }
            var cacheWarning = Directory.Exists(cache.DirectoryPath)
                ? ImmutableArray.Create(new PluginDiagnostic(manifest.Id, PluginDiagnosticStage.Cache, PluginDiagnosticSeverity.Warning,
                    "LX_CACHE_INVALID", "编译缓存不完整或认证失败，已重新编译。")) : [];
            if (cacheWarning.Length > 0) logger.LogWarning("源码插件缓存认证失败，重新编译 {PluginId} {CacheKey}", manifest.Id, key);
            candidate = cache.CreateCandidate();
            var (success, diagnostics) = await EmitAsync(candidate, cache, inputs, manifest.Id, referenceSet, cancellationToken).ConfigureAwait(false);
            if (!success)
            {
                logger.LogWarning("源码插件编译失败 {PluginId} {DiagnosticCount}", manifest.Id, diagnostics.Length);
                return new(false, null, null, key, false, cacheWarning.AddRange(diagnostics));
            }
            await cache.SealAsync(candidate, inputs, diagnostics, authenticationKey, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            cache.Publish(candidate);
            candidate = null;
            logger.LogInformation("源码插件编译完成 {PluginId} {CacheKey} {DiagnosticCount}", manifest.Id, key, diagnostics.Length);
            return new(true, cache.AssemblyPath, cache.PdbPath, key, false, cacheWarning.AddRange(diagnostics));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException)
        {
            logger.LogError(exception, "源码插件编译缓存操作失败 {PluginId} {CacheKey} {ErrorType}", manifest.Id, key, exception.GetType().Name);
            return Failure(manifest.Id, PluginDiagnosticStage.Cache, "LX_CACHE_FAILED", "无法安全读写或发布编译缓存。", key);
        }
        finally
        {
            if (candidate is not null)
            {
                try { cache.CleanupCandidate(candidate); }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "源码插件候选缓存清理失败 {PluginId} {CacheKey} {ErrorType}", manifest.Id, key, exception.GetType().Name);
                }
            }
        }
    }

    internal static string CompilerIdentity => typeof(CSharpCompilation).Assembly.FullName + "|"
        + typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId + "|"
        + typeof(Compilation).Assembly.FullName + "|" + typeof(Compilation).Assembly.ManifestModule.ModuleVersionId;

    private async Task<(bool Success, ImmutableArray<PluginDiagnostic> Diagnostics)> EmitAsync(
        string candidate, SourcePluginCache cache, SourcePluginInputs inputs, string id, SourcePluginReferences referenceSet, CancellationToken token)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Parse, SourceCodeKind.Regular);
        var trees = inputs.Sources.Select(source => CSharpSyntaxTree.ParseText(
            SourceText.From(new MemoryStream(source.Bytes), new UTF8Encoding(false, true)), parseOptions, source.RelativePath, token)).ToArray();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: settings.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
            nullableContextOptions: NullableContextOptions.Enable, deterministic: true, allowUnsafe: false,
            concurrentBuild: false, generalDiagnosticOption: settings.WarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default);
        var compilation = CSharpCompilation.Create(cache.AssemblyName, trees, referenceSet.Metadata, options);
        var english = inputs.Localization.Single(item => item.RelativePath == "i18n/en-US.json").Bytes;
        await using var dll = File.Create(SourcePluginPaths.Resolve(candidate, cache.AssemblyRelative));
        await using var pdb = File.Create(SourcePluginPaths.Resolve(candidate, cache.PdbRelative));
        var emit = compilation.Emit(dll, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: cache.AssemblyName + ".pdb"),
            manifestResources: [new ResourceDescription(cache.AssemblyName + ".i18n.en-US.json", () => new MemoryStream(english, writable: false), true)],
            cancellationToken: token);
        // GetLineSpan 使用实际源码位置，故意不使用可受 #line 影响的 GetMappedLineSpan。
        var diagnostics = emit.Diagnostics.Where(item => item.Severity >= DiagnosticSeverity.Warning).Select(item =>
        {
            var span = item.Location.GetLineSpan();
            return new PluginDiagnostic(id, PluginDiagnosticStage.Compile,
                item.Severity == DiagnosticSeverity.Error ? PluginDiagnosticSeverity.Error : PluginDiagnosticSeverity.Warning,
                item.Id, SafeCompileMessage, item.Location.IsInSource ? span.Path : null,
                item.Location.IsInSource ? span.StartLinePosition.Line + 1 : null,
                item.Location.IsInSource ? span.StartLinePosition.Character + 1 : null);
        }).OrderBy(item => item.RelativePath, StringComparer.Ordinal).ThenBy(item => item.Line).ThenBy(item => item.Column)
            .ThenBy(item => item.Code, StringComparer.Ordinal).ToImmutableArray();
        return (emit.Success, diagnostics);
    }

    private static SourcePluginCompilationResult Failure(string id, PluginDiagnosticStage stage, string code, string message, string? key = null)
        => new(false, null, null, key, false, [new(id, stage, PluginDiagnosticSeverity.Error, code, message)]);
}

/// <summary>固定默认选项；内部变体只用于验证实际生效选项参与缓存身份。</summary>
internal sealed record SourcePluginCompilerSettings(bool Optimize = true, bool WarningsAsErrors = false)
{
    internal string Identity => $"v1|CSharp14|regular|documentation=parse|dll|nullable=enable|deterministic=true|unsafe=false|concurrent=false|portable-pdb|optimize={Optimize}|warnings-as-errors={WarningsAsErrors}";
}
