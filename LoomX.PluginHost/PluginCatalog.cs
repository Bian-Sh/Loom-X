namespace LoomX.Plugins.Host;

/// <summary>一个通过 Manifest 验证、待加载的插件包。</summary>
public sealed record DiscoveredPlugin(PluginManifest Manifest, string Directory);

/// <summary>
/// 插件目录发现：约定每个插件包是插件根目录下的一个子目录，内含 plugin.manifest.json。
/// 单个插件包非法只记录诊断，不影响其余插件的发现。
/// </summary>
public static class PluginCatalog
{
    public static IReadOnlyList<DiscoveredPlugin> Discover(string pluginDirectory, IList<string> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var discovered = new List<DiscoveredPlugin>();
        if (!System.IO.Directory.Exists(pluginDirectory))
        {
            diagnostics.Add($"插件目录不存在：{pluginDirectory}");
            return discovered;
        }

        foreach (var directory in System.IO.Directory.EnumerateDirectories(pluginDirectory).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(directory, ManifestParser.ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                diagnostics.Add($"跳过无 Manifest 的目录：{Path.GetFileName(directory)}");
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(manifestPath);
            }
            catch (IOException exception)
            {
                diagnostics.Add($"插件 {Path.GetFileName(directory)} 的 Manifest 读取失败：{exception.Message}");
                continue;
            }

            var errors = new List<string>();
            var manifest = ManifestParser.Parse(json, errors);
            if (manifest is null)
            {
                foreach (var error in errors)
                    diagnostics.Add($"插件 {Path.GetFileName(directory)} 被拒绝：{error}");
                continue;
            }

            discovered.Add(new DiscoveredPlugin(manifest, directory));
        }

        var duplicateIds = discovered
            .GroupBy(item => item.Manifest.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicateIds.Count > 0)
        {
            foreach (var id in duplicateIds)
                diagnostics.Add($"插件 id 重复，全部拒绝：{id}");
            discovered.RemoveAll(item => duplicateIds.Contains(item.Manifest.Id));
        }

        return discovered;
    }
}
