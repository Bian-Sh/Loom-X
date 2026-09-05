using Avalonia;

namespace LoomX.NodeGraph;

public sealed record RuntimeGraphLayoutOptions
{
    public double NodeWidth { get; init; } = 180;
    public double NodeHeight { get; init; } = 58;
    public double ModelWidth { get; init; } = 232;
    public double ModelHeight { get; init; } = 44;
    public double ProviderWidth { get; init; } = 280;
    public double ProviderHeaderHeight { get; init; } = 38;
    public double ProviderPadding { get; init; } = 16;
    public double RowGap { get; init; } = 18;
    public double ColumnGap { get; init; } = 140;
    public double ProviderGap { get; init; } = 36;
    public double OuterPadding { get; init; } = 40;
    public double EmptyProviderHeight { get; init; } = 96;
}

public sealed record RuntimeGraphNodeLayout(
    string NodeId,
    RuntimeGraphNodeKind Kind,
    Rect Bounds,
    string? ParentProviderId = null);

public sealed record RuntimeGraphProviderGroupLayout(
    string ProviderId,
    Rect Bounds,
    IReadOnlyList<string> ModelIds);

public sealed record RuntimeGraphEdgeLayout(
    string EdgeId,
    Point Source,
    Point Target);

public sealed record RuntimeGraphLayoutSnapshot(
    IReadOnlyDictionary<string, RuntimeGraphNodeLayout> Nodes,
    IReadOnlyDictionary<string, RuntimeGraphProviderGroupLayout> ProviderGroups,
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
        var combos = snapshot.Combos
            .OrderBy(node => node.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.SortOrder)
            .ThenBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var providers = snapshot.Providers.OrderBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var contentHeight = Math.Max(
            Math.Max(ColumnHeight(endpoints.Length, options.NodeHeight, options.RowGap), ColumnHeight(combos.Length, options.NodeHeight, options.RowGap)),
            providers.Sum(provider => ProviderHeight(provider.Models.Count, options)) + Math.Max(0, providers.Length - 1) * options.ProviderGap);
        contentHeight = Math.Max(contentHeight, options.NodeHeight);

        var endpointX = options.OuterPadding;
        var comboX = endpointX + options.NodeWidth + options.ColumnGap;
        var providerX = comboX + options.NodeWidth + options.ColumnGap;
        var nodes = new Dictionary<string, RuntimeGraphNodeLayout>(StringComparer.OrdinalIgnoreCase);
        PlaceNodes(endpoints, endpointX, options.NodeWidth, options.NodeHeight, contentHeight, options, nodes);
        PlaceNodes(combos, comboX, options.NodeWidth, options.NodeHeight, contentHeight, options, nodes);

        var providerLayouts = new Dictionary<string, RuntimeGraphProviderGroupLayout>(StringComparer.OrdinalIgnoreCase);
        var providerY = options.OuterPadding + (contentHeight - providers.Sum(provider => ProviderHeight(provider.Models.Count, options)) - Math.Max(0, providers.Length - 1) * options.ProviderGap) / 2;
        foreach (var provider in providers)
        {
            var height = ProviderHeight(provider.Models.Count, options);
            var bounds = new Rect(providerX, providerY, options.ProviderWidth, height);
            var modelIds = provider.Models
                .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(model => model.Id)
                .ToArray();
            providerLayouts.Add(provider.Id, new RuntimeGraphProviderGroupLayout(provider.Id, bounds, modelIds));

            for (var index = 0; index < modelIds.Length; index++)
            {
                var modelY = providerY + options.ProviderHeaderHeight + options.ProviderPadding + index * (options.ModelHeight + options.RowGap);
                nodes.Add(modelIds[index], new RuntimeGraphNodeLayout(
                    modelIds[index],
                    RuntimeGraphNodeKind.Model,
                    new Rect(providerX + options.ProviderPadding, modelY, options.ModelWidth, options.ModelHeight),
                    provider.Id));
            }

            providerY += height + options.ProviderGap;
        }

        var edgeLayouts = snapshot.Edges
            .OrderBy(edge => edge.Id, StringComparer.OrdinalIgnoreCase)
            .Select(edge => CreateEdgeLayout(edge, nodes, providerLayouts))
            .Where(edge => edge is not null)
            .Cast<RuntimeGraphEdgeLayout>()
            .ToArray();
        var contentWidth = providerX + options.ProviderWidth + options.OuterPadding;
        return new RuntimeGraphLayoutSnapshot(
            nodes,
            providerLayouts,
            edgeLayouts,
            new Rect(0, 0, contentWidth, contentHeight + options.OuterPadding * 2));
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

    private static RuntimeGraphEdgeLayout? CreateEdgeLayout(
        RuntimeGraphEdge edge,
        IReadOnlyDictionary<string, RuntimeGraphNodeLayout> nodes,
        IReadOnlyDictionary<string, RuntimeGraphProviderGroupLayout> providers)
    {
        if (!TryGetBounds(edge.SourceId, nodes, providers, out var sourceBounds)
            || !TryGetBounds(edge.TargetId, nodes, providers, out var targetBounds)) return null;
        return new RuntimeGraphEdgeLayout(
            edge.Id,
            new Point(sourceBounds.Right, sourceBounds.Center.Y),
            new Point(targetBounds.Left, targetBounds.Center.Y));
    }

    private static bool TryGetBounds(
        string id,
        IReadOnlyDictionary<string, RuntimeGraphNodeLayout> nodes,
        IReadOnlyDictionary<string, RuntimeGraphProviderGroupLayout> providers,
        out Rect bounds)
    {
        if (nodes.TryGetValue(id, out var node))
        {
            bounds = node.Bounds;
            return true;
        }

        if (providers.TryGetValue(id, out var provider))
        {
            bounds = provider.Bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private static double ColumnHeight(int count, double itemHeight, double rowGap) =>
        count == 0 ? 0 : count * itemHeight + Math.Max(0, count - 1) * rowGap;

    private static double ProviderHeight(int modelCount, RuntimeGraphLayoutOptions options)
    {
        var modelsHeight = ColumnHeight(modelCount, options.ModelHeight, options.RowGap);
        return Math.Max(
            options.EmptyProviderHeight,
            options.ProviderHeaderHeight + options.ProviderPadding * 2 + modelsHeight);
    }

    private static void Validate(RuntimeGraphLayoutOptions options)
    {
        if (options.NodeWidth <= 0 || options.NodeHeight <= 0 || options.ModelWidth <= 0 || options.ModelHeight <= 0
            || options.ProviderWidth <= 0 || options.ProviderHeaderHeight <= 0 || options.ProviderPadding < 0
            || options.RowGap < 0 || options.ColumnGap < 0 || options.ProviderGap < 0
            || options.OuterPadding < 0 || options.EmptyProviderHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "节点图布局尺寸必须为正数，间距不能为负数。");
    }
}
