using Avalonia;

namespace LoomX.NodeGraph;

public sealed record RuntimeGraphLayoutOptions
{
    public double NodeWidth { get; init; } = 180;
    public double NodeHeight { get; init; } = 58;
    public double ModelWidth { get; init; } = 260;
    public double ModelHeight { get; init; } = 50;
    public double RowGap { get; init; } = 18;
    public double ColumnGap { get; init; } = 140;
    public double OuterPadding { get; init; } = 40;
}

public sealed record RuntimeGraphNodeLayout(
    string NodeId,
    RuntimeGraphNodeKind Kind,
    Rect Bounds,
    string? ParentComboId = null);

public sealed record RuntimeGraphEdgeLayout(
    string EdgeId,
    Point Source,
    Point Target);

public sealed record RuntimeGraphLayoutSnapshot(
    IReadOnlyDictionary<string, RuntimeGraphNodeLayout> Nodes,
    IReadOnlyList<RuntimeGraphEdgeLayout> Edges,
    Rect ContentBounds);

public static class RuntimeGraphLayout
{
    public static RuntimeGraphLayoutSnapshot Create(RuntimeGraphSnapshot snapshot, RuntimeGraphLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new RuntimeGraphLayoutOptions();
        Validate(options);

        var endpoints = snapshot.Endpoints.OrderBy(node => node.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var combos = snapshot.Combos.OrderBy(node => node.SortOrder).ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var models = snapshot.Models.OrderBy(node => node.ProviderDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var contentHeight = Math.Max(
            Math.Max(ColumnHeight(endpoints.Length, options.NodeHeight, options.RowGap), ColumnHeight(combos.Length, options.NodeHeight, options.RowGap)),
            ColumnHeight(models.Length, options.ModelHeight, options.RowGap));
        contentHeight = Math.Max(contentHeight, options.NodeHeight);

        var endpointX = options.OuterPadding;
        var comboX = endpointX + options.NodeWidth + options.ColumnGap;
        var modelX = comboX + options.NodeWidth + options.ColumnGap;
        var nodes = new Dictionary<string, RuntimeGraphNodeLayout>(StringComparer.OrdinalIgnoreCase);
        PlaceNodes(endpoints, endpointX, options.NodeWidth, options.NodeHeight, contentHeight, options, nodes);
        PlaceNodes(combos, comboX, options.NodeWidth, options.NodeHeight, contentHeight, options, nodes);
        PlaceNodes(models, modelX, options.ModelWidth, options.ModelHeight, contentHeight, options, nodes);

        var edgeLayouts = snapshot.Edges
            .OrderBy(edge => edge.Id, StringComparer.OrdinalIgnoreCase)
            .Select(edge => CreateEdgeLayout(edge, nodes))
            .Where(edge => edge is not null)
            .Cast<RuntimeGraphEdgeLayout>()
            .ToArray();
        return new RuntimeGraphLayoutSnapshot(
            nodes,
            edgeLayouts,
            new Rect(0, 0, modelX + options.ModelWidth + options.OuterPadding, contentHeight + options.OuterPadding * 2));
    }

    private static void PlaceNodes(
        IReadOnlyList<RuntimeGraphNode> items,
        double x,
        double width,
        double height,
        double contentHeight,
        RuntimeGraphLayoutOptions options,
        IDictionary<string, RuntimeGraphNodeLayout> layouts)
    {
        var columnHeight = ColumnHeight(items.Count, height, options.RowGap);
        var y = options.OuterPadding + (contentHeight - columnHeight) / 2;
        foreach (var item in items)
        {
            layouts.Add(item.Id, new RuntimeGraphNodeLayout(item.Id, item.Kind, new Rect(x, y, width, height)));
            y += height + options.RowGap;
        }
    }

    private static RuntimeGraphEdgeLayout? CreateEdgeLayout(RuntimeGraphEdge edge, IReadOnlyDictionary<string, RuntimeGraphNodeLayout> nodes)
    {
        if (!nodes.TryGetValue(edge.SourceId, out var source) || !nodes.TryGetValue(edge.TargetId, out var target)) return null;
        return new RuntimeGraphEdgeLayout(edge.Id, new Point(source.Bounds.Right, source.Bounds.Center.Y), new Point(target.Bounds.Left, target.Bounds.Center.Y));
    }

    private static double ColumnHeight(int count, double itemHeight, double rowGap) =>
        count == 0 ? 0 : count * itemHeight + Math.Max(0, count - 1) * rowGap;

    private static void Validate(RuntimeGraphLayoutOptions options)
    {
        if (options.NodeWidth <= 0 || options.NodeHeight <= 0 || options.ModelWidth <= 0 || options.ModelHeight <= 0
            || options.RowGap < 0 || options.ColumnGap < 0 || options.OuterPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "节点图布局尺寸必须为正数，间距不能为负数。");
    }
}
