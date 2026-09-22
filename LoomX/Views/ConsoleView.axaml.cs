using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Specialized;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class ConsoleView : UserControl
{
    private const double BottomThreshold = 4;
    private bool following = true;
    private bool isAttached;
    private bool restoringScroll;
    private double lastOffsetY;
    private ScrollViewer? logScroll;
    private ConsoleViewModel? observedModel;
    public ConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => { isAttached = true; SubscribeObservedModel(); AttachLogScroll(); };
        DetachedFromVisualTree += (_, _) => { SaveScrollState(); UnsubscribeObservedModel(); DetachLogScroll(); isAttached = false; };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeObservedModel();
        observedModel = (sender as ConsoleView)?.DataContext as ConsoleViewModel;
        if (isAttached) SubscribeObservedModel();
        if (isAttached) AttachLogScroll();
    }

    private void SubscribeObservedModel()
    {
        if (observedModel is not null) observedModel.VisibleLogs.CollectionChanged += LogsChanged;
    }

    private void UnsubscribeObservedModel()
    {
        if (observedModel is not null) observedModel.VisibleLogs.CollectionChanged -= LogsChanged;
    }

    private void AttachLogScroll()
    {
        if (!isAttached) return;
        if (logScroll is null)
            logScroll = FindDescendant<ScrollViewer>(LogList);
        if (logScroll is null)
        {
            Dispatcher.UIThread.Post(AttachLogScroll, DispatcherPriority.Render);
            return;
        }
        logScroll.ScrollChanged -= LogScroll_OnScroll;
        logScroll.ScrollChanged += LogScroll_OnScroll;
        RestoreScrollPosition();
    }

    private void DetachLogScroll()
    {
        if (logScroll is not null) logScroll.ScrollChanged -= LogScroll_OnScroll;
        logScroll = null;
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T match) return match;
            if (child is Visual visual && FindDescendant<T>(visual) is { } nested) return nested;
        }
        return null;
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
            if (logScroll is null) return;
            SetScrollToBottom();
            following = true;
            JumpButton.IsVisible = false;
            observedModel?.UpdateScrollState(logScroll.Offset.Y, shouldFollowTail: true);
        }, DispatcherPriority.Render);
    }

    private void RestoreScrollPosition()
    {
        restoringScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            var model = observedModel ?? DataContext as ConsoleViewModel;
            if (logScroll is null)
            {
                restoringScroll = false;
                return;
            }
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
                logScroll.Offset = new Vector(logScroll.Offset.X, Math.Min(model.ScrollOffsetY, maxOffset));
                JumpButton.IsVisible = true;
            }
            lastOffsetY = logScroll.Offset.Y;
            restoringScroll = false;
            model?.UpdateScrollState(logScroll.Offset.Y, following);
        }, DispatcherPriority.Render);
    }

    private double MaxScrollOffset() => logScroll is null ? 0 : Math.Max(0, logScroll.Extent.Height - logScroll.Viewport.Height);

    private void SetScrollToBottom()
    {
        if (logScroll is null) return;
        logScroll.Offset = new Vector(logScroll.Offset.X, MaxScrollOffset());
        lastOffsetY = logScroll.Offset.Y;
    }

    private void SaveScrollState()
    {
        observedModel?.UpdateScrollState(logScroll?.Offset.Y ?? 0, following);
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
        observedModel?.UpdateScrollState(logScroll?.Offset.Y ?? 0, shouldFollowTail: true);
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
