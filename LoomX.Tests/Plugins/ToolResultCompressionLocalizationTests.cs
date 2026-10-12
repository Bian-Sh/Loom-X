using System.Xml.Linq;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.ToolResultCompression;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 插件本地化归属：文案由插件自带的 i18n 资源提供，宿主资源里不留任何插件专属文案。
/// 这一套是两个第一方插件共同的参考实现，凭据保护也走同一条链路。
/// </summary>
public sealed class ToolResultCompressionLocalizationTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-trc-i18n-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 插件程序集仍由测试进程的 ALC 持有，临时目录留给系统清理。
        }
    }

    [Fact]
    public void Plugin_ProvidesOwnDisplayInfoPerCulture()
    {
        var plugin = new ToolResultCompressionPlugin();

        var zh = plugin.GetDisplayInfo(new PluginUiContext("zh-CN"));
        var en = plugin.GetDisplayInfo(new PluginUiContext("en-US"));
        var ja = plugin.GetDisplayInfo(new PluginUiContext("ja-JP"));
        var tw = plugin.GetDisplayInfo(new PluginUiContext("zh-TW"));

        Assert.False(zh.IsEmpty);
        Assert.Equal("工具结果压缩", zh.DisplayName);
        Assert.Equal("Tool result compression", en.DisplayName);
        Assert.Equal("ツール結果圧縮", ja.DisplayName);
        Assert.Equal("工具結果壓縮", tw.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(zh.LifecycleNote));
        Assert.Equal("系统插件", zh.Category);
    }

    [Fact]
    public void PluginRuntime_ExposesDisplayInfoFromPlugin()
    {
        StartRuntimeWithCompressionPlugin();

        var runtime = PluginRuntime.Start(
            new PluginRuntimeOptions
            {
                PluginDirectory = Path.Combine(rootDirectory, "plugins"),
                DataRootDirectory = Path.Combine(rootDirectory, "data"),
                SourceCompiler = new SourcePluginCompiler(Path.Combine(rootDirectory, "cache")),
            },
            NullLoggerFactory.Instance);

        var info = runtime.GetPluginDisplayInfo(ToolResultCompressionPlugin.PluginId, "zh-CN");

        Assert.NotNull(info);
        Assert.Equal("工具结果压缩", info!.DisplayName);
        Assert.Empty(runtime.Diagnostics);
    }

    [Fact]
    public void HostResources_DoNotCarryAnyPluginSpecificText()
    {
        // 插件文案写进宿主资源会让「新增插件」变成「改宿主」，这里守住这条边界。
        foreach (var fileName in new[]
                 {
                     "Strings.resx", "Strings.en-US.resx", "Strings.ja-JP.resx", "Strings.zh-TW.resx",
                 })
        {
            var resources = File.ReadAllText(GetRepositoryFile("LoomX", "Resources", fileName));
            Assert.False(resources.Contains("plugins.credential", StringComparison.Ordinal), fileName);
            Assert.False(resources.Contains("plugins.compression", StringComparison.Ordinal), fileName);
            Assert.False(resources.Contains("工具结果压缩", StringComparison.Ordinal), fileName);
            Assert.False(resources.Contains("Tool result compression", StringComparison.Ordinal), fileName);
            Assert.False(resources.Contains("凭据保护", StringComparison.Ordinal), fileName);
        }
    }

    [Fact]
    public void SettingsPage_RendersPluginOwnTextPerCulture()
    {
        var plugin = new ToolResultCompressionPlugin();
        plugin.Initialize(new PluginInitializationContext(
            ToolResultCompressionPlugin.PluginId,
            Path.Combine(rootDirectory, "data")));

        var zhText = CollectText(plugin, "zh-CN");
        var enText = CollectText(plugin, "en-US");

        Assert.Contains("节省对比", zhText, StringComparison.Ordinal);
        Assert.Contains("压缩策略", zhText, StringComparison.Ordinal);
        Assert.Contains("Savings", enText, StringComparison.Ordinal);
        Assert.Contains("Compression policy", enText, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCulture_FallsBackToEmbeddedDefault()
    {
        var text = PluginTextCatalog.For(
            typeof(ToolResultCompressionPlugin),
            ToolResultCompressionPlugin.PluginId,
            "xx-XX");

        // 未提供的文化回落到随程序集嵌入的默认文化，而不是空白或键名。
        Assert.Equal("Savings", text.Get("settings.savings_title"));
    }

    [Fact]
    public void TextCatalog_PerPluginIsolationPreventsCrossContamination()
    {
        // 两个插件各自持有目录：凭据保护的文案不能出现在压缩插件的目录里。
        var catalog = PluginTextCatalog.For(
            typeof(ToolResultCompressionPlugin),
            ToolResultCompressionPlugin.PluginId,
            "zh-CN");

        Assert.Equal("key.absent", catalog.Get("key.absent"));
        Assert.Equal("工具结果压缩", catalog.Get("plugin.name"));
        Assert.DoesNotContain("凭据保护", catalog.Get("plugin.description"), StringComparison.Ordinal);
    }

    [Fact]
    public void PluginPayload_ShipsLocalizationFiles()
    {
        var payload = XDocument.Load(GetRepositoryFile("FirstPartySourcePayload.targets"));
        foreach (var (plugin, pluginId, itemName) in new[]
        {
            ("LoomX.ToolResultCompression", "loomx.tool-result-compression", "_CompressionPluginPayload"),
            ("LoomX.CredentialProtection", "loomx.credential-protection", "_CredentialPluginPayload"),
        })
        {
            var project = XDocument.Load(GetRepositoryFile("plugins", plugin, plugin + ".csproj"));
            // 两个工程导入同一显式负载表，不要求恢复已经移除的递归 i18n glob。
            Assert.Contains(project.Descendants("Import"), item =>
                (string?)item.Attribute("Project") == "..\\..\\FirstPartySourcePayload.targets");
            Assert.Contains(project.Descendants("PluginPayload"), item =>
                (string?)item.Attribute("Include") == "@(" + itemName + ")");
            Assert.Contains(project.Descendants("Target"), item =>
                (string?)item.Attribute("Name") == "CopyPluginPayloadToOutput");
            var items = payload.Descendants(itemName).ToArray();
            Assert.NotEmpty(items);
            Assert.DoesNotContain(items, item => ((string?)item.Attribute("Include") ?? string.Empty).IndexOfAny(['*', '?']) >= 0);
            foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            {
                var relativePath = "i18n/" + culture + ".json";
                var localization = Assert.Single(items, item =>
                    ((string?)item.Attribute("PackageRelativePath"))?.Replace('\\', '/') == relativePath);
                Assert.Equal("$(MSBuildThisFileDirectory)plugins\\" + plugin + "\\i18n\\" + culture + ".json",
                    (string?)localization.Attribute("Include"));
                Assert.True(File.Exists(GetRepositoryFile("plugins", plugin, "i18n", culture + ".json")), plugin + "/" + culture);
                Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "plugins", pluginId, "i18n", culture + ".json")),
                    "测试输出缺少插件文案：" + pluginId + "/" + culture);
            }
        }
    }

    private void StartRuntimeWithCompressionPlugin() =>
        FirstPartySourceTestPayload.Stage(Path.Combine(rootDirectory, "plugins"), ToolResultCompressionPlugin.PluginId);

    private static string CollectText(ToolResultCompressionPlugin plugin, string cultureName)
    {
        var settings = plugin.GetUiContributions(new PluginUiContext(cultureName))
            .Single(item => item.Slot == PluginUiSlot.DetailBody);
        return string.Join('\n', Texts(settings.Root));
    }

    private static IEnumerable<string> Texts(PluginUiNode node)
    {
        switch (node)
        {
            case PluginUiTextNode text:
                yield return text.Text;
                break;
            case PluginUiStackNode stack:
                foreach (var child in stack.Children.SelectMany(Texts)) yield return child;
                break;
            case PluginUiGridNode grid:
                foreach (var child in grid.Children.Select(item => item.Content).SelectMany(Texts))
                    yield return child;
                break;
            case PluginUiSurfaceNode surface:
                foreach (var child in Texts(surface.Content)) yield return child;
                break;
        }
    }

    private static string GetRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
