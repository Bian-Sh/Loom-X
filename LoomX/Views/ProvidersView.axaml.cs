using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoomX;
using LoomX.ViewModels;

namespace LoomX.Views;
public partial class ProvidersView : UserControl
{
    private ListBox? modelDragList;
    private bool ignoreModelCaptureLost;

    public ProvidersView()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerMovedEvent, ModelDrag_OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerReleasedEvent, ModelDrag_OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerCaptureLostEvent, ModelDrag_OnPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private void AddHeaderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel || viewModel.SelectedProvider is null) return;
        viewModel.SelectedProvider.AddHeader();
    }

    private void RemoveHeaderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HeaderEditorViewModel header } || DataContext is not ProvidersViewModel viewModel || viewModel.SelectedProvider is null) return;
        viewModel.SelectedProvider.RemoveHeader(header);
    }

    private void ToggleApiKeyVisibilityButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProvidersViewModel viewModel)
            viewModel.SelectedProvider?.ToggleApiKeyVisibility();
    }

    private async void DeleteProviderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProviderEditorViewModel provider } || DataContext is not ProvidersViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not MainWindow owner) return;

        var dialog = new GlassDialogWindow
        {
            Title = "提示",
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancelButton = new Button { Content = "取消", MinWidth = 76, Classes = { "dialog-action" } };
        var deleteButton = new Button { Content = "删除", MinWidth = 76, Classes = { "dialog-action", "dialog-danger" } };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 12 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);
        dialog.DialogContent = new TextBlock
        {
            Text = $"确定删除 Provider“{provider.DisplayName}”吗？",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            MaxWidth = 340,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        dialog.DialogActions = buttons;
        cancelButton.Click += (_, _) => dialog.Close(false);
        deleteButton.Click += (_, _) => dialog.Close(true);
        owner.AppearanceCoordinator.ApplyTo(dialog);

        if (await dialog.ShowDialog<bool>(owner)) viewModel.DeleteProviderCommand.Execute(provider);
    }

    private void ModelHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: ModelEditorViewModel model } || DataContext is not ProvidersViewModel viewModel) return;
        var list = sender is Visual visual ? visual.FindAncestorOfType<ListBox>() : null;
        if (list is null || !viewModel.BeginModelDrag(model)) return;
        modelDragList = list;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void ModelDrag_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel || !viewModel.IsModelDragActive || modelDragList is null || e.Pointer.Captured != this) return;
        var pointer = e.GetPosition(modelDragList);
        var targetIndex = GetModelInsertionSlot(pointer.Y, GetModelRows());
        if (viewModel.MoveModelDrag(targetIndex)) modelDragList.UpdateLayout();
        e.Handled = true;
    }

    private async void ModelDrag_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel || !viewModel.IsModelDragActive || e.Pointer.Captured != this) return;
        ignoreModelCaptureLost = true;
        e.Pointer.Capture(null);
        modelDragList = null;
        await viewModel.CompleteModelDragAsync();
        ignoreModelCaptureLost = false;
        e.Handled = true;
    }

    private void ModelDrag_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ignoreModelCaptureLost || DataContext is not ProvidersViewModel viewModel || !viewModel.IsModelDragActive) return;
        modelDragList = null;
        viewModel.CancelModelDrag();
    }

    private IReadOnlyList<ModelRow> GetModelRows()
    {
        if (modelDragList is null) return [];
        var rows = new List<ModelRow>();
        for (var index = 0; index < modelDragList.ItemCount; index++)
        {
            if (modelDragList.ContainerFromIndex(index) is not Visual container || container.DataContext is not ModelEditorViewModel model) continue;
            var top = container.TranslatePoint(new Avalonia.Point(0, 0), modelDragList)?.Y;
            if (top is not null) rows.Add(new ModelRow(model, top.Value, container.Bounds.Height));
        }
        return rows;
    }

    private static int GetModelInsertionSlot(double pointerY, IReadOnlyList<ModelRow> rows)
    {
        var slot = 0;
        foreach (var row in rows)
        {
            if (pointerY < row.Top + row.Height / 2) break;
            slot++;
        }
        return slot;
    }

    private sealed record ModelRow(ModelEditorViewModel Model, double Top, double Height);
}
