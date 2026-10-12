using System.Text.Json;
using System.Xml.Linq;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using LoomX.ToolResultCompression;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 插件负载的文件名与目录布局在所有工程里必须完全一致：
/// <c>plugins/&lt;id&gt;/plugin.manifest.json</c> 与 <c>plugins/&lt;id&gt;/i18n/&lt;culture&gt;.json</c>。
/// <para>
/// 同名冲突只能靠「每个插件各占一个目录」解决。曾经为了绕开测试输出目录里的平铺覆盖，
/// 测试工程把 Manifest 改名成 <c>plugin-manifests/&lt;id&gt;.json</c>、把文案塞进
/// <c>i18n/&lt;程序集名&gt;/</c>，结果运行时契约被拆成两套：输出根目录长期躺着一份只属于
/// 另一个插件的 Manifest，却没有任何测试会发现。这里把布局钉死，防止再次分叉。
/// </para>
/// </summary>
public sealed class PluginPayloadLayoutTests
{
    private static readonly string[] Cultures = ["zh-CN", "zh-TW", "ja-JP", "en-US"];

    [Fact]
    public void TestOutput_MirrorsRuntimePluginDirectoryLayout()
    {
        foreach (var pluginId in FirstPartyPluginIds)
        {
            var packageDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", pluginId);

            Assert.True(Directory.Exists(packageDirectory), packageDirectory);
            Assert.True(
                File.Exists(Path.Combine(packageDirectory, "plugin.manifest.json")),
                pluginId + "/plugin.manifest.json");
            foreach (var culture in Cultures)
            {
                Assert.True(
                    File.Exists(Path.Combine(packageDirectory, "i18n", culture + ".json")),
                    pluginId + "/i18n/" + culture + ".json");
            }
        }
    }

    [Fact]
    public void TestOutput_HasNoFlattenedPluginPayload()
    {
        // 平铺副本一定是错的：两个插件的 plugin.manifest.json 与 i18n/<culture>.json 同名，
        // 输出根目录最多留下最后一份，另一个插件的负载被静默丢弃且无人察觉。
        var baseDirectory = AppContext.BaseDirectory;

        Assert.False(
            File.Exists(Path.Combine(baseDirectory, "plugin.manifest.json")),
            "输出根目录出现平铺的 plugin.manifest.json：内容只属于一个插件");
        Assert.False(
            Directory.Exists(Path.Combine(baseDirectory, "i18n")),
            "输出根目录出现平铺的 i18n/：内容只属于一个插件");
    }

    [Fact]
    public void TestProject_MirrorsPayloadInsteadOfRenamingIt()
    {
        var project = File.ReadAllText(GetRepositoryFile("LoomX.Tests", "LoomX.Tests.csproj"));

        Assert.Contains("FirstPartySourcePayload.targets", project, StringComparison.Ordinal);
        foreach (var pluginId in FirstPartyPluginIds)
            Assert.Contains(@"$(OutputPath)plugins\" + pluginId + @"\%(PackageRelativePath)", project, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin-manifests", project, StringComparison.Ordinal);
    }

    [Fact]
    public void HostProject_DoesNotDeclarePluginPayloadAsContent()
    {
        // Content 会随 ProjectReference 传给每个引用方，同名负载平铺后只剩一份且内容是错的。
        // 插件工程改用自有目标后，这里不需要（也不该有）任何为绕开覆盖而做的改名或删除目标。
        foreach (var projectName in new[] { "LoomX", "LoomX.PluginPlayground" })
        {
            var project = File.ReadAllText(GetRepositoryFile(projectName, projectName + ".csproj"));

            Assert.DoesNotContain("plugin-manifests", project, StringComparison.Ordinal);
            Assert.DoesNotContain("RemoveFlattenedPlugin", project, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TextCatalog_ReadsEveryCultureFromTheMirroredPackageDirectory()
    {
        foreach (var pluginId in FirstPartyPluginIds)
        {
            foreach (var culture in Cultures)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                    AppContext.BaseDirectory, "plugins", pluginId, "i18n", culture + ".json")));
                var expected = document.RootElement.GetProperty("plugin.name").GetString();

                // 逐条比对外部文件内容：只要 catalog 还能显示可读文案却答非所问，
                // 就说明它退化到了嵌入兜底或别的插件的资源。
                Assert.Equal(expected, Resolve(pluginId, culture));
            }
        }
    }

    [Theory]
    [InlineData("loomx.credential-protection")]
    [InlineData("loomx.tool-result-compression")]
    public void TestOutput_ContainsExactSourcePackageWithoutBinaryDependencies(string pluginId)
    {
        var packageDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", pluginId);
        Assert.True(File.Exists(Path.Combine(packageDirectory, "GlobalUsings.cs")),
            "源码包必须包含显式 GlobalUsings.cs，不能依赖静态 SDK 生成源码");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageDirectory, "plugin.manifest.json")));
        var runtime = manifest.RootElement.GetProperty("runtime");
        Assert.Equal("csharp-source", runtime.GetProperty("kind").GetString());
        var sources = runtime.GetProperty("sources").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.NotEmpty(sources);
        Assert.All(sources, source =>
        {
            Assert.Equal(Path.GetFileName(source), source);
            Assert.EndsWith(".cs", source, StringComparison.Ordinal);
        });
        var expected = sources.Concat(Cultures.Select(culture => "i18n/" + culture + ".json"))
            .Append("plugin.manifest.json").Order(StringComparer.Ordinal).ToArray();
        var actual = Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(packageDirectory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        var repositoryFolder = pluginId == CredentialProtectionPlugin.PluginId
            ? "LoomX.CredentialProtection" : "LoomX.ToolResultCompression";
        foreach (var relativePath in expected)
            Assert.Equal(File.ReadAllBytes(GetRepositoryFile("plugins", repositoryFolder, relativePath)),
                File.ReadAllBytes(Path.Combine(packageDirectory, relativePath)));
    }

    [Theory]
    [InlineData("LoomX.CredentialProtection", "_CredentialPluginPayload")]
    [InlineData("LoomX.ToolResultCompression", "_CompressionPluginPayload")]
    public void SharedPayloadList_EqualsManifestSourcesAndFourCultures(string sourceFolder, string itemName)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(GetRepositoryFile("plugins", sourceFolder, "plugin.manifest.json")));
        var sources = manifest.RootElement.GetProperty("runtime").GetProperty("sources")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        var topLevelSources = Directory.GetFiles(GetRepositoryFile("plugins", sourceFolder), "*.cs")
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(topLevelSources, sources.Order(StringComparer.Ordinal).ToArray());
        var payload = XDocument.Load(GetRepositoryFile("FirstPartySourcePayload.targets"));
        Assert.Empty(payload.Descendants("Content"));
        var items = payload.Descendants(itemName).ToArray();
        Assert.All(items, item =>
        {
            var relativePath = (string)item.Attribute("PackageRelativePath")!;
            Assert.Equal(@"$(MSBuildThisFileDirectory)plugins\" + sourceFolder + @"\" + relativePath,
                (string?)item.Attribute("Include"));
        });
        var expected = sources.Concat(Cultures.Select(culture => "i18n/" + culture + ".json"))
            .Append("plugin.manifest.json").Order(StringComparer.Ordinal).ToArray();
        var actual = items.Select(item => ((string)item.Attribute("PackageRelativePath")!)
            .Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    private static string[] FirstPartyPluginIds =>
        [CredentialProtectionPlugin.PluginId, ToolResultCompressionPlugin.PluginId];

    private static string Resolve(string pluginId, string cultureName) =>
        pluginId == CredentialProtectionPlugin.PluginId
            ? PluginTextCatalog.For(
                    typeof(CredentialProtectionPlugin),
                    CredentialProtectionPlugin.PluginId,
                    cultureName).Get("plugin.name")
            : PluginTextCatalog.For(
                    typeof(ToolResultCompressionPlugin),
                    ToolResultCompressionPlugin.PluginId,
                    cultureName).Get("plugin.name");

    private static string GetRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}