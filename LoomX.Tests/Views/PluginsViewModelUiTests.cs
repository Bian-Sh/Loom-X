using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.Localization;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using LoomX.ViewModels;
using LoomX.Views;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class PluginsViewModelUiTests
{
    [Fact]
    public void PluginList_DoesNotExposeGlobalRefreshButton()
    {
        using var gatewayService = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gatewayService);
        viewModel.Plugins.Add(CreatePluginItem(viewModel));

        var view = Render(viewModel);
        var refreshText = ResourceLookup.Resolve("plugins.action.refresh");

        Assert.DoesNotContain(
            view.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.Content, refreshText));
    }

    [Fact]
    public void PluginCard_UsesCompactStatusControlsAndInlineVersion()
    {
        using var gatewayService = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gatewayService);
        var item = CreatePluginItem(viewModel);
        viewModel.Plugins.Add(item);

        var view = Render(viewModel);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().ToArray();
        var name = Assert.Single(texts, text => text.Text == item.DisplayName);
        var version = Assert.Single(texts, text => text.Text == $"v{item.Version}");
        var toggle = Assert.Single(view.GetVisualDescendants().OfType<ToggleSwitch>());
        var gear = Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            button => Equals(ToolTip.GetTip(button), ResourceLookup.Resolve("plugins.detail.open")));

        Assert.DoesNotContain(texts, text => text.Text == item.StatusLabel);
        Assert.Equal(string.Empty, toggle.OnContent);
        Assert.Equal(string.Empty, toggle.OffContent);
        Assert.Equal(item.StatusLabel, ToolTip.GetTip(toggle));
        Assert.Equal(item.StatusLabel, AutomationProperties.GetName(toggle));
        Assert.True(toggle.Bounds.Left < gear.Bounds.Left, "启用开关应位于设置按钮左侧。");
        Assert.InRange(version.Bounds.Left - name.Bounds.Right, 0, 12);
        Assert.Equal(name.Bounds.Bottom, version.Bounds.Bottom, 2);
        Assert.InRange(version.FontSize, 11, name.FontSize);
    }

    [Fact]
    public void PluginDetailHeader_UsesUnifiedBackTitleWithoutPluginId()
    {
        using var gatewayService = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gatewayService);
        var item = CreatePluginItem(viewModel);
        viewModel.Plugins.Add(item);
        item.OpenDetailCommand.Execute(null);

        var view = Render(viewModel);
        var texts = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible)
            .ToArray();
        var name = Assert.Single(texts, text => text.Text == item.DisplayName);
        var version = Assert.Single(texts, text => text.Text == $"v{item.Version}");
        var back = Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            button => Equals(ToolTip.GetTip(button), ResourceLookup.Resolve("plugins.detail.back")));
        var arrow = Assert.IsType<TextBlock>(back.Content);

        Assert.DoesNotContain(texts, text => text.Text == item.StatusLabel);
        Assert.DoesNotContain(texts, text => text.Text == item.Id);
        Assert.Equal("←", arrow.Text);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(back.Background).Color);
        Assert.Equal(new Thickness(0), back.BorderThickness);
        var pseudoClassesProperty = typeof(StyledElement).GetProperty(
            "PseudoClasses",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(pseudoClassesProperty);
        var pseudoClasses = Assert.IsAssignableFrom<IPseudoClasses>(pseudoClassesProperty.GetValue(back));
        pseudoClasses.Add(":pointerover");
        Assert.NotEqual(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(back.Background).Color);
        pseudoClasses.Remove(":pointerover");
        var backRight = back.TranslatePoint(new Point(back.Bounds.Width, 0), view)!.Value.X;
        var nameLeft = name.TranslatePoint(default, view)!.Value.X;
        Assert.Equal(32, back.Bounds.Width);
        Assert.Equal(32, back.Bounds.Height);
        Assert.InRange(nameLeft - backRight, 0, 8);
        Assert.InRange(version.Bounds.Left - name.Bounds.Right, 0, 12);
        Assert.Equal(name.Bounds.Bottom, version.Bounds.Bottom, 2);
        Assert.InRange(version.FontSize, 11, name.FontSize);
    }

    [Fact]
    public void PluginList_DoesNotRenderTransientLoadStatus()
    {
        AvaloniaTestBootstrap.Ensure();

        const string statusMarker = "不应显示的插件加载状态";
        var view = new PluginsView
        {
            DataContext = new PluginListContext(statusMarker),
        };
        var host = new Window { Content = view };
        host.Measure(new Size(1000, 700));
        host.Arrange(new Rect(0, 0, 1000, 700));
        host.UpdateLayout();

        Assert.DoesNotContain(
            view.GetVisualDescendants().OfType<TextBlock>(),
            item => item.Text == statusMarker);
    }

    [Fact]
    public void PluginList_DoesNotRenderInlineLoadFailure()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new PluginsView
        {
            DataContext = new PluginListContext("", HasLoadError: true),
        };
        var host = new Window { Content = view };
        host.Measure(new Size(1000, 700));
        host.Arrange(new Rect(0, 0, 1000, 700));
        host.UpdateLayout();

        var failureTitle = ResourceLookup.Resolve("plugins.error.title");
        Assert.DoesNotContain(
            view.GetVisualDescendants().OfType<TextBlock>(),
            item => item.Text == failureTitle);
    }

    [Fact]
    public void DetailNavigation_OnlyOpensValidatedDeclaredDetail()
    {
        using var gatewayService = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gatewayService);
        var detail = new PluginUiContribution(
            PluginUiContribution.CurrentSchemaVersion,
            "settings",
            PluginUiSlot.DetailBody,
            new PluginUiTextNode("detail"));
        var item = new PluginItemViewModel(
            new LoadedPluginInfo("test.plugin", "1.0.0", [], 0, true, HasDetailUi: true),
            cardContributions: [],
            detailContributions: [detail],
            setEnabled: static (_, _) => { },
            openDetail: viewModel.OpenPluginDetail,
            localize: static key => key);

        Assert.True(item.HasDetailUi);
        item.OpenDetailCommand.Execute(null);

        Assert.Same(item, viewModel.SelectedPlugin);
        Assert.True(viewModel.IsPluginDetailVisible);
        Assert.False(viewModel.IsPluginListVisible);

        viewModel.BackToPluginListCommand.Execute(null);

        Assert.Null(viewModel.SelectedPlugin);
        Assert.True(viewModel.IsPluginListVisible);

        var invalidItem = new PluginItemViewModel(
            new LoadedPluginInfo("test.invalid", "1.0.0", [], 0, true, HasDetailUi: true),
            cardContributions: [],
            detailContributions: [],
            setEnabled: static (_, _) => { },
            openDetail: viewModel.OpenPluginDetail,
            localize: static key => key);

        invalidItem.OpenDetailCommand.Execute(null);

        Assert.Null(viewModel.SelectedPlugin);
        Assert.True(viewModel.IsPluginListVisible);
    }
    [Fact]
    public void UiRefreshQueue_CoalescesPendingWorkPerPluginAndUsesDispatcher()
    {
        var dispatched = new List<Action>();
        var refreshed = new List<string>();
        using var queue = new PluginUiRefreshQueue(dispatched.Add, refreshed.Add);

        queue.Enqueue("plugin-a");
        queue.Enqueue("plugin-a");
        queue.Enqueue("plugin-b");

        Assert.Empty(refreshed);
        Assert.Equal(2, dispatched.Count);

        foreach (var action in dispatched.ToArray())
            action();

        Assert.Equal(["plugin-a", "plugin-b"], refreshed);

        queue.Enqueue("plugin-a");

        Assert.Equal(3, dispatched.Count);
    }
    [Fact]
    public void UiRefreshQueue_DoesNotLoseInvalidationRaisedDuringRefresh()
    {
        PluginUiRefreshQueue? queue = null;
        var refreshCount = 0;
        queue = new PluginUiRefreshQueue(
            dispatch: action => action(),
            refresh: pluginId =>
            {
                refreshCount++;
                if (refreshCount == 1)
                    queue!.Enqueue(pluginId);
            });

        using (queue)
            queue.Enqueue("plugin-a");

        Assert.Equal(2, refreshCount);
    }

    private sealed record PluginListContext(string Status, bool HasLoadError = false)
    {
        public bool IsPluginListVisible => true;
        public bool IsPluginDetailVisible => false;
        public string PluginCountLabel => "2 个插件";
        public string EnabledCountLabel => "2 个启用";
        public string DiagnosticCountLabel => "0 条诊断";
        public bool IsEmpty => false;
        public bool HasDiagnostics => false;
        public IReadOnlyList<object> Plugins => [];
        public IReadOnlyList<string> Diagnostics => [];
    }

    private static PluginItemViewModel CreatePluginItem(PluginsViewModel viewModel)
    {
        var detail = new PluginUiContribution(
            PluginUiContribution.CurrentSchemaVersion,
            "settings",
            PluginUiSlot.DetailBody,
            new PluginUiTextNode("detail"));
        return new PluginItemViewModel(
            new LoadedPluginInfo("test.plugin", "1.2.3", [], 0, true, HasDetailUi: true),
            cardContributions: [],
            detailContributions: [detail],
            setEnabled: static (_, _) => { },
            openDetail: viewModel.OpenPluginDetail,
            localize: static key => key switch
            {
                "plugins.status.enabled" => "已启用",
                "plugins.status.disabled" => "已停用",
                _ => key,
            },
            displayInfo: new PluginDisplayInfo("测试插件", "插件说明", "系统插件", "生命周期说明"));
    }

    private static PluginsView Render(object dataContext)
    {
        AvaloniaTestBootstrap.Ensure();
        var view = new PluginsView { DataContext = dataContext };
        var host = new Window { Content = view };
        host.Measure(new Size(1000, 700));
        host.Arrange(new Rect(0, 0, 1000, 700));
        host.UpdateLayout();
        return view;
    }
}
