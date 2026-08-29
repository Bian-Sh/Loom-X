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
    private bool following = true;
    public ConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => ScrollToLatest();
        DetachedFromVisualTree += (_, _) => (DataContext as ConsoleViewModel)?.Dispose();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (e is not null && sender is ConsoleView view && view.DataContext is ConsoleViewModel model)
        {
            model.VisibleLogs.CollectionChanged += LogsChanged;
            ScrollToLatest();
        }
    }

    private void LogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (following) ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!following) return;
            LogScroll.Offset = new Vector(LogScroll.Offset.X, LogScroll.Extent.Height);
        }, DispatcherPriority.Loaded);
    }

    private void LogScroll_OnScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;
        following = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 32;
        JumpButton.IsVisible = !following;
    }

    private void JumpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        LogScroll.Offset = new Vector(LogScroll.Offset.X, LogScroll.Extent.Height);
        following = true;
        JumpButton.IsVisible = false;
    }
}
