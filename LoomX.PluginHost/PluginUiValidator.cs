namespace LoomX.Plugins.Host;

internal readonly record struct PluginUiValidationResult(bool IsValid, string ReasonCode)
{
    public static PluginUiValidationResult Valid => new(true, string.Empty);
    public static PluginUiValidationResult Invalid(string reasonCode) => new(false, reasonCode);
}

/// <summary>验证插件声明式 UI 的结构边界；失败原因只返回安全代码，不回显节点内容。</summary>
internal static class PluginUiValidator
{
    private const int MaxDepth = 12;
    private const int MaxNodes = 128;
    private const int MaxTextLength = 4096;
    private const int MaxGeometryLength = 2048;
    private const int MaxColumns = 12;
    private const double MaxSpacing = 64;
    private const double MaxSize = 512;

    public static PluginUiValidationResult Validate(PluginUiContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.SchemaVersion != PluginUiContribution.CurrentSchemaVersion)
            return PluginUiValidationResult.Invalid("UNSUPPORTED_SCHEMA");
        if (string.IsNullOrWhiteSpace(contribution.Id) || contribution.Id.Length > 128)
            return PluginUiValidationResult.Invalid("INVALID_ID");
        if (contribution.Root is null)
            return PluginUiValidationResult.Invalid("MISSING_ROOT");

        var nodeCount = 0;
        return ValidateNode(contribution.Root, depth: 1, ref nodeCount);
    }

    private static PluginUiValidationResult ValidateNode(PluginUiNode node, int depth, ref int nodeCount)
    {
        if (depth > MaxDepth) return PluginUiValidationResult.Invalid("TREE_TOO_DEEP");
        nodeCount++;
        if (nodeCount > MaxNodes) return PluginUiValidationResult.Invalid("TOO_MANY_NODES");

        switch (node)
        {
            case PluginUiStackNode stack:
                if (!IsFiniteInRange(stack.Spacing, 0, MaxSpacing))
                    return PluginUiValidationResult.Invalid("INVALID_SPACING");
                if (stack.Children is null)
                    return PluginUiValidationResult.Invalid("MISSING_CHILDREN");
                foreach (var child in stack.Children)
                {
                    if (child is null) return PluginUiValidationResult.Invalid("NULL_CHILD");
                    var result = ValidateNode(child, depth + 1, ref nodeCount);
                    if (!result.IsValid) return result;
                }
                return PluginUiValidationResult.Valid;

            case PluginUiGridNode grid:
                if (grid.Columns is < 1 or > MaxColumns)
                    return PluginUiValidationResult.Invalid("INVALID_COLUMNS");
                if (!IsFiniteInRange(grid.ColumnSpacing, 0, MaxSpacing)
                    || !IsFiniteInRange(grid.RowSpacing, 0, MaxSpacing))
                    return PluginUiValidationResult.Invalid("INVALID_SPACING");
                if (grid.Children is null)
                    return PluginUiValidationResult.Invalid("MISSING_CHILDREN");
                foreach (var item in grid.Children)
                {
                    if (item is null || item.Content is null)
                        return PluginUiValidationResult.Invalid("NULL_CHILD");
                    if (item.ColumnSpan < 1 || item.ColumnSpan > grid.Columns)
                        return PluginUiValidationResult.Invalid("INVALID_COLUMN_SPAN");
                    var result = ValidateNode(item.Content, depth + 1, ref nodeCount);
                    if (!result.IsValid) return result;
                }
                return PluginUiValidationResult.Valid;

            case PluginUiSurfaceNode surface:
                if (!IsFiniteInRange(surface.Padding, 0, MaxSpacing)
                    || !IsFiniteInRange(surface.CornerRadius, 0, MaxSpacing))
                    return PluginUiValidationResult.Invalid("INVALID_SURFACE_SIZE");
                return surface.Content is null
                    ? PluginUiValidationResult.Invalid("NULL_CHILD")
                    : ValidateNode(surface.Content, depth + 1, ref nodeCount);

            case PluginUiTextNode text:
                return text.Text is null || text.Text.Length > MaxTextLength
                    ? PluginUiValidationResult.Invalid("TEXT_TOO_LONG")
                    : PluginUiValidationResult.Valid;

            case PluginUiIconNode icon:
                if (string.IsNullOrWhiteSpace(icon.Data) || icon.Data.Length > MaxGeometryLength)
                    return PluginUiValidationResult.Invalid("INVALID_ICON_DATA");
                return IsFiniteInRange(icon.Size, 1, MaxSize)
                    ? PluginUiValidationResult.Valid
                    : PluginUiValidationResult.Invalid("INVALID_ICON_SIZE");

            case PluginUiDividerNode:
                return PluginUiValidationResult.Valid;

            default:
                return PluginUiValidationResult.Invalid("UNKNOWN_NODE_TYPE");
        }
    }

    private static bool IsFiniteInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}
