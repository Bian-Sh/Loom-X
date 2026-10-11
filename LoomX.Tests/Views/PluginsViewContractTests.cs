using System.IO;
using System.Xml.Linq;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class PluginsViewContractTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");

    [Fact]
    public void PluginPageIsRegisteredAsAFirstClassNavigationPage()
    {
        var mainViewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "MainWindowViewModel.cs"));
        var app = File.ReadAllText(Path.Combine(Root, "LoomX", "App.axaml"));
        var resources = File.ReadAllText(Path.Combine(Root, "LoomX", "Resources", "Strings.resx"));

        Assert.Contains("new(\"nav.plugins\"", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.plugins\", pluginsViewModel)", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type vm:PluginsViewModel}\"><views:PluginsView", app, StringComparison.Ordinal);
        Assert.Contains("name=\"nav.plugins\"", resources, StringComparison.Ordinal);
        Assert.Contains("name=\"nav.plugins.description\"", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginIconOnlyButtonStaysCloseToTextHeight()
    {
        var app = XDocument.Load(Path.Combine(Root, "LoomX", "App.axaml"));
        var style = app
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && (string?)element.Attribute("Selector") == "Button.plugin-ui-button.is-icon-only");
        var setters = style
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string)element.Attribute("Value")!,
                StringComparer.Ordinal);

        Assert.Equal("24", setters["Width"]);
        Assert.Equal("24", setters["Height"]);
        Assert.Equal("24", setters["MinWidth"]);
        Assert.Equal("24", setters["MinHeight"]);
        Assert.Equal("12", setters["CornerRadius"]);
    }

    [Fact]
    public void EveryPluginCanBeToggledAndCarriesItsOwnLifecycleText()
    {
        var viewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "PluginsViewModel.cs"));
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));
        var pluginI18n = File.ReadAllText(Path.Combine(
            Root, "plugins", "LoomX.CredentialProtection", "i18n", "zh-CN.json"));

        // 所有插件都可停用；停用语义由插件自己的 lifecycle 文案说明，宿主不再硬编码任何插件 id。
        Assert.Contains("public bool CanToggle => true", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanToggle}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialProtectionPluginId", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("loomx.credential-protection", viewModel, StringComparison.Ordinal);
        // 「停用后历史引用仍会还原」这条关键语义现在住在插件资源里。
        Assert.Contains("历史会话中已签发的凭据引用仍会识别", pluginI18n, StringComparison.Ordinal);
    }


    [Fact]
    public void PluginCardsRenderOnlyGenericCardBodyContributions()
    {
        var viewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "PluginsViewModel.cs"));
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));

        Assert.Contains("pluginRuntime.GetUiContributions(", viewModel, StringComparison.Ordinal);
        Assert.Contains("PluginUiSlot.CardBody", viewModel, StringComparison.Ordinal);
        Assert.Contains("<controls:PluginUiPresenter", view, StringComparison.Ordinal);
        Assert.Contains("Contributions=\"{Binding CardContributions}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("已脱敏请求", viewModel + view, StringComparison.Ordinal);
        Assert.DoesNotContain("SanitizedRequests", viewModel + view, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoredResponses", viewModel + view, StringComparison.Ordinal);
    }
    [Fact]
    public void DetailBodyUsesHostNavigationShellAndManifestDrivenGear()
    {
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));

        Assert.Contains("IsVisible=\"{Binding HasDetailUi}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenDetailCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPluginDetailVisible}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BackToPluginListCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Contributions=\"{Binding SelectedPlugin.DetailContributions}\"", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// 详情页交互：返回入口收敛到标题区，标题区在滚动区之外，长内容滚动时始终可见。
    /// </summary>
    [Fact]
    public void PluginDetailHeaderStaysFixedAndOwnsSingleBackCommand()
    {
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));
        var detailSection = view[view.IndexOf("IsPluginDetailVisible", StringComparison.Ordinal)..];

        // 标题区必须在 ScrollViewer 之前、且在其之外，滚动只作用于内容区。
        var headerIndex = detailSection.IndexOf("SelectedPlugin.DisplayName", StringComparison.Ordinal);
        var scrollIndex = detailSection.IndexOf("<ScrollViewer", StringComparison.Ordinal);
        Assert.True(headerIndex > 0, "详情标题区未找到。");
        Assert.True(scrollIndex > headerIndex, "标题区必须排在 ScrollViewer 之前，否则会随滚动消失。");

        // 插件图标占位已移除。
        Assert.DoesNotContain("M 8,13 L 13,13 L 13,8 L 19,8 L 19,13 L 24,13 L 24,19 L 19,19 L 19,24 L 13,24 L 13,19 L 8,19 Z",
            detailSection, StringComparison.Ordinal);

        // 返回入口唯一：详情区只出现一次返回命令绑定。
        Assert.Equal(1, CountOccurrences(detailSection, "Command=\"{Binding BackToPluginListCommand}\""));
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var position = text.IndexOf(needle, StringComparison.Ordinal);
        while (position >= 0)
        {
            count++;
            position = text.IndexOf(needle, position + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    [Fact]
    public void PluginUiRefreshUsesDispatcherCoalescingAndCultureSpecificSnapshots()
    {
        var viewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "PluginsViewModel.cs"));

        Assert.Contains("runtime.PluginUiInvalidated += OnPluginUiInvalidated", viewModel, StringComparison.Ordinal);
        Assert.Contains("uiRefreshQueue.Enqueue(args.PluginId)", viewModel, StringComparison.Ordinal);
        Assert.Contains("RefreshPluginUi(plugin.Id, culture.Name)", viewModel, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginPageReadsRuntimeSummariesWithoutExposingSensitivePayloads()
    {
        var viewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "PluginsViewModel.cs"));
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));

        Assert.Contains("gatewayService.GetHostedService<PluginRuntime>()", viewModel, StringComparison.Ordinal);
        Assert.Contains("pluginRuntime.PluginInfos", viewModel, StringComparison.Ordinal);
        Assert.Contains("pluginRuntime.Diagnostics", viewModel, StringComparison.Ordinal);
        Assert.Contains("public bool HasLoadError", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!runtime.SetPluginEnabled(item.Id, enabled))", viewModel, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CapabilitySummary}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", viewModel, StringComparison.Ordinal);
    }
}
