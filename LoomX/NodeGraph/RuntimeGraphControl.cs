using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoomX.NodeGraph;

/// <summary>
/// LoomX 运行时路由拓扑的只读原生绘制控件。
/// 控件只负责把投影和布局结果绘制出来，不负责读取 Router 配置或解释业务事件。
/// </summary>
public sealed class RuntimeGraphControl : Control
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 2.5;
    public const double ZoomStep = 1.2;
    public const double KindWatermarkMinZoom = 1.0;

    public static readonly StyledProperty<RuntimeGraphSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<RuntimeGraphControl, RuntimeGraphSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<RuntimeGraphLayoutSnapshot?> LayoutProperty =
        AvaloniaProperty.Register<RuntimeGraphControl, RuntimeGraphLayoutSnapshot?>(nameof(Layout));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<RuntimeGraphControl, double>(nameof(Zoom), 1d);

    public static readonly StyledProperty<Vector> PanProperty =
        AvaloniaProperty.Register<RuntimeGraphControl, Vector>(nameof(Pan), default);

    private Point pointerDownPosition;
    private Vector panAtPointerDown;
    private bool pointerDownOnNode;
    private bool isPanning;

    static RuntimeGraphControl()
    {
        AffectsMeasure<RuntimeGraphControl>(LayoutProperty, SnapshotProperty);
        AffectsRender<RuntimeGraphControl>(LayoutProperty, SnapshotProperty, ZoomProperty, PanProperty);
        SnapshotProperty.Changed.AddClassHandler<RuntimeGraphControl>((control, _) => control.ScheduleFitToView());
        LayoutProperty.Changed.AddClassHandler<RuntimeGraphControl>((control, _) => control.ScheduleFitToView());
    }

    public RuntimeGraphSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public RuntimeGraphLayoutSnapshot? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom));
    }

    public Vector Pan
    {
        get => GetValue(PanProperty);
        set => SetValue(PanProperty, value);
    }

    public double FitPadding { get; set; } = 24;

    public RuntimeGraphSelection? Selection { get; private set; }

    public event EventHandler<RuntimeGraphSelectionChangedEventArgs>? SelectionChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        var contentSize = ResolveLayout()?.ContentBounds.Size ?? new Size(0, 0);
        var width = double.IsPositiveInfinity(availableSize.Width)
            ? contentSize.Width
            : Math.Min(contentSize.Width, availableSize.Width);
        var height = double.IsPositiveInfinity(availableSize.Height)
            ? contentSize.Height
            : Math.Min(contentSize.Height, availableSize.Height);
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var layout = ResolveLayout();
        if (layout is null) return;

        var background = ResolveBrush("GraphBackgroundBrush", Brushes.Transparent);
        var border = ResolveBrush("GraphBorderBrush", Brushes.Gray);
        var text = ResolveBrush("GraphTextBrush", Brushes.White);
        var muted = ResolveBrush("GraphMutedBrush", Brushes.LightGray);
        var live = ResolveBrush("GraphLiveBrush", Brushes.LightGreen);
        var zoom = EffectiveZoom;

        using (context.PushClip(Bounds))
        {
            context.DrawRectangle(background, null, new Rect(Bounds.Size));

            foreach (var edge in layout.Edges)
            {
                var selected = IsEdgeRelated(edge.EdgeId);
                var edgeBrush = selected ? live : muted;
                var edgePen = new Pen(WithAlpha(edgeBrush, selected ? 0.95 : 0.78), selected ? 2.4 : 1.5);
                context.DrawLine(edgePen, ToViewport(edge.Source), ToViewport(edge.Target));
            }

            foreach (var provider in layout.ProviderGroups.Values.OrderBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase))
                DrawProviderGroup(context, provider, text, muted, border, zoom);

            foreach (var node in layout.Nodes.Values
                         .Where(item => item.Kind is RuntimeGraphNodeKind.Endpoint or RuntimeGraphNodeKind.Combo)
                         .OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
            {
                var fill = node.Kind == RuntimeGraphNodeKind.Endpoint
                    ? WithAlpha(live, 0.16)
                    : WithAlpha(border, 0.42);
                DrawNode(context, node, NodeLabel(node.NodeId), fill, border, text, zoom);
            }

            foreach (var node in layout.Nodes.Values
                         .Where(item => item.Kind == RuntimeGraphNodeKind.Model)
                         .OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
                DrawNode(context, node, NodeLabel(node.NodeId), WithAlpha(border, 0.28), border, text, zoom, 12);
        }
    }

    public void ZoomIn() => ZoomAround(EffectiveZoom * ZoomStep, new Point(Bounds.Width / 2, Bounds.Height / 2));

    public void ZoomOut() => ZoomAround(EffectiveZoom / ZoomStep, new Point(Bounds.Width / 2, Bounds.Height / 2));

    public void FitToView() => FitToView(Bounds.Size);

    public void FitToView(Size viewport)
    {
        var layout = ResolveLayout();
        if (layout is null || viewport.Width <= 0 || viewport.Height <= 0) return;

        var content = layout.ContentBounds;
        var availableWidth = Math.Max(1, viewport.Width - FitPadding * 2);
        var availableHeight = Math.Max(1, viewport.Height - FitPadding * 2);
        var fitZoom = Math.Clamp(
            Math.Min(availableWidth / Math.Max(1, content.Width), availableHeight / Math.Max(1, content.Height)),
            MinZoom,
            MaxZoom);
        Zoom = fitZoom;
        Pan = new Vector(
            (viewport.Width - content.Width * fitZoom) / 2 - content.X * fitZoom,
            (viewport.Height - content.Height * fitZoom) / 2 - content.Y * fitZoom);
    }

    public bool FocusEndpoint(string endpointId) => FocusEndpoint(endpointId, Bounds.Size);

    public bool FocusEndpoint(string endpointId, Size viewport)
    {
        if (string.IsNullOrWhiteSpace(endpointId) || viewport.Width <= 0 || viewport.Height <= 0) return false;
        var layout = ResolveLayout();
        if (layout is null || !layout.Nodes.TryGetValue(endpointId, out var node)
            || node.Kind != RuntimeGraphNodeKind.Endpoint) return false;

        SetSelection(new RuntimeGraphSelection(RuntimeGraphSelectionKind.Node, endpointId));
        var zoom = Math.Clamp(Math.Max(EffectiveZoom, 0.85), MinZoom, MaxZoom);
        Zoom = zoom;
        Pan = new Vector(
            viewport.Width / 2 - node.Bounds.Center.X * zoom,
            viewport.Height / 2 - node.Bounds.Center.Y * zoom);
        return true;
    }

    public RuntimeGraphSelection? Pick(Point viewportPoint)
    {
        var layout = ResolveLayout();
        if (layout is null) return null;
        var graphPoint = FromViewport(viewportPoint);

        foreach (var node in layout.Nodes.Values.OrderByDescending(item => item.Kind == RuntimeGraphNodeKind.Model))
            if (node.Bounds.Contains(graphPoint))
                return new RuntimeGraphSelection(RuntimeGraphSelectionKind.Node, node.NodeId);

        foreach (var provider in layout.ProviderGroups.Values)
            if (provider.Bounds.Contains(graphPoint))
                return new RuntimeGraphSelection(RuntimeGraphSelectionKind.ProviderGroup, provider.ProviderId);

        return null;
    }

    public RuntimeGraphSelection? SelectAt(Point viewportPoint)
    {
        var selection = Pick(viewportPoint);
        SetSelection(selection);
        return selection;
    }

    public RuntimeGraphSelectionDetails? GetSelectionDetails()
    {
        if (Selection is null || Snapshot is null) return null;
        if (Selection.Kind == RuntimeGraphSelectionKind.ProviderGroup)
        {
            var provider = Snapshot.Providers.FirstOrDefault(item => string.Equals(item.Id, Selection.Id, StringComparison.OrdinalIgnoreCase));
            return provider is null
                ? null
                : new RuntimeGraphSelectionDetails(
                    Selection.Id,
                    Selection.Kind,
                    provider.DisplayName,
                    provider.Enabled,
                    provider.BaseUrl,
                    provider.Protocol,
                    null,
                    null,
                    provider.Models.Count);
        }

        var node = Snapshot.Endpoints.FirstOrDefault(item => string.Equals(item.Id, Selection.Id, StringComparison.OrdinalIgnoreCase))
            ?? Snapshot.Combos.FirstOrDefault(item => string.Equals(item.Id, Selection.Id, StringComparison.OrdinalIgnoreCase))
            ?? Snapshot.Models.FirstOrDefault(item => string.Equals(item.Id, Selection.Id, StringComparison.OrdinalIgnoreCase));
        if (node is null) return null;
        return new RuntimeGraphSelectionDetails(
            node.Id,
            Selection.Kind,
            node.DisplayName,
            node.Enabled,
            null,
            null,
            node.EndpointId,
            node.ProviderId,
            null);
    }

    public void ClearSelection() => SetSelection(null);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var updateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        var middleButton = updateKind == PointerUpdateKind.MiddleButtonPressed;
        var leftButton = updateKind == PointerUpdateKind.LeftButtonPressed;
        if (!middleButton && !leftButton) return;

        pointerDownPosition = point;
        panAtPointerDown = Pan;
        pointerDownOnNode = Pick(point) is not null;
        isPanning = middleButton || (leftButton && !pointerDownOnNode);
        if (isPanning)
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isPanning || e.Pointer.Captured != this) return;
        var point = e.GetPosition(this);
        Pan = panAtPointerDown + point - pointerDownPosition;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetPosition(this);
        var updateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        var pointerDelta = point - pointerDownPosition;
        var pointerDistance = Math.Sqrt(pointerDelta.X * pointerDelta.X + pointerDelta.Y * pointerDelta.Y);
        if (isPanning && updateKind is PointerUpdateKind.LeftButtonReleased or PointerUpdateKind.MiddleButtonReleased)
        {
            isPanning = false;
            e.Pointer.Capture(null);
            if (updateKind == PointerUpdateKind.LeftButtonReleased && !pointerDownOnNode && pointerDistance <= 4)
                SetSelection(null);
            e.Handled = true;
            return;
        }

        if (updateKind == PointerUpdateKind.LeftButtonReleased && pointerDownOnNode && pointerDistance <= 4)
        {
            SetSelection(Pick(point));
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        isPanning = false;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        ZoomAround(e.Delta.Y > 0 ? EffectiveZoom * ZoomStep : EffectiveZoom / ZoomStep, e.GetPosition(this));
        e.Handled = true;
    }

    private double EffectiveZoom
    {
        get
        {
            var value = GetValue(ZoomProperty);
            return double.IsFinite(value) ? Math.Clamp(value, MinZoom, MaxZoom) : 1;
        }
    }

    private Point ToViewport(Point graphPoint)
    {
        var zoom = EffectiveZoom;
        return new Point(graphPoint.X * zoom + Pan.X, graphPoint.Y * zoom + Pan.Y);
    }

    private Rect ToViewport(Rect graphBounds)
    {
        var topLeft = ToViewport(graphBounds.TopLeft);
        var zoom = EffectiveZoom;
        return new Rect(topLeft, new Size(graphBounds.Width * zoom, graphBounds.Height * zoom));
    }

    private Point FromViewport(Point viewportPoint)
    {
        var zoom = EffectiveZoom;
        return new Point((viewportPoint.X - Pan.X) / zoom, (viewportPoint.Y - Pan.Y) / zoom);
    }

    private void ZoomAround(double requestedZoom, Point viewportAnchor)
    {
        var nextZoom = Math.Clamp(requestedZoom, MinZoom, MaxZoom);
        var currentZoom = EffectiveZoom;
        if (Math.Abs(nextZoom - currentZoom) < 0.0001) return;

        var graphAnchor = FromViewport(viewportAnchor);
        Zoom = nextZoom;
        Pan = new Vector(
            viewportAnchor.X - graphAnchor.X * nextZoom,
            viewportAnchor.Y - graphAnchor.Y * nextZoom);
    }

    private void SetSelection(RuntimeGraphSelection? selection)
    {
        if (Equals(Selection, selection)) return;
        var previous = Selection;
        Selection = selection;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, new RuntimeGraphSelectionChangedEventArgs(previous, selection));
    }

    private bool IsEdgeRelated(string edgeId)
    {
        if (Selection is null || Snapshot is null) return false;
        var edge = Snapshot.Edges.FirstOrDefault(item => string.Equals(item.Id, edgeId, StringComparison.OrdinalIgnoreCase));
        if (edge is null) return false;
        return string.Equals(edge.SourceId, Selection.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(edge.TargetId, Selection.Id, StringComparison.OrdinalIgnoreCase);
    }

    private RuntimeGraphLayoutSnapshot? ResolveLayout() =>
        Layout ?? (Snapshot is null ? null : RuntimeGraphLayout.Create(Snapshot));

    private void ScheduleFitToView()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || ResolveLayout() is null) return;
        Dispatcher.UIThread.Post(FitToView, DispatcherPriority.Loaded);
    }

    private void DrawProviderGroup(
        DrawingContext context,
        RuntimeGraphProviderGroupLayout provider,
        IBrush text,
        IBrush muted,
        IBrush border,
        double zoom)
    {
        var groupFill = WithAlpha(border, 0.16);
        var headerFill = WithAlpha(border, 0.28);
        var bounds = ToViewport(provider.Bounds);
        var selected = Selection is { Kind: RuntimeGraphSelectionKind.ProviderGroup } selection
            && string.Equals(selection.Id, provider.ProviderId, StringComparison.OrdinalIgnoreCase);
        var groupBorder = selected ? ResolveBrush("GraphLiveBrush", Brushes.LightGreen) : border;
        context.DrawRectangle(groupFill, new Pen(WithAlpha(groupBorder, selected ? 1 : 0.92), selected ? 2 : 1), bounds, 10 * zoom, 10 * zoom, default);

        var headerInset = Math.Max(1, zoom);
        var headerBounds = new Rect(
            bounds.X + headerInset,
            bounds.Y + headerInset,
            Math.Max(0, bounds.Width - headerInset * 2),
            Math.Max(0, 38 * zoom - headerInset));
        context.DrawRectangle(headerFill, null, headerBounds);
        context.DrawLine(
            new Pen(WithAlpha(groupBorder, 0.68), Math.Max(1, zoom)),
            new Point(headerBounds.Left, headerBounds.Bottom),
            new Point(headerBounds.Right, headerBounds.Bottom));

        var providerName = Snapshot?.Providers.FirstOrDefault(item => string.Equals(item.Id, provider.ProviderId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? provider.ProviderId;
        if (zoom >= 0.25)
        {
            var countWidth = Math.Min(100 * zoom, Math.Max(0, headerBounds.Width * 0.42));
            DrawText(
                context,
                providerName,
                new Rect(headerBounds.X + 12 * zoom, headerBounds.Y, Math.Max(0, headerBounds.Width - countWidth - 24 * zoom), headerBounds.Height),
                text,
                Math.Max(9, 14 * zoom),
                FontWeight.SemiBold,
                TextAlignment.Left);
            if (zoom >= 0.4)
                DrawText(
                    context,
                    $"{provider.ModelIds.Count} 个模型",
                    new Rect(headerBounds.Right - countWidth - 12 * zoom, headerBounds.Y, Math.Max(0, countWidth), headerBounds.Height),
                    muted,
                    Math.Max(8, 10 * zoom),
                    FontWeight.Normal,
                    TextAlignment.Right);
        }

        DrawKindWatermark(context, "Provider", bounds, zoom, bottomInset: 3);
    }

    private void DrawNode(
        DrawingContext context,
        RuntimeGraphNodeLayout node,
        string label,
        IBrush fill,
        IBrush border,
        IBrush text,
        double zoom,
        double fontSize = 14)
    {
        var bounds = ToViewport(node.Bounds);
        var selected = Selection is { Kind: RuntimeGraphSelectionKind.Node } selection
            && string.Equals(selection.Id, node.NodeId, StringComparison.OrdinalIgnoreCase);
        var nodeBorder = selected ? ResolveBrush("GraphLiveBrush", Brushes.LightGreen) : border;
        context.DrawRectangle(fill, new Pen(WithAlpha(nodeBorder, selected ? 1 : 0.9), selected ? 2 : 1), bounds, 8 * zoom, 8 * zoom, default);
        if (zoom >= 0.25)
            DrawText(context, label, new Rect(bounds.X + 12 * zoom, bounds.Y, Math.Max(0, bounds.Width - 24 * zoom), bounds.Height), text, Math.Max(8, fontSize * zoom), FontWeight.SemiBold, TextAlignment.Left);
        DrawKindWatermark(context, NodeKindLabel(node.Kind), bounds, zoom);
    }

    private static void DrawKindWatermark(
        DrawingContext context,
        string label,
        Rect bounds,
        double zoom,
        double bottomInset = 5)
    {
        if (zoom < KindWatermarkMinZoom || string.IsNullOrWhiteSpace(label)) return;

        var textHeight = Math.Max(10, 11 * zoom);
        var textWidth = Math.Min(78 * zoom, Math.Max(0, bounds.Width - 16 * zoom));
        if (textWidth <= 0 || bounds.Height <= textHeight) return;

        var brush = new SolidColorBrush(Color.FromArgb(125, 145, 167, 177));
        DrawText(
            context,
            label,
            new Rect(
                bounds.Right - textWidth - 8 * zoom,
                bounds.Bottom - textHeight - bottomInset * zoom,
                textWidth,
                textHeight),
            brush,
            Math.Clamp(9 * zoom, 7, 11),
            FontWeight.Normal,
            TextAlignment.Right);
    }

    private static string NodeKindLabel(RuntimeGraphNodeKind kind) => kind switch
    {
        RuntimeGraphNodeKind.Endpoint => "Endpoint",
        RuntimeGraphNodeKind.Combo => "Combo",
        RuntimeGraphNodeKind.Model => "Model",
        _ => kind.ToString()
    };

    private string NodeLabel(string nodeId)
    {
        if (Snapshot is null) return nodeId;
        return Snapshot.Endpoints.FirstOrDefault(item => string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? Snapshot.Combos.FirstOrDefault(item => string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? Snapshot.Models.FirstOrDefault(item => string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? nodeId;
    }

    private static void DrawText(
        DrawingContext context,
        string value,
        Rect bounds,
        IBrush brush,
        double fontSize,
        FontWeight weight,
        TextAlignment alignment)
    {
        if (string.IsNullOrWhiteSpace(value) || bounds.Width <= 0 || bounds.Height <= 0) return;
        var formatted = new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, weight),
            fontSize,
            brush)
        {
            MaxTextWidth = bounds.Width,
            TextAlignment = alignment,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var y = bounds.Y + Math.Max(0, (bounds.Height - formatted.Height) / 2);
        context.DrawText(formatted, new Point(bounds.X, y));
    }

    private IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (TryGetResource(key, null, out var value) && value is IBrush brush) return brush;
        if (Application.Current is { } application && application.TryGetResource(key, null, out value) && value is IBrush applicationBrush)
            return applicationBrush;
        return fallback;
    }

    private static IBrush WithAlpha(IBrush brush, double opacity)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var alpha = (byte)Math.Clamp(Math.Round(solid.Color.A * Math.Clamp(opacity, 0, 1)), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
    }
}

public enum RuntimeGraphSelectionKind
{
    Node,
    ProviderGroup
}

public sealed record RuntimeGraphSelection(RuntimeGraphSelectionKind Kind, string Id);

public sealed class RuntimeGraphSelectionChangedEventArgs(
    RuntimeGraphSelection? previous,
    RuntimeGraphSelection? current) : EventArgs
{
    public RuntimeGraphSelection? Previous { get; } = previous;
    public RuntimeGraphSelection? Current { get; } = current;
}

public sealed record RuntimeGraphSelectionDetails(
    string Id,
    RuntimeGraphSelectionKind Kind,
    string DisplayName,
    bool Enabled,
    string? BaseUrl,
    string? Protocol,
    string? EndpointId,
    string? ProviderId,
    int? ModelCount);
