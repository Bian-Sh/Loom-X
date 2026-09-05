using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX;
using LoomX.ViewModels;
using System.Diagnostics;

namespace LoomX.Views;
public partial class ProvidersView : UserControl
{
    private ItemsControl? modelDragItemsControl;
    private Grid? modelDragHost;
    private Border? modelDragPreviewBorder;
    private double modelDragPointerOffsetY;
    private bool ignoreModelCaptureLost;
    private CancellationTokenSource? modelAnimationCancellation;

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
        if (sender is not Border handleBorder || handleBorder.Tag is not ModelEditorViewModel model || DataContext is not ProvidersViewModel viewModel) return;
        var modelBorder = handleBorder.FindAncestorOfType<Border>();
        if (modelBorder is null || !modelBorder.Classes.Contains("model-cell")) return;
        var itemsControl = handleBorder.FindAncestorOfType<ItemsControl>();
        if (itemsControl?.DataContext is not ModelEditorViewModel && itemsControl?.DataContext is not ProvidersViewModel) return;
        var host = FindModelDragHost(itemsControl);
        if (host is null)
        {
            itemsControl.UpdateLayout();
            host = FindModelDragHost(itemsControl);
        }
        var preview = host is null ? null : FindVisualDescendant<Border>(host, item => item.Classes.Contains("model-drag-preview"));
        if (host is null || preview is null) return;

        modelDragItemsControl = itemsControl;
        modelDragHost = host;
        modelDragPreviewBorder = preview;
        var pointerPosition = e.GetPosition(modelDragHost);
        var pointerInModel = e.GetPosition(modelBorder);
        var modelTop = pointerPosition.Y - pointerInModel.Y;

        e.Pointer.Capture(this);
        if (!viewModel.BeginModelDrag(model))
        {
            e.Pointer.Capture(null);
            ClearModelDragVisuals();
            return;
        }

        // 以手柄实际按下点为锚点，避免预览在按下时跳到行的另一侧。
        modelDragPointerOffsetY = pointerInModel.Y;
        modelDragPreviewBorder.RenderTransform = new TranslateTransform(0, modelTop);
        e.Handled = true;
    }

    private void ModelDrag_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel || !viewModel.IsModelDragActive || modelDragItemsControl is null || modelDragHost is null || modelDragPreviewBorder is null || e.Pointer.Captured != this) return;

        var pointerPosition = e.GetPosition(modelDragHost);
        var previewTop = Math.Max(0, pointerPosition.Y - modelDragPointerOffsetY);
        if (modelDragPreviewBorder.Bounds.Height > 0) previewTop = Math.Min(previewTop, Math.Max(0, modelDragHost.Bounds.Height - modelDragPreviewBorder.Bounds.Height));
        modelDragPreviewBorder.RenderTransform = new TranslateTransform(0, previewTop);

        var rowsBefore = GetModelRows();
        var previewCenterY = previewTop + modelDragPreviewBorder.Bounds.Height / 2;
        var targetSlot = GetModelInsertionSlot(previewCenterY, rowsBefore);
        var offsets = rowsBefore.Where(row => row.Model.IsRealModel).ToDictionary(row => row.Model, row => row.Top);
        if (viewModel.MoveModelDragPlaceholder(targetSlot))
        {
            modelDragItemsControl.UpdateLayout();
            AnimateMovedRows(offsets, GetModelRows());
        }
        e.Handled = true;
    }

    private async void ModelDrag_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel || !viewModel.IsModelDragActive || e.Pointer.Captured != this) return;
        ignoreModelCaptureLost = true;
        e.Pointer.Capture(null);
        ClearModelDragVisuals();
        await viewModel.CompleteModelDragAsync();
        ignoreModelCaptureLost = false;
        e.Handled = true;
    }

    private void ModelDrag_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ignoreModelCaptureLost || DataContext is not ProvidersViewModel viewModel || !viewModel.IsModelDragActive) return;
        ClearModelDragVisuals();
        viewModel.CancelModelDrag();
    }

    private static Grid? FindModelDragHost(Visual? start)
    {
        for (var current = start; current is not null; current = current.GetVisualParent())
        {
            if (current is not Grid grid) continue;
            if (FindVisualDescendant<ItemsControl>(grid, item => item.ItemCount > 0) is not null &&
                FindVisualDescendant<Border>(grid, item => item.Classes.Contains("model-drag-preview")) is not null)
                return grid;
        }
        return null;
    }

    private static T? FindVisualDescendant<T>(Visual root, Func<T, bool> predicate)
        where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T match && predicate(match)) return match;
            if (FindVisualDescendant(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    private IReadOnlyList<ModelRow> GetModelRows()
    {
        if (modelDragItemsControl is null || modelDragHost is null) return [];
        var result = new List<ModelRow>();
        for (var index = 0; index < modelDragItemsControl.ItemCount; index++)
        {
            if (modelDragItemsControl.ContainerFromIndex(index) is not Visual container) continue;
            var border = FindModelRowBorder(container);
            if (border?.DataContext is not ModelEditorViewModel model) continue;
            var top = modelDragItemsControl.TranslatePoint(new Point(0, container.Bounds.Top), modelDragHost)?.Y;
            if (top is null) continue;
            result.Add(new ModelRow(model, border, top.Value, container.Bounds.Height));
        }
        return result;
    }

    private static Border? FindModelRowBorder(Visual root)
    {
        if (root is Border border && border.IsVisible && (border.Classes.Contains("model-cell") || border.Classes.Contains("model-drag-placeholder"))) return border;
        foreach (var child in root.GetVisualChildren())
        {
            if (FindModelRowBorder(child) is { } result) return result;
        }
        return null;
    }

    private static int GetModelInsertionSlot(double pointerY, IReadOnlyList<ModelRow> rows)
    {
        var slot = 0;
        foreach (var row in rows)
        {
            if (!row.Model.IsRealModel) continue;
            if (pointerY < row.Top + row.Height / 2) break;
            slot++;
        }
        return slot;
    }

    private void AnimateMovedRows(IReadOnlyDictionary<ModelEditorViewModel, double> previous, IReadOnlyList<ModelRow> current)
    {
        modelAnimationCancellation?.Cancel();
        modelAnimationCancellation?.Dispose();
        modelAnimationCancellation = new CancellationTokenSource();
        var token = modelAnimationCancellation.Token;
        var deltas = current.Where(row => row.Model.IsRealModel && previous.TryGetValue(row.Model, out _))
            .Select(row => (row.Border, Delta: previous[row.Model] - row.Top)).Where(item => Math.Abs(item.Delta) > 0.5).ToArray();
        if (deltas.Length == 0) return;
        _ = AnimateRowsAsync(deltas, token);
    }

    private static async Task AnimateRowsAsync((Border Border, double Delta)[] rows, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        const double durationMs = 180;
        try
        {
            while (stopwatch.Elapsed.TotalMilliseconds < durationMs)
            {
                var progress = stopwatch.Elapsed.TotalMilliseconds / durationMs;
                var eased = 1 - Math.Pow(1 - progress, 3);
                foreach (var (border, delta) in rows) border.RenderTransform = new TranslateTransform(0, delta * (1 - eased));
                await Task.Delay(16, cancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            foreach (var (border, _) in rows) border.RenderTransform = null;
        }
    }

    private void ClearModelDragVisuals()
    {
        if (modelDragPreviewBorder is not null) modelDragPreviewBorder.RenderTransform = null;
        modelAnimationCancellation?.Cancel();
        modelAnimationCancellation?.Dispose();
        modelAnimationCancellation = null;
        modelDragItemsControl = null;
        modelDragHost = null;
        modelDragPreviewBorder = null;
    }

    private readonly record struct ModelRow(ModelEditorViewModel Model, Border Border, double Top, double Height);
}
