using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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

        var headerBounds = new Rect(bounds.X, bounds.Y, bounds.Width, 38 * zoom);
        context.DrawRectangle(headerFill, null, headerBounds, 10 * zoom, 10 * zoom, default);
        context.DrawRectangle(headerFill, null, new Rect(headerBounds.X, headerBounds.Y + 10 * zoom, headerBounds.Width, Math.Max(0, headerBounds.Height - 10 * zoom)));
        context.DrawLine(new Pen(WithAlpha(groupBorder, 0.68), Math.Max(1, zoom)), new Point(bounds.Left + 12 * zoom, headerBounds.Bottom), new Point(bounds.Right - 12 * zoom, headerBounds.Bottom));

        var providerName = Snapshot?.Providers.FirstOrDefault(item => string.Equals(item.Id, provider.ProviderId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? provider.ProviderId;
        if (zoom >= 0.45)
        {
            DrawText(context, providerName, new Rect(bounds.X + 14 * zoom, bounds.Y + 7 * zoom, Math.Max(0, bounds.Width - 28 * zoom), 20 * zoom), text, 14 * zoom, FontWeight.SemiBold, TextAlignment.Left);
            if (zoom >= 0.65)
                DrawText(context, $"{provider.ModelIds.Count} 个模型", new Rect(bounds.X + 14 * zoom, bounds.Y + 23 * zoom, Math.Max(0, bounds.Width - 28 * zoom), 14 * zoom), muted, 10 * zoom, FontWeight.Normal, TextAlignment.Left);
        }
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
        if (zoom >= 0.5)
            DrawText(context, label, new Rect(bounds.X + 12 * zoom, bounds.Y, Math.Max(0, bounds.Width - 24 * zoom), bounds.Height), text, fontSize * zoom, FontWeight.SemiBold, TextAlignment.Left);
    }

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
            MaxTextHeight = bounds.Height,
            TextAlignment = alignment,
            Trimming = TextTrimming.CharacterEllipsis,
            LineHeight = bounds.Height
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
