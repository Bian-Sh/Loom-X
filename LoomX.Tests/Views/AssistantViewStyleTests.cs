using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.ViewModels;
using LoomX.Views;
using System.Collections.ObjectModel;
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
        var messageScroll = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
        var messageScrollBar = Assert.IsType<ScrollBar>(view.FindControl<ScrollBar>("MessageScrollBar"));
        var popup = Assert.IsType<Popup>(view.FindControl<Popup>("modelPopup"));
        var popupSurface = Assert.IsType<Border>(popup.Child);

        var transform = Assert.IsType<TranslateTransform>(newSessionButton.RenderTransform);
        Assert.Equal(3, transform.X);
        Assert.True(input.AcceptsReturn);
        Assert.Contains("input-embedded", input.Classes);
        Assert.Contains("composer-input", input.Classes);
        Assert.Null(input.Theme);
        Assert.Equal(TextWrapping.Wrap, input.TextWrapping);
        Assert.Equal(ScrollBarVisibility.Auto, input.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Disabled, input.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        var chatRegion = Assert.IsType<Grid>(messageScroll.Parent);
        Assert.Same(chatRegion, messageScrollBar.Parent);
        Assert.Equal(0, Grid.GetColumn(messageScroll));
        Assert.Equal(1, Grid.GetColumn(messageScrollBar));
        Assert.Equal(ScrollBarVisibility.Hidden, messageScroll.VerticalScrollBarVisibility);
        Assert.False(messageScrollBar.AllowAutoHide);
        Assert.Equal(12, messageScrollBar.Width);
        Assert.Equal(new Thickness(4, 0, 0, 0), messageScrollBar.Margin);
        Assert.Equal(256, popupSurface.Width);
        Assert.Equal(360, popupSurface.MaxHeight);
    }

    [Fact]
    public void ComposerUsesSharedMaterialAndPreservesCompositeEditingSurface()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var host = new Window { Content = view };
        var focusSink = new TextBox { Width = 1, Height = 1 };
        Assert.IsType<Grid>(view.Content).Children.Add(focusSink);
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));
        host.Show();
        try
        {
            var input = Assert.IsType<TextBox>(view.FindControl<TextBox>("inputTextBox"));
            var inputCard = Assert.IsType<Border>(view.FindControl<Border>("inputCard"));

            Assert.True(input.Focus());
            input.ApplyTemplate();
            host.UpdateLayout();

            var border = Assert.Single(
                input.GetVisualDescendants().OfType<Border>(),
                item => item.Name == "PART_BorderElement");
            var scrollViewer = Assert.Single(
                input.GetVisualDescendants().OfType<ScrollViewer>(),
                item => item.Name == "PART_ScrollViewer");

            Assert.Contains("focused", inputCard.Classes);
            Assert.Equal(Brushes.Transparent, input.Background);
            Assert.Equal(Brushes.Transparent, border.Background);
            Assert.Equal(Brushes.Transparent, border.BorderBrush);
            Assert.Equal(default, border.BorderThickness);
            Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
            Assert.NotNull(input.CaretBrush);
            Assert.NotNull(input.SelectionBrush);
            Assert.NotNull(input.SelectionForegroundBrush);

            input.Text = "第一行\n第二行";
            input.SelectionStart = 1;
            input.SelectionEnd = 4;
            Assert.Equal("第一行\n第二行", input.Text);
            Assert.Equal(1, input.SelectionStart);
            Assert.Equal(4, input.SelectionEnd);

            Assert.True(focusSink.Focus());
            Assert.DoesNotContain("focused", inputCard.Classes);
        }
        finally
        {
            host.Close();
        }
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
    public void FocusedModelSearchKeepsPopupMaterialVisible()
    {
        AvaloniaTestBootstrap.Ensure();

        var modelSearch = new TextBox();
        modelSearch.Classes.Add("model-search");
        modelSearch.Classes.Add("input-transparent");
        var view = new AssistantView();
        var root = Assert.IsType<Grid>(view.Content);
        root.Children.Add(modelSearch);
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));
        host.Show();
        try
        {
            Assert.True(modelSearch.Focus());
            modelSearch.ApplyTemplate();

            var visibleTemplateBackgrounds = modelSearch.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Background is ISolidColorBrush brush && brush.Color.A > 0)
                .Select(border => $"{border.Name ?? "<unnamed>"}: {border.Background}")
                .ToArray();

            Assert.True(modelSearch.IsFocused);
            Assert.Equal(Brushes.Transparent, modelSearch.Background);
            Assert.Empty(visibleTemplateBackgrounds);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void LongModelNamesUseEllipsisAndFullNameTooltip()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "LoomX", "Views", "AssistantView.axaml"));
        var source = File.ReadAllText(path);

        Assert.Contains("Classes=\"model-search input-transparent\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox.model-search:pointerover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox.model-search:focus", source, StringComparison.Ordinal);
        Assert.Contains("<Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"10\">", source, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", source, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding DisplayName}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalMessageScrollBarLivesOutsideMessageContent()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));

        var scrollViewer = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
        var scrollBar = Assert.IsType<ScrollBar>(view.FindControl<ScrollBar>("MessageScrollBar"));

        Assert.Same(scrollViewer.Parent, scrollBar.Parent);
        Assert.Equal(0, Grid.GetColumn(scrollViewer));
        Assert.Equal(1, Grid.GetColumn(scrollBar));
        Assert.False(scrollBar.AllowAutoHide);
    }

    [Fact]
    public void ExternalMessageScrollBarSynchronizesWithMessageViewport()
    {
        AvaloniaTestBootstrap.Ensure();

        var context = new MessageListContext();
        for (var index = 0; index < 40; index++)
        {
            context.Messages.Add(ChatMessageViewModel.Status($"状态 {index}: 这是一条用于撑高消息区的测试内容。"));
        }

        var view = new AssistantView { DataContext = context };
        var host = new Window
        {
            Content = view,
            Width = 520,
            Height = 320,
            SizeToContent = SizeToContent.Manual
        };
        host.Show();
        try
        {
            host.UpdateLayout();
            var scrollViewer = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
            var scrollBar = Assert.IsType<ScrollBar>(view.FindControl<ScrollBar>("MessageScrollBar"));
            var updateScrollBar = typeof(AssistantView).GetMethod(
                "UpdateMessageScrollBar",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateScrollBar);
            updateScrollBar.Invoke(view, null);

            Assert.True(scrollBar.IsVisible, $"Extent={scrollViewer.Extent.Height}, Viewport={scrollViewer.Viewport.Height}, Maximum={scrollBar.Maximum}");
            Assert.True(scrollBar.Maximum > 0);
            Assert.Equal(scrollViewer.Viewport.Height, scrollBar.ViewportSize, 6);

            scrollBar.Value = scrollBar.Maximum / 2;

            Assert.Equal(scrollBar.Value, scrollViewer.Offset.Y, 6);
        }
        finally
        {
            host.Close();
        }
    }
    [Fact]
    public void ChatRegionKeepsTwoPixelGapAboveComposer()
    {
        AvaloniaTestBootstrap.Ensure();

        var context = new MessageListContext();
        context.Messages.Add(ChatMessageViewModel.Status("状态"));
        var view = new AssistantView { DataContext = context };
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));

        var chatRegion = Assert.IsType<Grid>(view.FindControl<Grid>("ChatRegion"));
        var inputCard = Assert.IsType<Border>(view.FindControl<Border>("inputCard"));

        Assert.Equal(2, inputCard.Bounds.Top - chatRegion.Bounds.Bottom, 6);
    }

    [Fact]
    public void StatusMessagesStayLeftAlignedRegardlessOfTextWidth()
    {
        AvaloniaTestBootstrap.Ensure();

        var context = new MessageListContext();
        context.Messages.Add(ChatMessageViewModel.Status("短状态"));
        context.Messages.Add(ChatMessageViewModel.Status("⚠ 任务失败：\n这是一条更长的错误详情，用来验证不同宽度不会把状态行横向居中。"));

        var view = new AssistantView { DataContext = context };
        var host = new Window { Content = view };
        host.Measure(new Size(1180, 760));
        host.Arrange(new Rect(0, 0, 1180, 760));
        host.UpdateLayout();

        var statusBlocks = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(item => item.IsEffectivelyVisible &&
                item.DataContext is ChatMessageViewModel { IsStatus: true } status &&
                item.Text == status.Text)
            .ToArray();

        Assert.Equal(2, statusBlocks.Length);
        Assert.All(statusBlocks, item => Assert.Equal(HorizontalAlignment.Left, item.HorizontalAlignment));
        var leftEdges = statusBlocks
            .Select(item => item.TranslatePoint(default, view)?.X)
            .Select(value => value ?? double.NaN)
            .ToArray();
        Assert.Equal(leftEdges[0], leftEdges[1], 6);
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
    [InlineData(Key.Enter, KeyModifiers.Control, false)]
    [InlineData(Key.Enter, KeyModifiers.Shift | KeyModifiers.Control, false)]
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

    private sealed class MessageListContext
    {
        public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
        public bool IsRunning => false;
        public bool HasError => false;
        public object? PendingApproval => null;
        public bool HasNoMessages => Messages.Count == 0;
    }
}
