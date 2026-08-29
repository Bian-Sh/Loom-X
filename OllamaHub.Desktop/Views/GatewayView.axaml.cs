using Avalonia.Controls;
using Avalonia.Input;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public partial class GatewayView : UserControl
{
    public GatewayView()
    {
        InitializeComponent();
        modelPopup.PlacementTarget = modelPickerButton;
        DataContextChanged += (_, _) => modelPopup.DataContext = DataContext;
    }

    private void ToggleModelPicker_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        modelPopup.DataContext = DataContext;
        modelPopup.IsOpen = !modelPopup.IsOpen;
        if (modelPopup.IsOpen) { modelSearch.Text = ""; modelSearch.Focus(); }
    }

    private void ModelSearch_OnTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (DataContext is GatewayViewModel viewModel) viewModel.FilterModels(modelSearch.Text);
    }

    private void ToggleProviderGroup_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: GatewayModelGroup group }) group.IsExpanded = !group.IsExpanded;
    }

    private async void ModelOption_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: GatewayModelOption option } && DataContext is GatewayViewModel viewModel)
        {
            await viewModel.ToggleModelRouteAsync(option);
        }
    }

    private async void CopyEndpointUrl_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GatewayEndpointEditorViewModel endpoint }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(endpoint.PublicUrl);
        if (DataContext is GatewayViewModel viewModel) viewModel.NotifyCopied();
    }

    private async void RouteAlias_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GatewayRouteEditorViewModel route } && DataContext is GatewayViewModel viewModel)
            await viewModel.SaveRouteChangesAsync(route);
    }
}
