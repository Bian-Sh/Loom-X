using Avalonia;
using Avalonia.Controls;
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
}
