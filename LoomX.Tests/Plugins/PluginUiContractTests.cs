using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginUiContractTests
{
    [Fact]
    public void ContributionContract_ExpressesCardAndDetailLayoutsWithoutUiFrameworkTypes()
    {
        var metric = new PluginUiSurfaceNode(
            new PluginUiStackNode(
                [
                    new PluginUiTextNode("已脱敏请求", PluginUiTextRole.Title),
                    new PluginUiTextNode("15/150", PluginUiTextRole.Metric, PluginUiTone.Success),
                    new PluginUiDividerNode(),
                    new PluginUiTextNode("累计脱敏词项 20", PluginUiTextRole.Caption),
                ],
                PluginUiOrientation.Vertical,
                6),
            PluginUiTone.Default,
            Padding: 14,
            CornerRadius: 8);
        var contribution = new PluginUiContribution(
            PluginUiContribution.CurrentSchemaVersion,
            "credential-observability",
            PluginUiSlot.CardBody,
            new PluginUiGridNode(3, [new PluginUiGridItem(metric)]));

        Assert.Equal(1, contribution.SchemaVersion);
        Assert.Equal(PluginUiSlot.CardBody, contribution.Slot);
        Assert.IsType<PluginUiGridNode>(contribution.Root);
        Assert.DoesNotContain(
            typeof(PluginUiContribution).Assembly.GetReferencedAssemblies(),
            name => name.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Provider_InvalidationEventCarriesNoBusinessPayload()
    {
        var provider = new TestProvider();
        var callCount = 0;
        provider.UiInvalidated += (_, _) => callCount++;

        provider.Invalidate();

        Assert.Equal(1, callCount);
        var contribution = Assert.Single(provider.GetUiContributions(new PluginUiContext("zh-CN")));
        Assert.Equal(PluginUiSlot.DetailBody, contribution.Slot);
    }

    private sealed class TestProvider : IPluginUiContributionProvider
    {
        public event EventHandler? UiInvalidated;

        public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) =>
        [
            new PluginUiContribution(
                PluginUiContribution.CurrentSchemaVersion,
                "settings",
                PluginUiSlot.DetailBody,
                new PluginUiTextNode(context.CultureName)),
        ];

        public void Invalidate() => UiInvalidated?.Invoke(this, EventArgs.Empty);
    }
}
