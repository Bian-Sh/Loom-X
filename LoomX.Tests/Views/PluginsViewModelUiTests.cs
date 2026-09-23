using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class PluginsViewModelUiTests
{
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
            isCredentialProtection: false,
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
            isCredentialProtection: false,
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
}