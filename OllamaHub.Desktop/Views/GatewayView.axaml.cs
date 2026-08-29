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

    private async void CopyEndpointButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not GatewayViewModel viewModel || viewModel.SelectedEndpoint is null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(viewModel.SelectedEndpoint.PublicUrl);
    }

    private async void RouteAlias_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GatewayRouteEditorViewModel route } && DataContext is GatewayViewModel viewModel)
            await viewModel.SaveRouteChangesAsync(route);
    }
}
