using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia;
using OllamaHub.Desktop;
using OllamaHub.Desktop.Services;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public partial class ActivityView : UserControl
{
    private ActivityViewModel? observedViewModel;

    public ActivityView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (observedViewModel is not null)
            observedViewModel.ScrollToTopRequested -= OnScrollToTopRequested;
        observedViewModel = DataContext as ActivityViewModel;
        if (observedViewModel is not null)
            observedViewModel.ScrollToTopRequested += OnScrollToTopRequested;
    }

    private void OnScrollToTopRequested(object? sender, EventArgs args) => activityScrollViewer.Offset = new Vector(0, 0);

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
