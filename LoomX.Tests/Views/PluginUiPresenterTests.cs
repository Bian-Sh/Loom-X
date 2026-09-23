using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using LoomX.Controls;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class PluginUiPresenterTests
{
    [Fact]
    public void Presenter_MapsDeclarativeNodesToAvaloniaControls()
    {
        AvaloniaTestBootstrap.Ensure();
        var contribution = new PluginUiContribution(
            PluginUiContribution.CurrentSchemaVersion,
            "summary",
            PluginUiSlot.CardBody,
            new PluginUiGridNode(
                2,
                [
                    new PluginUiGridItem(
                        new PluginUiSurfaceNode(
                            new PluginUiStackNode(
                                [
                                    new PluginUiTextNode(
                                        "15/150",
                                        PluginUiTextRole.Metric,
                                        PluginUiTone.Success,
                                        Wrap: true,
                                        HorizontalAlignment: PluginUiAlignment.Center),
                                    new PluginUiDividerNode(PluginUiTone.Accent),
                                ],
                                Spacing: 7),
                            PluginUiTone.Success,
                            Padding: 12,
                            CornerRadius: 8),
                        ColumnSpan: 1,
                        HorizontalAlignment: PluginUiAlignment.Stretch),
                    new PluginUiGridItem(
                        new PluginUiIconNode(
                            "M 0,0 L 8,0 L 8,8 Z",
                            PluginUiTone.Warning,
                            Size: 18,
                            HorizontalAlignment: PluginUiAlignment.End),
                        HorizontalAlignment: PluginUiAlignment.End),
                ],
                ColumnSpacing: 9,
                RowSpacing: 6));

        var presenter = new PluginUiPresenter { Contributions = [contribution] };

        var grid = Assert.IsType<Grid>(presenter.Content);
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(9, grid.ColumnSpacing);
        Assert.Equal(6, grid.RowSpacing);

        var surface = Assert.IsType<Border>(grid.Children[0]);
        Assert.Contains("plugin-ui-surface", surface.Classes);
        Assert.Contains("tone-success", surface.Classes);
        Assert.Equal(new Avalonia.Thickness(12), surface.Padding);
        Assert.Equal(new Avalonia.CornerRadius(8), surface.CornerRadius);

        var stack = Assert.IsType<StackPanel>(surface.Child);
        Assert.Equal(Orientation.Vertical, stack.Orientation);
        Assert.Equal(7, stack.Spacing);
        var metric = Assert.IsType<TextBlock>(stack.Children[0]);
        Assert.Equal("15/150", metric.Text);
        Assert.Contains("role-metric", metric.Classes);
        Assert.Contains("tone-success", metric.Classes);
        Assert.Equal(TextWrapping.Wrap, metric.TextWrapping);
        Assert.Equal(HorizontalAlignment.Center, metric.HorizontalAlignment);
        Assert.IsType<Border>(stack.Children[1]);

        var icon = Assert.IsType<PathIcon>(grid.Children[1]);
        Assert.Equal(18, icon.Width);
        Assert.Equal(18, icon.Height);
        Assert.Contains("tone-warning", icon.Classes);
        Assert.Equal(HorizontalAlignment.Right, icon.HorizontalAlignment);
    }

    [Fact]
    public void Presenter_IgnoresUnknownOrMalformedNodes()
    {
        AvaloniaTestBootstrap.Ensure();
        var presenter = new PluginUiPresenter
        {
            Contributions =
            [
                new(
                    PluginUiContribution.CurrentSchemaVersion,
                    "unknown",
                    PluginUiSlot.CardBody,
                    new UnknownNode()),
                new(
                    PluginUiContribution.CurrentSchemaVersion,
                    "bad-icon",
                    PluginUiSlot.CardBody,
                    new PluginUiIconNode("not valid geometry")),
            ],
        };

        Assert.Null(presenter.Content);
    }

    private sealed record UnknownNode : PluginUiNode;
}
