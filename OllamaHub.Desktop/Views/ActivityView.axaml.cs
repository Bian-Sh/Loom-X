using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using OllamaHub.Desktop;
using OllamaHub.Desktop.Services;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public partial class ActivityView : UserControl
{
    public ActivityView()
    {
        InitializeComponent();
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
