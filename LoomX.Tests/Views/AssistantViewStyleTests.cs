using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.Services;
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

        Assert.Null(newSessionButton.RenderTransform);
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
        Assert.Equal(new Thickness(4, 0, -6, 0), messageScrollBar.Margin);
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
    public void 聊天区与输入卡边界相接而视口向下延伸()
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

        Assert.Equal(0, inputCard.Bounds.Top - chatRegion.Bounds.Bottom, 6);
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

    [Fact]
    public void PlainEnterIsInterceptedBeforeMultilineTextBoxHandlesIt()
    {
        AvaloniaTestBootstrap.Ensure();

        using var gatewayService = new GatewayProcessService();
        var viewModel = new AssistantViewModel(gatewayService) { InputText = "测试消息" };
        var sent = false;
        var sendCommandField = typeof(AssistantViewModel).GetField(
            "<SendCommand>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(sendCommandField);
        sendCommandField.SetValue(viewModel, new DelegateCommand(() => sent = true));

        var view = new AssistantView { DataContext = viewModel };
        var host = new Window { Content = view };
        host.Show();
        try
        {
            var input = Assert.IsType<TextBox>(view.FindControl<TextBox>("inputTextBox"));
            input.Text = "测试消息";
            input.CaretIndex = input.Text.Length;
            Assert.True(input.Focus());

            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = input,
                Key = Key.Enter,
                KeyModifiers = KeyModifiers.None,
            };
            input.RaiseEvent(args);

            Assert.True(sent);
            Assert.True(args.Handled);
            Assert.Equal("测试消息", input.Text);
        }
        finally
        {
            host.Close();
        }
    }

    [Theory]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Control)]
    public void ModifiedEnterStillCreatesLineBreak(KeyModifiers modifiers)
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var host = new Window { Content = view };
        host.Show();
        try
        {
            var input = Assert.IsType<TextBox>(view.FindControl<TextBox>("inputTextBox"));
            input.Text = "第一行";
            input.CaretIndex = input.Text.Length;
            Assert.True(input.Focus());

            input.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = input,
                Key = Key.Enter,
                KeyModifiers = modifiers,
            });

            Assert.Equal("第一行" + Environment.NewLine, input.Text);
        }
        finally
        {
            host.Close();
        }
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

    [Fact]
    public void 异常气泡实际控件使用普通字重并可换行()
    {
        AvaloniaTestBootstrap.Ensure();
        var context = new MessageListContext();
        var error = ChatMessageViewModel.Error("错误详情\nHTTP 状态码：402");
        context.Messages.Add(error);
        var view = new AssistantView { DataContext = context };
        var host = new Window { Content = view, Width = 800, Height = 600, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            var text = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == error.Text && item.Bounds.Width > 0);
            Assert.True(text.IsVisible);
            Assert.Equal(14, text.FontSize);
            Assert.Equal(FontWeight.Normal, text.FontWeight);
            Assert.Equal(22, text.LineHeight);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.Equal(Color.Parse("#303639"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            var bubble = Assert.IsType<Border>(text.Parent);
            Assert.True(bubble.IsVisible);
            Assert.Equal(new CornerRadius(18, 18, 18, 6), bubble.CornerRadius);
            Assert.DoesNotContain(bubble.GetVisualDescendants(), child => child is Button);
        }
        finally { host.Close(); }
    }

    [Theory]
    [InlineData(420, false, false)]
    [InlineData(960, false, true)]
    [InlineData(420, true, true)]
    public void 助手按钮完整且视口仅在无批准卡时重叠半个圆角(int width, bool approval, bool multiline)
    {
        AvaloniaTestBootstrap.Ensure();
        var context = new MessageListContext { PendingApproval = approval ? new object() : null };
        for (var index = 0; index < 40; index++)
            context.Messages.Add(ChatMessageViewModel.Status($"消息 {index}"));
        var view = new AssistantView { DataContext = context };
        var host = new Window { Content = view, Width = width, Height = 600, ShowActivated = false };
        host.Show();
        try
        {
            if (multiline) view.FindControl<TextBox>("inputTextBox")!.Text = "第一行\n第二行\n第三行";
            host.UpdateLayout();
            var button = view.FindControl<Button>("newSessionButton")!;
            var position = button.TranslatePoint(default, view)!.Value;
            Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= view.Bounds.Width);
            var region = view.FindControl<Grid>("ChatRegion")!;
            var scroll = view.FindControl<ScrollViewer>("MessageScroll")!;
            var card = view.FindControl<Border>("inputCard")!;
            var bar = view.FindControl<ScrollBar>("MessageScrollBar")!;
            Assert.Equal(new Thickness(0, 10, 0, 0), region.Margin);
            Assert.Equal(approval ? default : new Thickness(0, 0, 0, -7), scroll.Margin);
            Assert.True(card.ZIndex > region.ZIndex);
            if (!approval)
            {
                var scrollBottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), view)!.Value.Y;
                var cardTop = card.TranslatePoint(default, view)!.Value.Y;
                Assert.Equal(card.CornerRadius.TopLeft / 2, scrollBottom - cardTop, 5);
                Assert.True(bar.TranslatePoint(new Point(0, bar.Bounds.Height), view)!.Value.Y <= cardTop);
                var content = Assert.IsType<StackPanel>(scroll.Content);
                Assert.Equal(14, Assert.IsType<Border>(content.Children.Last()).Height);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                host.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                var lastText = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                    item => item.Text == "消息 39" && item.Bounds.Width > 0);
                var lastBottom = lastText.TranslatePoint(new Point(0, lastText.Bounds.Height), view)!.Value.Y;
                Assert.True(lastBottom <= cardTop, $"末条正文底部={lastBottom}，输入卡顶部={cardTop}，Offset={scroll.Offset}，Extent={scroll.Extent}，Viewport={scroll.Viewport}");
            }
        }
        finally { host.Close(); }
    }

    [Fact]
    public void 消息滚动条实际模板采用透明轨道与主题胶囊滑块()
    {
        AvaloniaTestBootstrap.Ensure();
        var context = new MessageListContext();
        for (var index = 0; index < 80; index++) context.Messages.Add(ChatMessageViewModel.Status($"消息 {index}"));
        var view = new AssistantView { DataContext = context };
        var bar = view.FindControl<ScrollBar>("MessageScrollBar")!;
        bar.IsVisible = true;
        bar.Maximum = 1000;
        bar.ViewportSize = 200;
        var host = new Window { Content = view, Width = 600, Height = 600, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            bar.ApplyTemplate();
            var track = Assert.Single(bar.GetVisualDescendants().OfType<Track>(), item => item.Name == "PART_Track");
            Assert.Equal(Orientation.Vertical, track.Orientation);
            Assert.True(track.IsDirectionReversed);
            bar.Value = 500;
            bar.LargeChange = 100;
            track.IncreaseButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(600, bar.Value);
            track.DecreaseButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(500, bar.Value);
            var thumb = Assert.IsType<Thumb>(track.Thumb);
            Assert.True(thumb.Bounds.Height >= 28);
            var capsule = Assert.Single(thumb.GetVisualDescendants().OfType<Border>(), item => item.Name == "Capsule");
            Assert.Equal(4, capsule.Width);
            Assert.Equal(4, capsule.Bounds.Width);
            Assert.True(capsule.Bounds.Height >= 28);
            Assert.Equal(12, thumb.Bounds.Width);
            Assert.Equal(new CornerRadius(2), capsule.CornerRadius);
            Assert.NotNull(capsule.Background);
            Assert.Equal(.45, capsule.Opacity);
            Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Border>(),
                item => item.Name != "Capsule" && item.Background is ISolidColorBrush brush && brush.Color.A > 0);
        }
        finally { host.Close(); }
    }

    private sealed class MessageListContext
    {
        public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
        public bool IsRunning => false;
        public bool HasError => false;
        public object? PendingApproval { get; set; }
        public bool HasNoMessages => Messages.Count == 0;
    }
}
