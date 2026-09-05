using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoomX.NodeGraph;

/// <summary>
/// LoomX 运行时路由拓扑的只读原生绘制控件。
/// 控件只负责把投影和布局结果绘制出来，不负责读取 Router 配置或解释业务事件。
/// </summary>
public sealed class RuntimeGraphControl : Control
{
    public static readonly StyledProperty<RuntimeGraphSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<RuntimeGraphControl, RuntimeGraphSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<RuntimeGraphLayoutSnapshot?> LayoutProperty =
        AvaloniaProperty.Register<RuntimeGraphControl, RuntimeGraphLayoutSnapshot?>(nameof(Layout));

    static RuntimeGraphControl()
    {
        AffectsMeasure<RuntimeGraphControl>(LayoutProperty, SnapshotProperty);
        AffectsRender<RuntimeGraphControl>(LayoutProperty, SnapshotProperty);
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
        var edgePen = new Pen(WithAlpha(muted, 0.78), 1.5);

        using (context.PushClip(Bounds))
        {
            context.DrawRectangle(background, null, new Rect(Bounds.Size));

            foreach (var edge in layout.Edges)
                context.DrawLine(edgePen, edge.Source, edge.Target);

            foreach (var provider in layout.ProviderGroups.Values.OrderBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase))
                DrawProviderGroup(context, provider, text, muted, border);

            foreach (var node in layout.Nodes.Values
                         .Where(item => item.Kind is RuntimeGraphNodeKind.Endpoint or RuntimeGraphNodeKind.Combo)
                         .OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
            {
                var fill = node.Kind == RuntimeGraphNodeKind.Endpoint
                    ? WithAlpha(live, 0.16)
                    : WithAlpha(border, 0.42);
                DrawNode(context, node.Bounds, NodeLabel(node.NodeId), fill, border, text);
            }

            foreach (var node in layout.Nodes.Values
                         .Where(item => item.Kind == RuntimeGraphNodeKind.Model)
                         .OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
                DrawNode(context, node.Bounds, NodeLabel(node.NodeId), WithAlpha(border, 0.28), border, text, 12);
        }
    }

    private RuntimeGraphLayoutSnapshot? ResolveLayout() =>
        Layout ?? (Snapshot is null ? null : RuntimeGraphLayout.Create(Snapshot));

    private void DrawProviderGroup(
        DrawingContext context,
        RuntimeGraphProviderGroupLayout provider,
        IBrush text,
        IBrush muted,
        IBrush border)
    {
        var groupFill = WithAlpha(border, 0.16);
        var headerFill = WithAlpha(border, 0.28);
        context.DrawRectangle(groupFill, new Pen(WithAlpha(border, 0.92), 1), provider.Bounds, 10, 10, default);

        var headerBounds = new Rect(provider.Bounds.X, provider.Bounds.Y, provider.Bounds.Width, 38);
        context.DrawRectangle(headerFill, null, headerBounds, 10, 10, default);
        context.DrawRectangle(headerFill, null, new Rect(headerBounds.X, headerBounds.Y + 10, headerBounds.Width, headerBounds.Height - 10));
        context.DrawLine(new Pen(WithAlpha(border, 0.68), 1), new Point(provider.Bounds.Left + 12, headerBounds.Bottom), new Point(provider.Bounds.Right - 12, headerBounds.Bottom));

        var providerName = Snapshot?.Providers.FirstOrDefault(item => string.Equals(item.Id, provider.ProviderId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? provider.ProviderId;
        DrawText(context, providerName, new Rect(provider.Bounds.X + 14, provider.Bounds.Y + 7, provider.Bounds.Width - 28, 20), text, 14, FontWeight.SemiBold, TextAlignment.Left);
        DrawText(context, $"{provider.ModelIds.Count} 个模型", new Rect(provider.Bounds.X + 14, provider.Bounds.Y + 23, provider.Bounds.Width - 28, 14), muted, 10, FontWeight.Normal, TextAlignment.Left);
    }

    private static void DrawNode(
        DrawingContext context,
        Rect bounds,
        string label,
        IBrush fill,
        IBrush border,
        IBrush text,
        double fontSize = 14)
    {
        context.DrawRectangle(fill, new Pen(WithAlpha(border, 0.9), 1), bounds, 8, 8, default);
        DrawText(context, label, new Rect(bounds.X + 12, bounds.Y, Math.Max(0, bounds.Width - 24), bounds.Height), text, fontSize, FontWeight.SemiBold, TextAlignment.Left);
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
