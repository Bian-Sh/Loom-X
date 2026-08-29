using Avalonia.Controls;
using Avalonia.Interactivity;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public partial class ActivityView : UserControl
{
    public ActivityView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => (DataContext as ActivityViewModel)?.Dispose();
    }

    private async void CopyRequestId_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ActivityViewModel { SelectedItem: { } selected }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(selected.RequestId);
    }
}
