using Avalonia.Controls;
using Avalonia.Input;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public partial class GatewayView : UserControl
{
    public GatewayView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => modelPopup.DataContext = DataContext;
    }

    private async void OpenModelPicker_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GatewayComboEditorViewModel combo } || DataContext is not GatewayViewModel viewModel) return;

        modelPopup.DataContext = viewModel;
        modelPickerPanel.DataContext = viewModel;
        modelGroupsItemsControl.ItemsSource = viewModel.ModelGroups;
        if (!await viewModel.PrepareModelPickerAsync(combo)) return;

        modelPopup.PlacementTarget = sender as Control;
        modelPopup.IsOpen = true;
        modelSearch.Text = "";
        modelSearch.Focus();
    }

    private void ModelSearch_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is GatewayViewModel viewModel) viewModel.FilterModels(modelSearch.Text);
    }

    private void ToggleProviderGroup_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: GatewayModelGroup group }) group.IsExpanded = !group.IsExpanded;
    }

    private async void ModelOption_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: GatewayModelOption option } && DataContext is GatewayViewModel viewModel) await viewModel.ToggleModelRouteAsync(option);
    }

    private void ToggleCombo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: GatewayComboEditorViewModel combo } && DataContext is GatewayViewModel viewModel)
        {
            combo.IsExpanded = !combo.IsExpanded;
            viewModel.SelectCombo(combo);
        }
    }

    private async void ComboName_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GatewayComboEditorViewModel combo } && DataContext is GatewayViewModel viewModel) await viewModel.SaveComboChangesAsync(combo);
    }

    private async void RouteHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button { Tag: GatewayRouteEditorViewModel route }) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(route.Id.ToString("N")));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private async void Route_OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: GatewayRouteEditorViewModel target } || DataContext is not GatewayViewModel viewModel) return;
        if (Guid.TryParse(e.DataTransfer.TryGetText(), out var routeId)) await viewModel.MoveRouteAsync(viewModel.FindSelectedRoute(routeId), target);
    }

    private async void CopyEndpointUrl_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GatewayEndpointEditorViewModel endpoint }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(endpoint.PublicUrl);
        if (DataContext is GatewayViewModel viewModel) viewModel.NotifyCopied();
    }
}
