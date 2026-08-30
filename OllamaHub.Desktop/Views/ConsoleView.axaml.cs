using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Threading;
using System.Collections.Specialized;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public partial class ConsoleView : UserControl
{
    private const double BottomThreshold = 4;
    private bool following = true;
    private bool isAttached;
    private bool restoringScroll;
    private double lastOffsetY;
    private ConsoleViewModel? observedModel;
    public ConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => { isAttached = true; SubscribeObservedModel(); RestoreScrollPosition(); };
        DetachedFromVisualTree += (_, _) => { SaveScrollState(); UnsubscribeObservedModel(); isAttached = false; };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeObservedModel();
        observedModel = (sender as ConsoleView)?.DataContext as ConsoleViewModel;
        if (isAttached) SubscribeObservedModel();
        if (isAttached) RestoreScrollPosition();
    }

    private void SubscribeObservedModel()
    {
        if (observedModel is not null) observedModel.VisibleLogs.CollectionChanged += LogsChanged;
    }

    private void UnsubscribeObservedModel()
    {
        if (observedModel is not null) observedModel.VisibleLogs.CollectionChanged -= LogsChanged;
    }

    private void LogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (following) ScrollToLatest(force: true);
    }

    private void ScrollToLatest(bool force = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!force && !following) return;
            SetScrollToBottom();
            following = true;
            JumpButton.IsVisible = false;
            observedModel?.UpdateScrollState(LogScroll.Offset.Y, shouldFollowTail: true);
        }, DispatcherPriority.Render);
    }

    private void RestoreScrollPosition()
    {
        restoringScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            var model = observedModel ?? DataContext as ConsoleViewModel;
            if (model?.FollowTail != false)
            {
                following = true;
                SetScrollToBottom();
                JumpButton.IsVisible = false;
            }
            else
            {
                following = false;
                var maxOffset = MaxScrollOffset();
                LogScroll.Offset = new Vector(LogScroll.Offset.X, Math.Min(model.ScrollOffsetY, maxOffset));
                JumpButton.IsVisible = true;
            }
            lastOffsetY = LogScroll.Offset.Y;
            restoringScroll = false;
            model?.UpdateScrollState(LogScroll.Offset.Y, following);
        }, DispatcherPriority.Render);
    }

    private double MaxScrollOffset() => Math.Max(0, LogScroll.Extent.Height - LogScroll.Viewport.Height);

    private void SetScrollToBottom()
    {
        LogScroll.Offset = new Vector(LogScroll.Offset.X, MaxScrollOffset());
        lastOffsetY = LogScroll.Offset.Y;
    }

    private void SaveScrollState()
    {
        observedModel?.UpdateScrollState(LogScroll.Offset.Y, following);
    }

    private void LogScroll_OnScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;
        if (restoringScroll) return;
        var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var offsetChanged = Math.Abs(scroll.Offset.Y - lastOffsetY) > 0.5;
        if (scroll.Offset.Y >= maxOffset - BottomThreshold)
            following = true;
        else if (offsetChanged)
            following = false;
        JumpButton.IsVisible = !following;
        observedModel?.UpdateScrollState(scroll.Offset.Y, following);
        lastOffsetY = scroll.Offset.Y;
    }

    private void JumpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetScrollToBottom();
        following = true;
        JumpButton.IsVisible = false;
        observedModel?.UpdateScrollState(LogScroll.Offset.Y, shouldFollowTail: true);
    }

    private async void CopyLog_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConsoleLogEntry entry }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(entry.TabSeparated);
        (DataContext as ConsoleViewModel)?.NotifyCopied();
    }
}
