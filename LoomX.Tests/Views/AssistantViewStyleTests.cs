using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.Assistant;
using LoomX.Assistant.UserDecisions;
using LoomX.Services;
using LoomX.ViewModels;
using LoomX.Views;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Xml.Linq;
using Xunit;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class AssistantViewStyleTests
{
    [Fact]
    public void 用户消息与异常消息正文使用可选择文本控件()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);

        var document = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "Views", "AssistantView.axaml"));
        var selectableMessages = document.Descendants()
            .Where(item => item.Name.LocalName == "SelectableTextBlock" && (string?)item.Attribute("Text") == "{Binding Text}")
            .ToArray();

        Assert.Equal(2, selectableMessages.Length);
        Assert.Contains(selectableMessages, item => item.Ancestors().Any(parent => (string?)parent.Attribute("IsVisible") == "{Binding IsUser}"));
        Assert.Contains(selectableMessages, item => item.Ancestors().Any(parent => (string?)parent.Attribute("IsVisible") == "{Binding IsError}"));
    }
    [Fact]
    public void 中键自动滚动速度包含死区方向与最大速度()
    {
        var method = typeof(AssistantView).GetMethod(
            "CalculateAutoScrollVelocity",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);

        double Calculate(double displacement) => (double)method.Invoke(null, [displacement])!;

        Assert.Equal(0, Calculate(0));
        Assert.Equal(0, Calculate(10));
        Assert.True(Calculate(-40) < 0);
        Assert.True(Calculate(40) > 0);
        Assert.True(Math.Abs(Calculate(120)) > Math.Abs(Calculate(40)));
        Assert.Equal(1800, Calculate(1000));
        Assert.Equal(-1800, Calculate(-1000));
    }

    [Fact]
    public void 中键自动滚动位移不会超过视口边界()
    {
        var method = typeof(AssistantView).GetMethod(
            "CalculateAutoScrollOffset",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);

        double Calculate(double current, double velocity, double elapsed, double maximum) =>
            (double)method.Invoke(null, [current, velocity, elapsed, maximum])!;

        Assert.Equal(65, Calculate(50, 100, 0.15, 200), 6);
        Assert.Equal(0, Calculate(5, -100, 1, 200));
        Assert.Equal(200, Calculate(190, 100, 1, 200));
        Assert.Equal(0, Calculate(50, 100, -1, -20));
    }
    [Fact]
    public void 中键自动滚动显示锚点方向并在退出时恢复状态()
    {
        AvaloniaTestBootstrap.Ensure();
        var context = new MessageListContext();
        for (var index = 0; index < 80; index++) context.Messages.Add(ChatMessageViewModel.Status($"消息 {index}"));

        var view = new AssistantView { DataContext = context };
        var host = new Window { Content = view, Width = 600, Height = 360, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            var scroll = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
            var anchor = Assert.IsType<Border>(view.FindControl<Border>("MessageAutoScrollAnchor"));
            var up = Assert.IsType<AvaloniaPath>(view.FindControl<AvaloniaPath>("MessageAutoScrollUpGlyph"));
            var down = Assert.IsType<AvaloniaPath>(view.FindControl<AvaloniaPath>("MessageAutoScrollDownGlyph"));
            var start = typeof(AssistantView).GetMethod("StartAutoScroll", BindingFlags.Instance | BindingFlags.NonPublic);
            var update = typeof(AssistantView).GetMethod("UpdateAutoScrollFeedback", BindingFlags.Instance | BindingFlags.NonPublic);
            var stop = typeof(AssistantView).GetMethod("StopAutoScroll", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(start);
            Assert.NotNull(update);
            Assert.NotNull(stop);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);

            var originalCursor = scroll.Cursor;
            start.Invoke(view, [new Point(120, 100), null]);
            Assert.True(anchor.IsVisible);
            Assert.NotSame(originalCursor, scroll.Cursor);
            Assert.Equal(104, Canvas.GetLeft(anchor));
            Assert.Equal(84, Canvas.GetTop(anchor));

            update.Invoke(view, [80d]);
            Assert.Equal(0.25, up.Opacity);
            Assert.Equal(1, down.Opacity);
            update.Invoke(view, [-80d]);
            Assert.Equal(1, up.Opacity);
            Assert.Equal(0.25, down.Opacity);

            stop.Invoke(view, null);
            Assert.False(anchor.IsVisible);
            Assert.Same(originalCursor, scroll.Cursor);
        }
        finally
        {
            host.Close();
        }
    }
    [Fact]
    public void 中键指针事件会启动更新并再次按下退出自动滚动()
    {
        AvaloniaTestBootstrap.Ensure();
        var context = new MessageListContext();
        for (var index = 0; index < 80; index++) context.Messages.Add(ChatMessageViewModel.Status($"消息 {index}"));

        var view = new AssistantView { DataContext = context };
        var host = new Window { Content = view, Width = 600, Height = 360, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            var scroll = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
            var anchor = Assert.IsType<Border>(view.FindControl<Border>("MessageAutoScrollAnchor"));
            var down = Assert.IsType<AvaloniaPath>(view.FindControl<AvaloniaPath>("MessageAutoScrollDownGlyph"));
            var pointer = CreateTestPointer();
            var middleProperties = new PointerPointProperties(
                RawInputModifiers.MiddleMouseButton,
                PointerUpdateKind.MiddleButtonPressed);

            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, $"Extent={scroll.Extent.Height}, Viewport={scroll.Viewport.Height}");
            var pressed = new PointerPressedEventArgs(
                scroll,
                pointer,
                scroll,
                new Point(120, 100),
                1,
                middleProperties,
                KeyModifiers.None,
                1);
            Assert.Equal(InputElement.PointerPressedEvent, pressed.RoutedEvent);
            var currentPoint = pressed.GetCurrentPoint(scroll);
            Assert.Equal(PointerUpdateKind.MiddleButtonPressed, currentPoint.Properties.PointerUpdateKind);
            Assert.True(currentPoint.Properties.IsMiddleButtonPressed);
            scroll.RaiseEvent(pressed);

            Assert.True(anchor.IsVisible);
            Assert.Same(scroll, pointer.Captured);

            scroll.RaiseEvent(new PointerEventArgs(
                InputElement.PointerMovedEvent,
                scroll,
                pointer,
                scroll,
                new Point(120, 180),
                2,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None));
            Assert.Equal(1, down.Opacity);

            scroll.RaiseEvent(new PointerPressedEventArgs(
                scroll,
                pointer,
                scroll,
                new Point(120, 180),
                3,
                middleProperties,
                KeyModifiers.None,
                1));

            Assert.False(anchor.IsVisible);
            Assert.Null(pointer.Captured);
        }
        finally
        {
            host.Close();
        }
    }
    [Fact]
    public void ChatLayoutUsesAlignedHeaderMultilineComposerAndCompactModelPopup()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var historyButton = Assert.IsType<Button>(view.FindControl<Button>("historyButton"));
        var newSessionButton = Assert.IsType<Button>(view.FindControl<Button>("newSessionButton"));
        var input = Assert.IsType<TextBox>(view.FindControl<TextBox>("inputTextBox"));
        var messageScroll = Assert.IsType<ScrollViewer>(view.FindControl<ScrollViewer>("MessageScroll"));
        var messageScrollBar = Assert.IsType<ScrollBar>(view.FindControl<ScrollBar>("MessageScrollBar"));
        var popup = Assert.IsType<Popup>(view.FindControl<Popup>("modelPopup"));
        var popupSurface = Assert.IsType<Border>(popup.Child);

        Assert.True(historyButton.RenderTransform is null || historyButton.RenderTransform.Value.IsIdentity);
        Assert.True(newSessionButton.RenderTransform is null || newSessionButton.RenderTransform.Value.IsIdentity);
        var headerActions = Assert.IsType<Grid>(view.FindControl<Grid>("HeaderActions"));
        Assert.Equal(62, headerActions.Width);
        Assert.Equal(new Thickness(0, 0, -16, 0), headerActions.Margin);
        Assert.False(view.FindControl<Grid>("AssistantHeader")!.ClipToBounds);
        Assert.Equal(0, Grid.GetColumn(historyButton));
        Assert.Equal(2, Grid.GetColumn(newSessionButton));
        var host = new Window { Content = view, Width = 1180, Height = 760, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            var historyLeft = historyButton.TranslatePoint(default, view)!.Value.X;
            var newSessionLeft = newSessionButton.TranslatePoint(default, view)!.Value.X;
            Assert.Equal(40, newSessionLeft - historyLeft, 1);
            var newSessionRight = newSessionButton.TranslatePoint(new Point(newSessionButton.Bounds.Width, 0), view)!.Value.X;
            Assert.True(newSessionRight <= view.Bounds.Width, $"新会话按钮右边缘 {newSessionRight} 超出助手视图 {view.Bounds.Width}");
        }
        finally { host.Close(); }
        Assert.True(input.AcceptsReturn);
        Assert.Contains("input-embedded", input.Classes);
        Assert.Contains("composer-input", input.Classes);
        Assert.Null(input.Theme);
        Assert.Equal(TextWrapping.Wrap, input.TextWrapping);
        Assert.Equal(ScrollBarVisibility.Auto, input.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Disabled, input.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        var chatRegion = Assert.IsType<Grid>(messageScroll.Parent);
        var scrollRail = Assert.IsType<Grid>(view.FindControl<Grid>("MessageScrollRail"));
        Assert.Same(scrollRail, messageScrollBar.Parent);
        Assert.Equal(0, Grid.GetColumn(messageScroll));
        Assert.Equal(1, Grid.GetRow(messageScrollBar));
        Assert.Equal(0, Grid.GetRow(scrollRail));
        Assert.Equal(4, Grid.GetRowSpan(scrollRail));
        Assert.Equal(new Thickness(0, 22, 2, 0), scrollRail.Margin);
        Assert.Equal(ScrollBarVisibility.Hidden, messageScroll.VerticalScrollBarVisibility);
        Assert.False(messageScrollBar.AllowAutoHide);
        Assert.Equal(12, messageScrollBar.Width);
        Assert.Equal(256, popupSurface.Width);
        Assert.Equal(360, popupSurface.MaxHeight);
    }


    [Fact]
    public void Markdown彩色图标固定兼容的SkiaSharp运行时()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "LoomX.csproj"));
        var references = document.Descendants("PackageReference")
            .ToDictionary(item => (string)item.Attribute("Include")!, item => (string)item.Attribute("Version")!);

        Assert.Equal("3.119.0", references["SkiaSharp"]);
        Assert.Equal("3.119.0", references["SkiaSharp.NativeAssets.Win32"]);
    }

    [Fact]
    public void 助手正文优先使用对齐文本字体并保留彩色Emoji回退()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "Views", "AssistantView.axaml"));
        var markdownStyle = document.Descendants()
            .Single(item => item.Name.LocalName == "Style" && (string?)item.Attribute("Selector") == "md|MarkdownTextBlock");
        var fontFamily = (string?)markdownStyle.Elements()
            .Single(item => item.Name.LocalName == "Setter" && (string?)item.Attribute("Property") == "FontFamily")
            .Attribute("Value");
        Assert.NotNull(fontFamily);
        var families = fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim('\''))
            .ToArray();

        Assert.Equal("Segoe UI", families[0]);
        Assert.Contains("Microsoft YaHei UI", families);
        Assert.DoesNotContain("Segoe UI Emoji", families);

        var rootFamilies = ((string?)document.Root!.Attribute("FontFamily"))!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim('\''))
            .ToArray();
        Assert.Equal("Segoe UI", rootFamilies[0]);
        Assert.Contains("Microsoft YaHei UI", rootFamilies);
        Assert.Contains("Segoe UI Emoji", rootFamilies);
    }

    [Fact]
    public void 助手Markdown将普通文字与Emoji分配到独立字体运行段()
    {
        var runs = AssistantMarkdownTypography.CreateRuns("中文 LoomX 123 👋");
        var textRun = Assert.Single(runs, run => run.Text?.Contains("LoomX 123", StringComparison.Ordinal) == true);
        var emojiRun = Assert.Single(runs, run => run.Text?.Contains("👋", StringComparison.Ordinal) == true);

        Assert.Equal("Segoe UI", textRun.FontFamily.Name);
        Assert.Equal("Segoe UI Emoji", emojiRun.FontFamily.Name);
        Assert.NotSame(textRun, emojiRun);
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
    public void 底部模型选择器常态具有稳定表面并垂直居中()
    {
        AvaloniaTestBootstrap.Ensure();
        var view = new AssistantView();
        var button = Assert.IsType<Button>(view.FindControl<Button>("modelPickerButton"));
        var host = new Window { Content = view, Width = 1180, Height = 760, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            Assert.Equal(VerticalAlignment.Center, button.VerticalAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            Assert.NotNull(button.Background);
            Assert.NotEqual(Brushes.Transparent, button.Background);
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

        Assert.Contains("Classes=\"model-search input-transparent search\"", source, StringComparison.Ordinal);
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

        var rail = Assert.IsType<Grid>(view.FindControl<Grid>("MessageScrollRail"));
        Assert.NotSame(scrollViewer.Parent, scrollBar.Parent);
        Assert.Same(rail, scrollBar.Parent);
        Assert.Equal(0, Grid.GetColumn(scrollViewer));
        Assert.Equal(1, Grid.GetRow(scrollBar));
        Assert.False(scrollBar.AllowAutoHide);
    }

    [Fact]
    public void 助手视图扩展到主窗口边缘并在内部保留内容Padding()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "App.axaml"));
        var assistantView = Assert.Single(document.Descendants(), item => item.Name.LocalName == "AssistantView");
        Assert.Equal("0,0,-32,-20", (string?)assistantView.Attribute("Margin"));
        var mainWindow = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "MainWindow.axaml"));
        var contentHost = Assert.Single(mainWindow.Descendants(), item => item.Name.LocalName == "Border" && (string?)item.Attribute("Grid.Row") == "1" && (string?)item.Attribute("Padding") == "32,20");
        Assert.Equal("False", (string?)contentHost.Attribute("ClipToBounds"));
        var contentControl = Assert.Single(contentHost.Descendants(), item => item.Name.LocalName == "ContentControl");
        Assert.Equal("False", (string?)contentControl.Attribute("ClipToBounds"));

        AvaloniaTestBootstrap.Ensure();
        var view = new AssistantView();
        Assert.Equal(new Thickness(0, 0, 32, 0), view.FindControl<Grid>("AssistantHeader")!.Margin);
        Assert.Equal(new Thickness(0, 10, 32, 0), view.FindControl<Grid>("ChatRegion")!.Margin);
        Assert.Equal(new Thickness(0, 0, 32, 20), view.FindControl<Border>("inputCard")!.Margin);
        Assert.Equal(new Thickness(0, 22, 2, 0), view.FindControl<Grid>("MessageScrollRail")!.Margin);
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

            var rail = Assert.IsType<Grid>(view.FindControl<Grid>("MessageScrollRail"));
            Assert.True(rail.IsVisible, $"Extent={scrollViewer.Extent.Height}, Viewport={scrollViewer.Viewport.Height}, Maximum={scrollBar.Maximum}");
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

    [Fact]
    public void AskUser选择题自由输入框使用绑定提示与长度()
    {
        AvaloniaTestBootstrap.Ensure();

        var viewModel = new AskUserDialogViewModel(new PendingUserDecision(
            "request-id",
            "owner-id",
            new UserDecisionRequest(
                "确认",
                "请选择运行模式",
                [new UserDecisionField(
                    "mode",
                    "模式",
                    UserDecisionFieldType.SingleSelect,
                    options: [new("safe", "安全")],
                    allowCustomInput: true,
                    maxLength: 64)])));
        var card = new AskUserCard { DataContext = viewModel };
        var host = new Window { Content = card, Width = 520, Height = 480, ShowActivated = false };
        host.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();

            var input = Assert.Single(
                card.GetVisualDescendants().OfType<TextBox>(),
                item => item.IsEffectivelyVisible);
            Assert.Equal("我有其他想法...", input.Watermark);
            Assert.Equal(64, input.MaxLength);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void AskUser结束后排队消息仍保留时不会残留旧卡片()
    {
        AvaloniaTestBootstrap.Ensure();

        using var gatewayService = new GatewayProcessService();
        using var viewModel = new AssistantViewModel(gatewayService);
        viewModel.EnqueueMessageForTesting("session-id", "排队消息");
        viewModel.ShowQueueForSessionForTesting("session-id");
        var pendingAskUserProperty = typeof(AssistantViewModel).GetProperty(nameof(AssistantViewModel.PendingAskUser));
        Assert.NotNull(pendingAskUserProperty);
        pendingAskUserProperty.SetValue(viewModel, new AskUserDialogViewModel(new PendingUserDecision(
            "request-id",
            "owner-id",
            new UserDecisionRequest(
                "确认",
                "请选择",
                [new UserDecisionField("answer", "回答", UserDecisionFieldType.Text, isRequired: true)]))));
        var view = new AssistantView { DataContext = viewModel };
        var host = new Window { Content = view, Width = 1180, Height = 760, ShowActivated = false };
        host.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            var popup = Assert.IsType<Popup>(view.FindControl<Popup>("assistantInteractionOverlay"));
            Assert.True(popup.IsOpen);
            Assert.Contains(popup.Child!.GetVisualDescendants().OfType<AskUserCard>(), card => card.IsEffectivelyVisible);

            pendingAskUserProperty.SetValue(viewModel, null);
            host.UpdateLayout();

            Assert.True(popup.IsOpen);
            Assert.DoesNotContain(popup.Child!.GetVisualDescendants().OfType<AskUserCard>(), card => card.IsEffectivelyVisible);
        }
        finally
        {
            host.Close();
        }
    }
    [Fact]
    public void AskUser与消息队列使用输入框锚定的应用内悬浮层()
    {
        var source = ReadDesktopFile("Views", "AssistantView.axaml");

        Assert.Contains("x:Name=\"assistantInteractionOverlay\"", source, StringComparison.Ordinal);
        Assert.Contains("PlacementTarget=\"{Binding #inputCard}\"", source, StringComparison.Ordinal);
        Assert.Contains("ShouldUseOverlayLayer=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("<views:AskUserCard", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding PendingAskUser}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext=\"{Binding PendingAskUser}\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding QueuedMessages}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DeleteCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"760\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new AskUserDialog {", ReadDesktopFile("ViewModels", "AssistantViewModel.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog<bool?>", ReadDesktopFile("ViewModels", "AssistantViewModel.cs"), StringComparison.Ordinal);
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
    public void 输入区只使用一个可切换发送与停止状态的按钮()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);

        var document = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "Views", "AssistantView.axaml"));
        var buttons = document.Descendants().Where(item => item.Name.LocalName == "Button").ToArray();
        static string? GetName(XElement item) => item.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;
        var actionButton = Assert.Single(buttons, item => GetName(item) == "composerActionButton");

        Assert.Equal("{Binding ComposerActionCommand}", (string?)actionButton.Attribute("Command"));
        Assert.Equal("{Binding IsComposerActionEnabled}", (string?)actionButton.Attribute("IsEnabled"));
        Assert.Equal("{Binding IsComposerStopAction}", (string?)actionButton.Attribute("Classes.stop"));
        Assert.DoesNotContain(buttons, item => (string?)item.Attribute("Command") == "{Binding CancelCommand}");
        Assert.DoesNotContain(buttons, item => GetName(item) == "sendButton");
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
            var historyButton = view.FindControl<Button>("historyButton")!;
            var button = view.FindControl<Button>("newSessionButton")!;
            Assert.True(historyButton.RenderTransform is null || historyButton.RenderTransform.Value.IsIdentity);
            Assert.True(button.RenderTransform is null || button.RenderTransform.Value.IsIdentity);
            var headerActions = view.FindControl<Grid>("HeaderActions")!;
            Assert.Equal(new Thickness(0, 0, -16, 0), headerActions.Margin);
            var region = view.FindControl<Grid>("ChatRegion")!;
            var scroll = view.FindControl<ScrollViewer>("MessageScroll")!;
            var card = view.FindControl<Border>("inputCard")!;
            var bar = view.FindControl<ScrollBar>("MessageScrollBar")!;
            Assert.Equal(new Thickness(0, 10, 32, 0), region.Margin);
            Assert.Equal(new Thickness(0, 0, 0, approval ? 0 : -7), scroll.Margin);
            Assert.True(card.ZIndex > region.ZIndex);
            if (!approval)
            {
                var scrollBottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), view)!.Value.Y;
                var cardTop = card.TranslatePoint(default, view)!.Value.Y;
                Assert.Equal(card.CornerRadius.TopLeft / 2, scrollBottom - cardTop, 5);
                var rail = view.FindControl<Grid>("MessageScrollRail")!;
                var railBottom = rail.TranslatePoint(new Point(0, rail.Bounds.Height), view)!.Value.Y;
                var cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), view)!.Value.Y;
                Assert.True(railBottom >= cardBottom);
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
        var rail = view.FindControl<Grid>("MessageScrollRail")!;
        var bar = view.FindControl<ScrollBar>("MessageScrollBar")!;
        rail.IsVisible = true;
        bar.Maximum = 1000;
        bar.ViewportSize = 200;
        var host = new Window { Content = view, Width = 600, Height = 600, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            bar.ApplyTemplate();
            var track = Assert.Single(bar.GetVisualDescendants().OfType<Track>(), item => item.Name == "PART_Track");
            var lineUp = view.FindControl<RepeatButton>("MessageScrollLineUpButton")!;
            var lineDown = view.FindControl<RepeatButton>("MessageScrollLineDownButton")!;
            var upGlyph = view.FindControl<AvaloniaPath>("MessageScrollUpGlyph")!;
            var downGlyph = view.FindControl<AvaloniaPath>("MessageScrollDownGlyph")!;
            Assert.Same(rail, lineUp.Parent);
            Assert.Same(rail, lineDown.Parent);
            Assert.Equal(0, Grid.GetRow(lineUp));
            Assert.Equal(2, Grid.GetRow(lineDown));
            Assert.NotNull(upGlyph.Fill);
            Assert.NotNull(downGlyph.Fill);
            Assert.Equal(Orientation.Vertical, track.Orientation);
            Assert.True(track.IsDirectionReversed);
            bar.Value = 500;
            bar.SmallChange = 48;
            lineUp.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(452, bar.Value);
            lineDown.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(500, bar.Value);
            bar.LargeChange = 100;
            track.IncreaseButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(600, bar.Value);
            track.DecreaseButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(500, bar.Value);
            var thumb = Assert.IsType<Thumb>(track.Thumb);
            Assert.True(thumb.Bounds.Height >= 28);
            var capsule = Assert.Single(thumb.GetVisualDescendants().OfType<Border>(), item => item.Name == "Capsule");
            Assert.Equal(6, capsule.Width);
            Assert.Equal(6, capsule.Bounds.Width);
            Assert.True(capsule.Bounds.Height >= 28);
            Assert.Equal(12, thumb.Bounds.Width);
            Assert.Equal(new CornerRadius(3), capsule.CornerRadius);
            Assert.NotNull(capsule.Background);
            Assert.Equal(.45, capsule.Opacity);
            Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Border>(),
                item => item.Name != "Capsule" && item.Background is ISolidColorBrush brush && brush.Color.A > 0);
        }
        finally { host.Close(); }
    }

    [Fact]
    public void GlobalScrollBarsUseTransparentTrackAndCapsuleThumb()
    {
        AvaloniaTestBootstrap.Ensure();

        var content = new StackPanel();
        for (var index = 0; index < 20; index++)
        {
            content.Children.Add(new Border { Height = 40 });
        }

        var viewer = new ScrollViewer
        {
            Content = content,
            Width = 200,
            Height = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var host = new Window { Content = viewer, ShowActivated = false };
        host.Show();
        try
        {
            host.UpdateLayout();
            var bar = Assert.Single(viewer.GetVisualDescendants().OfType<ScrollBar>(),
                item => item.Orientation == Orientation.Vertical);
            bar.ApplyTemplate();

            var track = Assert.Single(bar.GetVisualDescendants().OfType<Track>(),
                item => item.Name == "PART_Track");
            var thumb = Assert.IsType<Thumb>(track.Thumb);
            var capsule = Assert.Single(thumb.GetVisualDescendants().OfType<Border>(),
                item => item.Name == "Capsule");

            Assert.Equal(6, capsule.Bounds.Width);
            Assert.Equal(new CornerRadius(3), capsule.CornerRadius);
            Assert.Equal(.45, capsule.Opacity);
            Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Border>(),
                item => item.Name != "Capsule" && item.Background is ISolidColorBrush brush && brush.Color.A > 0);
        }
        finally
        {
            host.Close();
        }
    }
    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
    private static IPointer CreateTestPointer()
    {
        var constructor = typeof(Avalonia.Input.Pointer).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(int), typeof(PointerType), typeof(bool)],
            modifiers: null);
        Assert.NotNull(constructor);
        return (IPointer)constructor.Invoke([1, PointerType.Mouse, true]);
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
