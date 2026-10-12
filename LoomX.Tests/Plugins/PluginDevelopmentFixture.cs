using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Plugins;

/// <summary>只在本任务临时根运行；直接复用公开生产验证、路径与快照读取入口。</summary>
internal sealed class PluginDevelopmentFixture : IAsyncDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "LoomX-development-fixture-" + Guid.NewGuid().ToString("N"));
    internal string DevelopmentRoot => Path.Combine(Root, "development");
    internal string InstalledRoot => Path.Combine(Root, "installed");
    internal string ProductionData => Path.Combine(Root, "production-data");
    internal SourcePluginCompiler Compiler { get; }
    internal PluginRuntime Runtime { get; private set; }
    internal PluginDevelopmentService Service { get; private set; }

    internal PluginDevelopmentFixture()
    {
        Directory.CreateDirectory(DevelopmentRoot);
        Directory.CreateDirectory(InstalledRoot);
        Compiler = new SourcePluginCompiler(Path.Combine(Root, "cache"));
        Runtime = StartRuntime();
        Service = NewService();
    }

    internal PluginDevelopmentService NewService(PluginDevelopmentValidation? validator = null) => new(
        new(DevelopmentRoot), Compiler, Runtime, NullLoggerFactory.Instance,
        validator ?? new(ValidateProductionInputsAsync, ResolveProductionPath, ReadProductionFileAsync));

    internal async Task StartInstalledAsync()
    {
        await Runtime.DisposeAsync();
        Runtime = StartRuntime();
        Service = NewService();
    }

    private PluginRuntime StartRuntime() => PluginRuntime.Start(new()
    {
        PluginDirectory = InstalledRoot, DataRootDirectory = ProductionData, SourceCompiler = Compiler,
        BridgeFactory = new PluginHostBridgeFactory(ProductionData, new ToastService(), NullLoggerFactory.Instance).CreateScope,
    }, NullLoggerFactory.Instance);

    internal void Write(string id, string relative, string text, bool installed = false)
    {
        var path = Path.Combine(installed ? InstalledRoot : DevelopmentRoot, id, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    internal void Package(string id = "demo", string version = "1", string? source = null, string[]? capabilities = null, bool installed = false, bool withExtension = false, bool withUi = false)
    {
        Write(id, "plugin.manifest.json", JsonSerializer.Serialize(new
        {
            id, version,
            runtime = new { kind = "csharp-source", entry_type = "Demo.Plugin", sources = new[] { "src/Plugin.cs" } },
            host_capabilities = capabilities ?? [], capabilities = Array.Empty<string>(),
            extensions = withExtension ? new[] { new { id = "echo", kind = "request", pipeline = "development.request", failure_policy = "fail-closed", capabilities = Array.Empty<string>() } } : [],
            ui = withUi ? new { contributions = new[] { new { id = "card", slot = "card-body" } } } : null,
        }), installed);
        Write(id, "src/Plugin.cs", source ?? BasicSource.Replace("\"demo\"", JsonSerializer.Serialize(id)), installed);
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" }) Write(id, "i18n/" + culture + ".json", "{}", installed);
    }

    internal const string BasicSource = """
        using System.Collections.Generic;
        using LoomX.Plugins;
        namespace Demo;
        public sealed class Plugin : ILoomXPlugin
        {
            public string Id => "demo";
            public void Initialize(PluginInitializationContext context) { }
            public IEnumerable<IPipelineExtension> CreateExtensions() => System.Array.Empty<IPipelineExtension>();
        }
        """;

    internal static string ResolveProductionPath(string root, string id) =>
        PluginPackageValidation.ResolvePackageDirectory(root, id);

    internal static Task<byte[]> ReadProductionFileAsync(string directory, string relative, CancellationToken ct) =>
        PluginPackageValidation.ReadPackageFileAsync(directory, relative, ct);
    internal static async Task<PluginDevelopmentValidationResult> ValidateProductionInputsAsync(string directory, CancellationToken ct)
    {
        var result = await PluginPackageValidation.ValidateAsync(directory, ct);
        return new(result.Success, result.Manifest, result.Diagnostics);
    }
    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        // 只清理本夹具明确创建的临时根，不触碰工作树或其他会话产物。
        var absolute = Path.GetFullPath(Root);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("LoomX-development-fixture-", StringComparison.Ordinal)) throw new InvalidOperationException();
        try { Directory.Delete(absolute, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { /* collectible ALC 的 DLL 可能尚未解除映射；只保留本夹具临时产物，不强制回收其他会话资源。 */ }
    }
}
