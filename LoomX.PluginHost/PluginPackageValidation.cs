using System.Text;
using LoomX.Plugins;

namespace LoomX.Plugins.Host;

public sealed record PluginPackageValidationResult(bool Success, PluginManifest? Manifest, IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>Runtime 与开发工具共用的执行前包检查；不加载程序集或执行插件代码。</summary>
public static class PluginPackageValidation
{
    public static string ResolvePackageDirectory(string root, string pluginId)
    {
        try
        {
            var relative = SourcePluginPaths.Relative(pluginId);
            if (relative.Contains('/') || relative != pluginId) throw new InvalidDataException();
            return SourcePluginPaths.Resolve(root, relative);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "插件目录身份不在允许的开发根边界内。"); }
    }

    /// <summary>开发工具读取包内文件的唯一生产路径入口；错误不回显输入和外部路径。</summary>
    public static async Task<byte[]> ReadPackageFileAsync(string directory, string relativePath, CancellationToken cancellationToken = default)
    {
        try { return await SourcePluginPaths.ReadAsync(directory, relativePath, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "包内文件无法安全读取。"); }
    }

    public static async Task<PluginPackageValidationResult> ValidateAsync(string directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<PluginDiagnostic> diagnostics = [];
        PluginManifest? manifest;
        try
        {
            var bytes = await SourcePluginPaths.ReadAsync(directory, ManifestParser.ManifestFileName, cancellationToken).ConfigureAwait(false);
            var errors = new List<string>();
            manifest = ManifestParser.Parse(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), errors);
            if (manifest is null)
            {
                // 原始验证错误可能携带输入，不把它们拷入面向模型的诊断。
                foreach (var _ in errors.DefaultIfEmpty(""))
                    diagnostics.Add(new("", PluginDiagnosticStage.Manifest, PluginDiagnosticSeverity.Error,
                        PluginDiagnosticCodes.InvalidManifest, "清单验证失败，请检查必需字段及声明一致性。", ManifestParser.ManifestFileName));
                return new(false, null, diagnostics);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            return new(false, null, [new("", PluginDiagnosticStage.Manifest, PluginDiagnosticSeverity.Error,
                PluginDiagnosticCodes.InvalidManifest, "清单不存在、无法读取或包路径非法。")]);
        }
        diagnostics.AddRange(PluginCapabilityCatalog.Validate(manifest.Id, manifest.HostCapabilities));
        if (manifest.Runtime?.Kind == "csharp-source")
        {
            foreach (var source in manifest.Runtime.Sources ?? [])
                CheckFile(directory, manifest.Id, source, PluginDiagnosticStage.SourceRead, "LX_SOURCE_MISSING", "声明的源码文件不存在。", diagnostics);
            foreach (var culture in SourcePluginInputs.Cultures)
                CheckFile(directory, manifest.Id, $"i18n/{culture}.json", PluginDiagnosticStage.SourceRead, "LX_LOCALIZATION_INVALID", "本地化负载不存在。", diagnostics);
            try { _ = await SourcePluginInputs.ReadAsync(directory, manifest, cancellationToken).ConfigureAwait(false); }
            catch (SourcePluginInputException exception) { diagnostics.Add(exception.Diagnostic); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { diagnostics.Add(new(manifest.Id, PluginDiagnosticStage.SourceRead, PluginDiagnosticSeverity.Error, "LX_SOURCE_READ_FAILED", "源码输入无法读取。")); }
        }
        else CheckFile(directory, manifest.Id, manifest.Assembly, PluginDiagnosticStage.Load,
            PluginDiagnosticCodes.InvalidEntry, "声明的程序集文件不存在。", diagnostics);
        ScanDependencies(directory, manifest.Id, diagnostics, cancellationToken);
        var result = diagnostics.Distinct().ToArray();
        return new(result.All(diagnostic => diagnostic.Severity != PluginDiagnosticSeverity.Error), manifest, result);
    }

    private static void CheckFile(string root, string id, string relative, PluginDiagnosticStage stage, string missingCode, string missingMessage, List<PluginDiagnostic> diagnostics)
    {
        try
        {
            var safe = SourcePluginPaths.Relative(relative);
            var absolute = SourcePluginPaths.Resolve(root, safe);
            if (!File.Exists(absolute)) diagnostics.Add(new(id, stage, PluginDiagnosticSeverity.Error, missingCode, missingMessage, safe));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { diagnostics.Add(new(id, stage, PluginDiagnosticSeverity.Error, PluginDiagnosticCodes.InvalidPath, "声明文件路径不在允许的插件包边界内。")); }
    }

    private static void ScanDependencies(string root, string id, List<PluginDiagnostic> diagnostics, CancellationToken token)
    {
        try
        {
            var directories = new Stack<(string Path, int Depth)>();
            directories.Push((Path.GetFullPath(root), 0));
            var seen = 0;
            while (directories.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                SourcePluginPaths.NoLinks(directory.Path);
                if (directory.Depth > 32) throw new InvalidDataException();
                foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (++seen > 4096) throw new InvalidDataException();
                    SourcePluginPaths.NoLinks(path);
                    if (Directory.Exists(path)) { directories.Push((path, directory.Depth + 1)); continue; }
                    var name = Path.GetFileName(path);
                    if (name.Equals("Microsoft.Data.Sqlite.dll", StringComparison.OrdinalIgnoreCase)
                        || (name.StartsWith("SQLitePCLRaw.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        || name.Equals("e_sqlite3.dll", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("libe_sqlite3.so", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("libe_sqlite3.dylib", StringComparison.OrdinalIgnoreCase))
                        diagnostics.Add(new(id, PluginDiagnosticStage.Load, PluginDiagnosticSeverity.Error,
                            "LX_DEPENDENCY_FORBIDDEN", "插件包不得夹带宿主 SQLite Bridge 实现。"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { diagnostics.Add(new(id, PluginDiagnosticStage.Load, PluginDiagnosticSeverity.Error, PluginDiagnosticCodes.InvalidPath, "插件包依赖路径非法或超过扫描上限。")); }
    }
}