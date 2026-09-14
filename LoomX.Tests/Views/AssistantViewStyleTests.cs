using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.Views;
using Xunit;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class AssistantViewStyleTests
{
    [Fact]
    public void ProcessToggle_DefaultStateIsTransparentAndHidesArrow()
    {
        AvaloniaTestBootstrap.Ensure();

        var arrow = new AvaloniaPath();
        arrow.Classes.Add("process-arrow");

        var toggle = new ToggleButton { Content = arrow };
        toggle.Classes.Add("process");

        var view = new AssistantView();
        var root = Assert.IsType<Grid>(view.Content);
        root.Children.Add(toggle);
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));
        toggle.ApplyTemplate();

        Assert.Equal(Brushes.Transparent, toggle.Background);
        Assert.Equal(Brushes.Transparent, toggle.BorderBrush);
        Assert.Equal(0, arrow.Opacity);
    }

    [Fact]
    public void ProcessToggle_CheckedStateKeepsTemplateBackgroundTransparent()
    {
        AvaloniaTestBootstrap.Ensure();

        var toggle = new ToggleButton { Content = "工具调用", IsChecked = true };
        toggle.Classes.Add("process");

        var view = new AssistantView();
        var root = Assert.IsType<Grid>(view.Content);
        root.Children.Add(toggle);
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));
        toggle.ApplyTemplate();

        var presenter = Assert.Single(toggle.GetVisualDescendants().OfType<ContentPresenter>(),
            item => item.Name == "PART_ContentPresenter");
        var visibleBackgrounds = toggle.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Background is not null && border.Background != Brushes.Transparent)
            .Select(border => $"{border.Name ?? "<unnamed>"}: {border.Background}")
            .ToArray();

        Assert.Equal(Brushes.Transparent, presenter.Background);
        Assert.Equal(Brushes.Transparent, presenter.BorderBrush);
        Assert.Empty(visibleBackgrounds);
    }
}
