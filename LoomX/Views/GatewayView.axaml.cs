using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class GatewayView : UserControl
{
    private ItemsControl? dragItemsControl;
    private Grid? dragHost;
    private Border? dragPreview;
    private double dragPointerOffsetY;
    private bool ignoreCaptureLost;
    private CancellationTokenSource? animationCancellation;

    public GatewayView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => modelPopup.DataContext = DataContext;
        AddHandler(InputElement.PointerMovedEvent, RouteDrag_OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerReleasedEvent, RouteDrag_OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerCaptureLostEvent, RouteDrag_OnPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
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

    private void ModelSort_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is GatewayViewModel viewModel) viewModel.ToggleModelSortDirection();
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

    private void RouteHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border routeBorder || routeBorder.Tag is not GatewayRouteEditorViewModel route) return;
        if (DataContext is not GatewayViewModel viewModel) return;
        var itemsControl = routeBorder.FindAncestorOfType<ItemsControl>();
        if (itemsControl?.DataContext is not GatewayComboEditorViewModel combo || !combo.CanDragRoutes) return;
        viewModel.SelectCombo(combo);
        var host = FindRouteDragHost(itemsControl);
        if (host is null)
        {
            itemsControl.UpdateLayout();
            host = FindRouteDragHost(itemsControl);
        }
        var preview = host is null ? null : FindVisualDescendant<Border>(host, item => item.Classes.Contains("route-drag-preview"));
        if (host is null || preview is null) return;

        dragItemsControl = itemsControl;
        dragHost = host;
        dragPreview = preview;

        var pointerPosition = e.GetPosition(dragHost);
        var routeTop = routeBorder.TranslatePoint(new Point(0, 0), dragHost)?.Y ?? pointerPosition.Y;

        // 先捕获到稳定的视图宿主，再更新 ItemsControl，避免列表重排触发捕获丢失并取消拖拽。
        e.Pointer.Capture(this);
        if (!viewModel.BeginRouteDrag(route))
        {
            e.Pointer.Capture(null);
            return;
        }

        dragPointerOffsetY = pointerPosition.Y - routeTop;
        dragPreview.RenderTransform = new TranslateTransform(0, routeTop);
        e.Handled = true;
    }

    private static Grid? FindRouteDragHost(Visual? start)
    {
        for (var current = start; current is not null; current = current.GetVisualParent())
        {
            if (current is not Grid grid) continue;
            if (FindVisualDescendant<ItemsControl>(grid, item => item.ItemCount > 0) is not null &&
                FindVisualDescendant<Border>(grid, item => item.Classes.Contains("route-drag-preview")) is not null)
            {
                return grid;
            }
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

    private void RouteDrag_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not GatewayViewModel viewModel || !viewModel.IsRouteDragActive || dragItemsControl is null || dragHost is null || dragPreview is null || e.Pointer.Captured != this) return;

        var pointerPosition = e.GetPosition(dragHost);
        var previewTop = Math.Max(0, pointerPosition.Y - dragPointerOffsetY);
        if (dragPreview.Bounds.Height > 0) previewTop = Math.Min(previewTop, Math.Max(0, dragHost.Bounds.Height - dragPreview.Bounds.Height));
        dragPreview.RenderTransform = new TranslateTransform(0, previewTop);

        var rowsBefore = GetRouteRows();
        var previewCenterY = previewTop + dragPreview.Bounds.Height / 2;
        var targetSlot = GetInsertionSlot(previewCenterY, rowsBefore);
        var offsets = rowsBefore.Where(row => row.Route.IsRealRoute).ToDictionary(row => row.Route, row => row.Top);
        if (viewModel.MoveRouteDragPlaceholder(targetSlot))
        {
            dragItemsControl.UpdateLayout();
            AnimateMovedRows(offsets, GetRouteRows());
        }
        e.Handled = true;
    }

    private async void RouteDrag_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not GatewayViewModel viewModel || !viewModel.IsRouteDragActive || e.Pointer.Captured != this) return;
        ignoreCaptureLost = true;
        e.Pointer.Capture(null);
        ClearDragVisuals();
        await viewModel.CompleteRouteDragAsync();
        ignoreCaptureLost = false;
        e.Handled = true;
    }

    private void RouteDrag_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ignoreCaptureLost || DataContext is not GatewayViewModel viewModel || !viewModel.IsRouteDragActive) return;
        ClearDragVisuals();
        viewModel.CancelRouteDrag();
    }

    private int GetInsertionSlot(double pointerY, IReadOnlyList<RouteRow> rows)
    {
        var slot = 0;
        foreach (var row in rows)
        {
            if (!row.Route.IsRealRoute) continue;
            if (pointerY < row.Top + row.Height / 2) break;
            slot++;
        }
        return slot;
    }

    private IReadOnlyList<RouteRow> GetRouteRows()
    {
        if (dragItemsControl is null || dragHost is null) return [];
        var result = new List<RouteRow>();
        for (var index = 0; index < dragItemsControl.ItemCount; index++)
        {
            if (dragItemsControl.ContainerFromIndex(index) is not Visual container) continue;
            var border = FindRouteBorder(container);
            if (border?.DataContext is not GatewayRouteEditorViewModel route) continue;
            var top = dragItemsControl.TranslatePoint(new Point(0, container.Bounds.Top), dragHost)?.Y;
            if (top is null) continue;
            result.Add(new RouteRow(route, border, top.Value, container.Bounds.Height));
        }
        return result;
    }

    private static Border? FindRouteBorder(Visual root)
    {
        if (root is Border border && border.IsVisible && border.Classes.Contains("member")) return border;
        foreach (var child in root.GetVisualChildren())
        {
            var result = FindRouteBorder(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void AnimateMovedRows(IReadOnlyDictionary<GatewayRouteEditorViewModel, double> previous, IReadOnlyList<RouteRow> current)
    {
        animationCancellation?.Cancel();
        animationCancellation?.Dispose();
        animationCancellation = new CancellationTokenSource();
        var token = animationCancellation.Token;
        var deltas = current.Where(row => row.Route.IsRealRoute && previous.TryGetValue(row.Route, out _))
            .Select(row => (row.Border, Delta: previous[row.Route] - row.Top)).Where(item => Math.Abs(item.Delta) > 0.5).ToArray();
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

    private void ClearDragVisuals()
    {
        if (dragPreview is not null) dragPreview.RenderTransform = null;
        animationCancellation?.Cancel();
        animationCancellation?.Dispose();
        animationCancellation = null;
        dragItemsControl = null;
        dragHost = null;
        dragPreview = null;
    }

    private readonly record struct RouteRow(GatewayRouteEditorViewModel Route, Border Border, double Top, double Height);

    private async void CopyEndpointUrl_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GatewayEndpointEditorViewModel endpoint }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(endpoint.PublicUrl);
        if (DataContext is GatewayViewModel viewModel) viewModel.NotifyCopied();
    }
}
