using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.Views;
using System.Reflection;
using Xunit;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class AssistantViewStyleTests
{
    [Fact]
    public void ChatLayoutUsesAlignedHeaderMultilineComposerAndCompactModelPopup()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var newSessionButton = Assert.IsType<Button>(view.FindControl<Button>("newSessionButton"));
        var input = Assert.IsType<TextBox>(view.FindControl<TextBox>("inputTextBox"));
        var popup = Assert.IsType<Popup>(view.FindControl<Popup>("modelPopup"));
        var popupSurface = Assert.IsType<Border>(popup.Child);

        var transform = Assert.IsType<TranslateTransform>(newSessionButton.RenderTransform);
        Assert.Equal(3, transform.X);
        Assert.True(input.AcceptsReturn);
        Assert.Equal(TextWrapping.Wrap, input.TextWrapping);
        Assert.Equal(ScrollBarVisibility.Auto, input.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Disabled, input.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.Equal(256, popupSurface.Width);
        Assert.Equal(360, popupSurface.MaxHeight);
    }

    [Fact]
    public void ModelPopupUsesDenseProviderAndModelRows()
    {
        AvaloniaTestBootstrap.Ensure();

        var providerButton = new Button();
        providerButton.Classes.Add("model-provider");
        var modelButton = new Button();
        modelButton.Classes.Add("model-option");

        var view = new AssistantView();
        var root = Assert.IsType<Grid>(view.Content);
        root.Children.Add(providerButton);
        root.Children.Add(modelButton);
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));

        Assert.Equal(32, providerButton.Height);
        Assert.Equal(new Thickness(8, 6), providerButton.Padding);
        Assert.Equal(30, modelButton.Height);
        Assert.Equal(new Thickness(10, 5), modelButton.Padding);
    }

    [Fact]
    public void ModelSearchKeepsPopupMaterialVisible()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var modelSearch = Assert.IsType<TextBox>(view.FindControl<TextBox>("modelSearch"));
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));

        Assert.Equal(Brushes.Transparent, modelSearch.Background);
    }

    [Fact]
    public void LongModelNamesUseEllipsisAndFullNameTooltip()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "LoomX", "Views", "AssistantView.axaml"));
        var source = File.ReadAllText(path);

        Assert.Contains("<Style Selector=\"TextBox.model-search:pointerover\">", source, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"TextBox.model-search:focus\">", source, StringComparison.Ordinal);
        Assert.Contains("<Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"10\">", source, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", source, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding DisplayName}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageScrollBarExpandsTowardWindowRight()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));

        var scrollViewer = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
        scrollViewer.ApplyTemplate();
        var scrollBar = Assert.Single(
            scrollViewer.GetVisualDescendants().OfType<ScrollBar>(),
            item => item.Orientation == Orientation.Vertical);
        scrollBar.ApplyTemplate();
        var thumb = Assert.Single(scrollBar.GetVisualDescendants().OfType<Thumb>());

        Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), thumb.RenderTransformOrigin);
    }

    [Theory]
    [InlineData(760, 243.2)]
    [InlineData(600, 192)]
    public void InputMaximumHeightUsesThirtyTwoPercentOfApplicationHeight(double applicationHeight, double expected)
    {
        var method = typeof(AssistantView).GetMethod(
            "CalculateInputMaxHeight",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, Assert.IsType<double>(method.Invoke(null, [applicationHeight])), 6);
    }

    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None, true)]
    [InlineData(Key.Enter, KeyModifiers.Shift, false)]
    [InlineData(Key.A, KeyModifiers.None, false)]
    public void EnterKeyDecisionPreservesShiftEnterLineBreak(Key key, KeyModifiers modifiers, bool expected)
    {
        var method = typeof(AssistantView).GetMethod(
            "ShouldSendMessage",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, Assert.IsType<bool>(method.Invoke(null, [key, modifiers])));
    }

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
