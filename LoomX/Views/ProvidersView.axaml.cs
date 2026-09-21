using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoomX;
using LoomX.ViewModels;
using LoomX.Localization;
using LoomX.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace LoomX.Views;
public sealed class ProviderCompatibilityMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter! : BindingOperations.DoNothing;
}

public sealed class ProviderBooleanNotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not bool boolean || !boolean;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

public partial class ProvidersView : UserControl
{
    private const double TestResponseAutoScrollDeadZone = 10;
    private const double TestResponseAutoScrollSpeedFactor = 12;
    private const double TestResponseAutoScrollMaximumSpeed = 1800;
    private readonly DispatcherTimer testResponseAutoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch testResponseAutoScrollStopwatch = new();
    private ScrollViewer? testResponseScrollViewer;
    private bool testResponseAutoScrollActive;
    private Point testResponseAutoScrollAnchorPoint;
    private Vector testResponseAutoScrollDisplacement;
    private IPointer? testResponseAutoScrollPointer;
    private Cursor? previousTestResponseCursor;
    private Cursor? testResponseAutoScrollCursor;
    private ItemsControl? modelDragItemsControl;
    private Grid? modelDragHost;
    private Border? modelDragPreviewBorder;
    private double modelDragPointerOffsetY;
    private bool ignoreModelCaptureLost;
    private CancellationTokenSource? modelAnimationCancellation;

    public ProvidersView()
    {
        InitializeComponent();
        AddHandler(InputElement.KeyDownEvent, TestResponseTextBox_OnKeyDown, RoutingStrategies.Tunnel, true);
        TestResponseTextBox.AddHandler(InputElement.PointerPressedEvent, TestResponseTextBox_OnPointerPressed, RoutingStrategies.Tunnel, true);
        TestResponseTextBox.AddHandler(InputElement.PointerMovedEvent, TestResponseTextBox_OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        TestResponseTextBox.AddHandler(InputElement.PointerCaptureLostEvent, TestResponseTextBox_OnPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        testResponseAutoScrollTimer.Tick += TestResponseAutoScrollTimer_OnTick;
        DetachedFromVisualTree += (_, _) => StopTestResponseAutoScroll();
        AddHandler(InputElement.PointerMovedEvent, ModelDrag_OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerReleasedEvent, ModelDrag_OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.PointerCaptureLostEvent, ModelDrag_OnPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private void TestResponseTextBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var updateKind = e.GetCurrentPoint(TestResponseTextBox).Properties.PointerUpdateKind;
        if (updateKind == PointerUpdateKind.MiddleButtonPressed)
        {
            if (testResponseAutoScrollActive)
                StopTestResponseAutoScroll();
            else
                StartTestResponseAutoScroll(e.GetPosition(TestResponseAutoScrollOverlay), e.Pointer);

            e.Handled = true;
            return;
        }

        if (!testResponseAutoScrollActive || updateKind is not (
                PointerUpdateKind.LeftButtonPressed or
                PointerUpdateKind.RightButtonPressed or
                PointerUpdateKind.XButton1Pressed or
                PointerUpdateKind.XButton2Pressed)) return;

        StopTestResponseAutoScroll();
        e.Handled = true;
    }

    private void TestResponseTextBox_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!testResponseAutoScrollActive) return;

        var pointerPosition = e.GetPosition(TestResponseAutoScrollOverlay);
        testResponseAutoScrollDisplacement = new Vector(
            pointerPosition.X - testResponseAutoScrollAnchorPoint.X,
            pointerPosition.Y - testResponseAutoScrollAnchorPoint.Y);
        UpdateTestResponseAutoScrollFeedback(testResponseAutoScrollDisplacement);
        e.Handled = true;
    }

    private void TestResponseTextBox_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (testResponseAutoScrollActive) StopTestResponseAutoScroll();
    }

    private void StartTestResponseAutoScroll(Point anchorPoint, IPointer? pointer)
    {
        var scrollViewer = FindTestResponseScrollViewer();
        if (scrollViewer is null) return;

        var horizontalMaximum = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        var verticalMaximum = Math.Max(0, TestTabScrollViewer.Extent.Height - TestTabScrollViewer.Viewport.Height);
        if (testResponseAutoScrollActive || (horizontalMaximum <= 0.5 && verticalMaximum <= 0.5)) return;

        testResponseScrollViewer = scrollViewer;
        testResponseAutoScrollActive = true;
        testResponseAutoScrollAnchorPoint = anchorPoint;
        testResponseAutoScrollDisplacement = default;
        testResponseAutoScrollPointer = pointer;
        previousTestResponseCursor = TestResponseTextBox.Cursor;
        var cursorType = horizontalMaximum > 0.5 && verticalMaximum > 0.5
            ? StandardCursorType.SizeAll
            : horizontalMaximum > 0.5
                ? StandardCursorType.SizeWestEast
                : StandardCursorType.SizeNorthSouth;
        testResponseAutoScrollCursor = new Cursor(cursorType);
        TestResponseTextBox.Cursor = testResponseAutoScrollCursor;
        Canvas.SetLeft(TestResponseAutoScrollAnchor, anchorPoint.X - TestResponseAutoScrollAnchor.Width / 2);
        Canvas.SetTop(TestResponseAutoScrollAnchor, anchorPoint.Y - TestResponseAutoScrollAnchor.Height / 2);
        TestResponseAutoScrollAnchor.IsVisible = true;
        UpdateTestResponseAutoScrollFeedback(default);
        pointer?.Capture(TestResponseTextBox);
        testResponseAutoScrollStopwatch.Restart();
        testResponseAutoScrollTimer.Start();
    }

    private void StopTestResponseAutoScroll()
    {
        if (!testResponseAutoScrollActive) return;

        testResponseAutoScrollActive = false;
        testResponseAutoScrollTimer.Stop();
        testResponseAutoScrollStopwatch.Reset();
        TestResponseAutoScrollAnchor.IsVisible = false;
        TestResponseTextBox.Cursor = previousTestResponseCursor;
        previousTestResponseCursor = null;
        testResponseAutoScrollCursor?.Dispose();
        testResponseAutoScrollCursor = null;
        testResponseAutoScrollDisplacement = default;
        testResponseScrollViewer = null;

        var pointer = testResponseAutoScrollPointer;
        testResponseAutoScrollPointer = null;
        if (pointer?.Captured == TestResponseTextBox) pointer.Capture(null);
    }

    private void UpdateTestResponseAutoScrollFeedback(Vector displacement)
    {
        var horizontalVelocity = CalculateTestResponseAutoScrollVelocity(displacement.X);
        var verticalVelocity = CalculateTestResponseAutoScrollVelocity(displacement.Y);
        TestResponseAutoScrollLeftGlyph.Opacity = horizontalVelocity < 0 ? 1 : horizontalVelocity > 0 ? 0.25 : 0.65;
        TestResponseAutoScrollRightGlyph.Opacity = horizontalVelocity > 0 ? 1 : horizontalVelocity < 0 ? 0.25 : 0.65;
        TestResponseAutoScrollUpGlyph.Opacity = verticalVelocity < 0 ? 1 : verticalVelocity > 0 ? 0.25 : 0.65;
        TestResponseAutoScrollDownGlyph.Opacity = verticalVelocity > 0 ? 1 : verticalVelocity < 0 ? 0.25 : 0.65;
    }

    private void TestResponseAutoScrollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!testResponseAutoScrollActive || testResponseScrollViewer is null) return;

        var elapsedSeconds = testResponseAutoScrollStopwatch.Elapsed.TotalSeconds;
        testResponseAutoScrollStopwatch.Restart();
        var horizontalVelocity = CalculateTestResponseAutoScrollVelocity(testResponseAutoScrollDisplacement.X);
        var verticalVelocity = CalculateTestResponseAutoScrollVelocity(testResponseAutoScrollDisplacement.Y);
        if (horizontalVelocity == 0 && verticalVelocity == 0) return;

        var horizontalMaximum = Math.Max(0, testResponseScrollViewer.Extent.Width - testResponseScrollViewer.Viewport.Width);
        var verticalMaximum = Math.Max(0, TestTabScrollViewer.Extent.Height - TestTabScrollViewer.Viewport.Height);
        var nextHorizontalOffset = CalculateTestResponseAutoScrollOffset(
            testResponseScrollViewer.Offset.X,
            horizontalVelocity,
            elapsedSeconds,
            horizontalMaximum);
        var nextVerticalOffset = CalculateTestResponseAutoScrollOffset(
            TestTabScrollViewer.Offset.Y,
            verticalVelocity,
            elapsedSeconds,
            verticalMaximum);
        var horizontalChanged = Math.Abs(nextHorizontalOffset - testResponseScrollViewer.Offset.X) > 0.01;
        var verticalChanged = Math.Abs(nextVerticalOffset - TestTabScrollViewer.Offset.Y) > 0.01;
        if (!horizontalChanged && !verticalChanged) return;

        if (horizontalChanged)
            testResponseScrollViewer.Offset = new Vector(nextHorizontalOffset, testResponseScrollViewer.Offset.Y);
        if (verticalChanged)
            TestTabScrollViewer.Offset = new Vector(TestTabScrollViewer.Offset.X, nextVerticalOffset);
    }

    private ScrollViewer? FindTestResponseScrollViewer()
    {
        TestResponseTextBox.ApplyTemplate();
        return TestResponseTextBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault(item => item.Name == "PART_ScrollViewer")
            ?? TestResponseTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    internal static double CalculateTestResponseAutoScrollVelocity(double displacement)
    {
        var distance = Math.Abs(displacement) - TestResponseAutoScrollDeadZone;
        if (distance <= 0) return 0;

        var speed = Math.Min(TestResponseAutoScrollMaximumSpeed, distance * TestResponseAutoScrollSpeedFactor);
        return Math.CopySign(speed, displacement);
    }

    internal static double CalculateTestResponseAutoScrollOffset(
        double current,
        double velocity,
        double elapsedSeconds,
        double maximum)
    {
        var safeMaximum = Math.Max(0, maximum);
        var safeElapsed = Math.Max(0, elapsedSeconds);
        return Math.Clamp(current + velocity * safeElapsed, 0, safeMaximum);
    }

    private void TestResponseTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TestResponseTextBox)
            || DataContext is not ProvidersViewModel viewModel)
            return;

        var textLength = TestResponseTextBox.Text?.Length ?? 0;
        if (!ShouldDeleteTestResponseSelection(
                e.Key,
                viewModel.TestPanel.IsRunning,
                textLength,
                TestResponseTextBox.SelectionStart,
                TestResponseTextBox.SelectionEnd)
            || !DeleteSelectedTestResponse(viewModel))
        {
            return;
        }

        e.Handled = true;
    }

    internal static bool ShouldDeleteTestResponseSelection(
        Key key,
        bool isRunning,
        int textLength,
        int selectionStart,
        int selectionEnd)
    {
        if (isRunning
            || textLength == 0
            || key is not (Key.Delete or Key.Back))
            return false;

        var start = Math.Clamp(selectionStart, 0, textLength);
        var end = Math.Clamp(selectionEnd, 0, textLength);
        return start != end;
    }

    private void TestResponseContextMenu_OnOpening(object? sender, CancelEventArgs e)
    {
        var hasSelection = TestResponseTextBox.SelectionStart != TestResponseTextBox.SelectionEnd;
        var canDelete = hasSelection
            && DataContext is ProvidersViewModel viewModel
            && !viewModel.TestPanel.IsRunning;
        CopyTestResponseMenuItem.IsEnabled = hasSelection;
        DeleteTestResponseMenuItem.IsEnabled = canDelete;
        SelectAllTestResponseMenuItem.IsEnabled = !string.IsNullOrEmpty(TestResponseTextBox.Text);
    }

    private async void CopyTestResponseMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TestResponseTextBox.SelectedText)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(TestResponseTextBox.SelectedText);
    }

    private void DeleteTestResponseMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProvidersViewModel viewModel)
            DeleteSelectedTestResponse(viewModel);
    }

    private void SelectAllTestResponseMenuItem_OnClick(object? sender, RoutedEventArgs e)
        => TestResponseTextBox.SelectAll();

    private bool DeleteSelectedTestResponse(ProvidersViewModel viewModel)
    {
        var selectionStart = TestResponseTextBox.SelectionStart;
        var selectionEnd = TestResponseTextBox.SelectionEnd;
        if (!viewModel.TestPanel.DeleteResponseSelection(selectionStart, selectionEnd)) return false;

        var caret = Math.Min(selectionStart, selectionEnd);
        TestResponseTextBox.SelectionStart = caret;
        TestResponseTextBox.SelectionEnd = caret;
        return true;
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

    private void CliIdentityMenuButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel) return;
        // 切换 CLI 身份菜单开合；首次打开时若已有缓存/默认版本则同步显示。
        viewModel.IsCliMenuOpen = !viewModel.IsCliMenuOpen;
        if (viewModel.IsCliMenuOpen)
        {
            viewModel.SelectedProvider?.LoadCliVersionsFromCache();
            viewModel.SelectedProvider?.RefreshCliVersionsIfStale();
        }
    }

    private void CliIdentityApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProvidersViewModel viewModel)
            viewModel.IsCliMenuOpen = false;
    }

    private async void DeleteProviderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProviderEditorViewModel provider } || DataContext is not ProvidersViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not MainWindow owner) return;

        var dialog = new GlassDialogWindow
        {
            Title = ResourceLookup.Resolve("providers.delete.dialog.title"),
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancelButton = new Button { Content = ResourceLookup.Resolve("providers.delete.dialog.cancel"), MinWidth = 76, Classes = { "dialog-action" } };
        var deleteButton = new Button { Content = ResourceLookup.Resolve("providers.delete.dialog.confirm"), MinWidth = 76, Classes = { "dialog-action", "dialog-danger" } };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 12 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);
        dialog.DialogContent = new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.delete.dialog.message"), provider.DisplayName),
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
        if (viewModel.HasModelSearchQuery) return;
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
