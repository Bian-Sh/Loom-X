using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoomX;
using LoomX.Services;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class ActivityView : UserControl
{
    private ActivityViewModel? observedViewModel;
    private ScrollViewer? activityScrollViewer;
    private bool isAttached;

    public ActivityView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => { isAttached = true; AttachScrollViewer(); };
        DetachedFromVisualTree += (_, _) => { DetachScrollViewer(); isAttached = false; };
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (observedViewModel is not null)
            observedViewModel.ScrollToTopRequested -= OnScrollToTopRequested;
        observedViewModel = DataContext as ActivityViewModel;
        if (observedViewModel is not null)
            observedViewModel.ScrollToTopRequested += OnScrollToTopRequested;
        if (isAttached) AttachScrollViewer();
    }

    private void AttachScrollViewer()
    {
        if (!isAttached) return;
        if (activityScrollViewer is null)
            activityScrollViewer = FindDescendant<ScrollViewer>(activityList);
        if (activityScrollViewer is null)
        {
            Dispatcher.UIThread.Post(AttachScrollViewer, DispatcherPriority.Render);
            return;
        }
        activityScrollViewer.ScrollChanged -= ActivityScrollViewer_OnScrollChanged;
        activityScrollViewer.ScrollChanged += ActivityScrollViewer_OnScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (activityScrollViewer is not null) activityScrollViewer.ScrollChanged -= ActivityScrollViewer_OnScrollChanged;
        activityScrollViewer = null;
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

    private void OnScrollToTopRequested(object? sender, EventArgs args)
    {
        if (activityScrollViewer is not null) activityScrollViewer.Offset = new Vector(0, 0);
    }

    private void ActivityScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer && DataContext is ActivityViewModel viewModel)
            viewModel.NotifyScrollMetrics(scrollViewer.Offset.Y, scrollViewer.Extent.Height, scrollViewer.Viewport.Height);
    }

    private async void CopyRequestId_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ActivityViewModel { SelectedItem: { } selected }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(selected.RequestId);
            (TopLevel.GetTopLevel(this) as MainWindow)?.ToastService.Show("Request ID 已复制", ToastLevel.Success);
        }
    }
}
