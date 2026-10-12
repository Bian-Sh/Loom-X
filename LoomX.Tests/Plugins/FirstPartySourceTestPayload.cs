using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>只消费测试输出的实际源码包；不回退仓库，也不复制静态业务 DLL 或 deps。</summary>
internal static class FirstPartySourceTestPayload
{
    public static string Stage(string pluginDirectory, string pluginId)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "plugins", pluginId);
        var manifestPath = Path.Combine(source, ManifestParser.ManifestFileName);
        Assert.True(File.Exists(manifestPath), "测试输出缺少插件清单：" + pluginId);
        var errors = new List<string>();
        var manifest = ManifestParser.Parse(File.ReadAllText(manifestPath), errors);
        Assert.Empty(errors);
        Assert.NotNull(manifest);
        Assert.Equal(pluginId, manifest.Id);
        Assert.NotNull(manifest.Runtime);
        Assert.Equal("csharp-source", manifest.Runtime.Kind);
        Assert.NotNull(manifest.Runtime.Sources);
        Assert.NotEmpty(manifest.Runtime.Sources);

        var destination = Path.Combine(pluginDirectory, pluginId);
        Copy(ManifestParser.ManifestFileName);
        foreach (var relativePath in manifest.Runtime.Sources)
        {
            Assert.EndsWith(".cs", relativePath, StringComparison.OrdinalIgnoreCase);
            Copy(relativePath);
        }
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            Copy(Path.Combine("i18n", culture + ".json"));
        return destination;

        void Copy(string relativePath)
        {
            var input = Resolve(source, relativePath);
            var output = Resolve(destination, relativePath);
            Assert.True(File.Exists(input), "测试输出缺少插件源码或文案：" + pluginId + "/" + relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(input, output, overwrite: true);
        }
    }

    private static string Resolve(string directory, string relativePath)
    {
        Assert.False(Path.IsPathRooted(relativePath));
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
        Assert.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, fullPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(relativePath.Split('/', '\\'), segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
        return fullPath;
    }
}
