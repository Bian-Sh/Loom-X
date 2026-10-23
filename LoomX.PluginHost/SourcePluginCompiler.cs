using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
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
    /// <summary>宿主安装镜像内的预编译种子根（tasks 3.1）；密钥文件与代际布局由发布期工具写入。</summary>
    internal const string SeedKeyRelative = ".cache..seed-key";
    private readonly string cacheRoot;
    private readonly string? seedRoot;
    private readonly ILogger<SourcePluginCompiler> logger;
    private readonly Func<SourcePluginReferences> references;
    private readonly SourcePluginCompilerSettings settings;

    public SourcePluginCompiler(string? cacheRoot = null)
        : this(cacheRoot, NullLogger<SourcePluginCompiler>.Instance) { }

    public SourcePluginCompiler(string? cacheRoot, ILogger<SourcePluginCompiler> logger, string? seedRoot = null)
        : this(cacheRoot, logger, () => SourcePluginReferences.Bundled.Value, new(), seedRoot) { }

    internal SourcePluginCompiler(string? cacheRoot, ILogger<SourcePluginCompiler> logger,
        Func<SourcePluginReferences> references, SourcePluginCompilerSettings settings, string? seedRoot = null)
    {
        this.cacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LoomX", "plugin-cache", "source");
        this.seedRoot = seedRoot;
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
        var inputsReadStartedAt = Stopwatch.GetTimestamp();
        try { inputs = await SourcePluginInputs.ReadAsync(packageDirectory, manifest, cancellationToken).ConfigureAwait(false); }
        catch (SourcePluginInputException exception)
        {
            logger.LogWarning(exception, "源码插件输入验证失败 {Stage} {Code}", exception.Diagnostic.Stage, exception.Diagnostic.Code);
            return new(false, null, null, null, false, [exception.Diagnostic]);
        }
        var inputsReadElapsed = Stopwatch.GetElapsedTime(inputsReadStartedAt);
        logger.LogInformation("源码插件编译开始 {PluginId} {SourceCount} {InputReadMs}", manifest.Id, inputs.Sources.Length,
            (long)inputsReadElapsed.TotalMilliseconds);
        var referencesStartedAt = Stopwatch.GetTimestamp();
        SourcePluginReferences referenceSet;
        try { referenceSet = references(); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or BadImageFormatException)
        {
            logger.LogWarning(exception, "源码插件固定引用不可用 {PluginId}", manifest.Id);
            return Failure(manifest.Id, PluginDiagnosticStage.Compile, "LX_REFERENCES_INVALID", "宿主固定编译引用缺失或摘要不匹配。");
        }
        // 引用集惰性初始化只在每进程首次支付，用于归属编译窗口内的固定开销（tasks 1.2/2.2）。
        var referencesMs = (long)Stopwatch.GetElapsedTime(referencesStartedAt).TotalMilliseconds;
        var key = inputs.CacheKey(referenceSet.Digest, CompilerIdentity, settings.Identity);
        var identity = SourceCacheIdentity.Create(referenceSet.Digest, CompilerIdentity, settings.Identity, inputs.InputIdentity());
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
                logger.LogInformation("源码插件编译缓存命中 {PluginId} {References} {Compiler} {Options} {Inputs} {CacheKey} {ReferencesMs}",
                    manifest.Id, identity.References, identity.Compiler, identity.Options, identity.Inputs, key, referencesMs);
                return new(true, cache.AssemblyPath, cache.PdbPath, key, true, cacheDiagnostics);
            }
            var cacheWarning = Directory.Exists(cache.DirectoryPath)
                ? ImmutableArray.Create(new PluginDiagnostic(manifest.Id, PluginDiagnosticStage.Cache, PluginDiagnosticSeverity.Warning,
                    "LX_CACHE_INVALID", "编译缓存不完整或认证失败，已重新编译。")) : [];
            if (cacheWarning.Length > 0) logger.LogWarning("源码插件缓存认证失败，重新编译 {PluginId} {CacheKey}", manifest.Id, key);
            await LogMissIdentityAsync(cache, authenticationKey, identity, manifest.Id, key, cancellationToken).ConfigureAwait(false);
            // 预编译种子（tasks 3.1/3.2）：宿主随附、按缓存键寻址并经种子密钥认证的代际，验证后复制进用户缓存重签发布；
            // 身份不一致（键不同→种子目录不存在）、产物损坏或种子缺失时保持返回 null，行为与无种子一致。
            var seeded = await AdoptSeedAsync(cache, inputs, authenticationKey, identity, manifest.Id, key, cancellationToken).ConfigureAwait(false);
            if (seeded is { } adopted) return adopted;
            candidate = cache.CreateCandidate();
            var (success, diagnostics) = await EmitAsync(candidate, cache, inputs, manifest.Id, referenceSet, cancellationToken).ConfigureAwait(false);
            if (!success)
            {
                logger.LogWarning("源码插件编译失败 {PluginId} {DiagnosticCount}", manifest.Id, diagnostics.Length);
                return new(false, null, null, key, false, cacheWarning.AddRange(diagnostics));
            }
            await cache.SealAsync(candidate, inputs, diagnostics, authenticationKey, identity.ToComponents(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            cache.Publish(candidate);
            candidate = null;
            logger.LogInformation("源码插件编译完成 {PluginId} {References} {Compiler} {Options} {Inputs} {CacheKey} {DiagnosticCount} {ReferencesMs}",
                manifest.Id, identity.References, identity.Compiler, identity.Options, identity.Inputs, key, diagnostics.Length, referencesMs);
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

    /// <summary>
    /// 缓存键内的编译器身份：程序集全名 + 程序集版本（tasks 3.3 稳定化）。
    /// NuGet 预编译程序集的版本不随宿主重建变化，消除「同包版本不同构建 MVID」的理论翻转；
    /// MVID 只进入 <see cref="CompilerIdentityDetail"/> 诊断输出，用于排查该极小概率场景。
    /// </summary>
    internal static string CompilerIdentity => typeof(CSharpCompilation).Assembly.FullName + "|"
        + typeof(CSharpCompilation).Assembly.GetName().Version + "|"
        + typeof(Compilation).Assembly.FullName + "|" + typeof(Compilation).Assembly.GetName().Version;

    /// <summary>诊断用完整编译器身份，含两个 Roslyn 组件的 MVID；不参与缓存键。</summary>
    internal static string CompilerIdentityDetail => CompilerIdentity + "|mvid="
        + typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId + "|"
        + typeof(Compilation).Assembly.ManifestModule.ModuleVersionId;

    /// <summary>未命中时与最近已认证代际的身份组成对比，指出翻转项；对比失败或无记录时输出当前身份供排查。</summary>
    private async Task LogMissIdentityAsync(SourcePluginCache cache, byte[] authenticationKey, SourceCacheIdentity identity,
        string pluginId, string cacheKey, CancellationToken token)
    {
        var stored = await cache.TryReadLastSealedIdentityAsync(cacheKey, authenticationKey, token).ConfigureAwait(false);
        if (stored is null)
        {
            logger.LogInformation("源码插件缓存未命中，无既有身份记录 {PluginId} {References} {Compiler} {Options} {Inputs} {CompilerDetail} {CacheKey}",
                pluginId, identity.References, identity.Compiler, identity.Options, identity.Inputs, CompilerIdentityDetail, cacheKey);
            return;
        }
        var flipped = SourceCacheIdentity.Components
            .Where(component => !stored.TryGetValue(component, out var value) || !string.Equals(value, identity.Component(component), StringComparison.Ordinal))
            .ToArray();
        logger.LogInformation(
            "源码插件缓存未命中，身份对比 {PluginId} {Flipped} {References} {Compiler} {Options} {Inputs} {StoredReferences} {StoredCompiler} {StoredOptions} {StoredInputs} {CompilerDetail} {CacheKey}",
            pluginId, flipped.Length == 0 ? "无" : string.Join(",", flipped),
            identity.References, identity.Compiler, identity.Options, identity.Inputs,
            stored.TryGetValue("references", out var storedReferences) ? storedReferences : "未知",
            stored.TryGetValue("compiler", out var storedCompiler) ? storedCompiler : "未知",
            stored.TryGetValue("options", out var storedOptions) ? storedOptions : "未知",
            stored.TryGetValue("inputs", out var storedInputs) ? storedInputs : "未知",
            CompilerIdentityDetail, cacheKey);
    }

    /// <summary>
    /// 尝试采用宿主随附的预编译种子（tasks 3.1/3.2）。种子按缓存键寻址：运行期计算的键与发布期一致才会命中，
    /// 身份不一致自然回落运行时编译；种子验证复用 <see cref="SourcePluginCache.ReadAsync"/> 的完整完整性与认证检查；
    /// 采用是把已验证产物复制进用户缓存并以用户认证密钥重签发布，运行时加载路径保持只有用户缓存一条。
    /// 任何失败都返回 null，行为与未提供种子一致。
    /// </summary>
    private async Task<SourcePluginCompilationResult?> AdoptSeedAsync(SourcePluginCache cache, SourcePluginInputs inputs,
        byte[] authenticationKey, SourceCacheIdentity identity, string pluginId, string cacheKey, CancellationToken token)
    {
        if (seedRoot is null) return null;
        try
        {
            var seedCache = new SourcePluginCache(seedRoot, pluginId, cacheKey);
            var seedKey = await SourcePluginPaths.ReadAsync(seedRoot, SeedKeyRelative, token).ConfigureAwait(false);
            var seedDiagnostics = await seedCache.ReadAsync(seedKey, inputs, token).ConfigureAwait(false);
            if (seedDiagnostics is not { } diagnostics)
            {
                logger.LogInformation("源码插件预编译种子未命中或验证失败，回退运行时编译 {PluginId} {CacheKey}", pluginId, cacheKey);
                return null;
            }
            if (!await cache.TryAdoptAsync(seedCache, inputs, diagnostics, authenticationKey, identity.ToComponents(), token).ConfigureAwait(false))
            {
                logger.LogWarning("源码插件预编译种子采用失败，回退运行时编译 {PluginId} {CacheKey}", pluginId, cacheKey);
                return null;
            }
            logger.LogInformation("源码插件编译缓存种子命中 {PluginId} {References} {Compiler} {Options} {Inputs} {CacheKey}",
                pluginId, identity.References, identity.Compiler, identity.Options, identity.Inputs, cacheKey);
            return new(true, cache.AssemblyPath, cache.PdbPath, cacheKey, true, diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning("源码插件预编译种子读取失败，回退运行时编译 {PluginId} {ErrorType}", pluginId, exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// 发布期离线编译（tasks 3.1）：与运行期完全相同的输入与编译流程，产物与完整性元数据写入种子根，
    /// 用发布期种子密钥签名；运行期命中要求键一致并复用同一套认证验证，不引入任何绕过身份校验的加载路径。
    /// </summary>
    internal async Task<bool> TrySeedAsync(string packageDirectory, PluginManifest manifest, byte[] seedKey, CancellationToken token)
    {
        try
        {
            var inputs = await SourcePluginInputs.ReadAsync(packageDirectory, manifest, token).ConfigureAwait(false);
            var referenceSet = references();
            var key = inputs.CacheKey(referenceSet.Digest, CompilerIdentity, settings.Identity);
            var identity = SourceCacheIdentity.Create(referenceSet.Digest, CompilerIdentity, settings.Identity, inputs.InputIdentity());
            var cache = new SourcePluginCache(cacheRoot, manifest.Id, key);
            var candidate = cache.CreateCandidate();
            try
            {
                var (success, diagnostics) = await EmitAsync(candidate, cache, inputs, manifest.Id, referenceSet, token).ConfigureAwait(false);
                if (!success)
                {
                    logger.LogWarning("源码插件预编译种子生成失败：编译诊断 {PluginId} {DiagnosticCount}", manifest.Id, diagnostics.Length);
                    return false;
                }
                await cache.SealAsync(candidate, inputs, diagnostics, seedKey, identity.ToComponents(), token).ConfigureAwait(false);
                cache.Publish(candidate);
                return true;
            }
            catch
            {
                cache.CleanupCandidate(candidate);
                throw;
            }
        }
        catch (Exception exception) when (exception is SourcePluginInputException or InvalidDataException or IOException
            or UnauthorizedAccessException or ArgumentException or BadImageFormatException)
        {
            logger.LogWarning(exception, "源码插件预编译种子生成失败 {PluginId} {ErrorType}", manifest.Id, exception.GetType().Name);
            return false;
        }
    }

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

/// <summary>
/// 缓存身份四个组成部分的诊断摘要：引用集、编译器、编译选项输出短哈希，编译选项保留可读串，插件输入输出短哈希。
/// 只用于日志与缓存元数据里的代际对比，MUST NOT 参与正确性判定——缓存键仍由完整身份唯一决定。
/// </summary>
internal sealed record SourceCacheIdentity(string References, string Compiler, string Options, string Inputs)
{
    internal static readonly string[] Components = ["references", "compiler", "options", "inputs"];

    internal static SourceCacheIdentity Create(string referencesDigest, string compilerIdentity, string optionsIdentity, string inputsHash) =>
        new(Short(referencesDigest), Short(compilerIdentity), optionsIdentity, Short(inputsHash));

    internal string Component(string name) => name switch
    {
        "references" => References,
        "compiler" => Compiler,
        "options" => Options,
        "inputs" => Inputs,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    internal Dictionary<string, string> ToComponents() => new(StringComparer.Ordinal)
    {
        ["references"] = References,
        ["compiler"] = Compiler,
        ["options"] = Options,
        ["inputs"] = Inputs,
    };

    private static string Short(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
