namespace LoomX.Plugins.Host;

internal readonly record struct PluginUiValidationResult(bool IsValid, string ReasonCode)
{
    public static PluginUiValidationResult Valid => new(true, string.Empty);
    public static PluginUiValidationResult Invalid(string reasonCode) => new(false, reasonCode);
}

/// <summary>验证插件声明式 UI 的结构边界；失败原因只返回安全代码，不回显节点内容。</summary>
internal static class PluginUiValidator
{
    private const int MaxDepth = 14;
    private const int MaxNodes = 512;
    private const int MaxTextLength = 4096;
    private const int MaxGeometryLength = 2048;
    private const int MaxColumns = 12;
    private const double MaxSpacing = 64;
    private const double MaxSize = 512;
    private const int MaxIdentifierLength = 128;
    private const int MaxLabelLength = 256;
    private const int MaxArguments = 32;
    private const int MaxOptions = 64;
    private const int MaxOptionLength = 256;
    private const int MaxColumnWeights = 12;
    private const double MaxColumnWeight = 1000d;

    public static PluginUiValidationResult Validate(PluginUiContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.SchemaVersion is < PluginUiContribution.MinimumSchemaVersion
            or > PluginUiContribution.CurrentSchemaVersion)
            return PluginUiValidationResult.Invalid("UNSUPPORTED_SCHEMA");
        if (string.IsNullOrWhiteSpace(contribution.Id) || contribution.Id.Length > 128)
            return PluginUiValidationResult.Invalid("INVALID_ID");
        if (contribution.Root is null)
            return PluginUiValidationResult.Invalid("MISSING_ROOT");

        var interactive = contribution.SchemaVersion >= PluginUiContribution.InteractiveSchemaVersion;
        var nodeCount = 0;
        return ValidateNode(contribution.Root, depth: 1, ref nodeCount, interactive);
    }

    private static PluginUiValidationResult ValidateNode(
        PluginUiNode node,
        int depth,
        ref int nodeCount,
        bool interactive)
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
                    var result = ValidateNode(child, depth + 1, ref nodeCount, interactive);
                    if (!result.IsValid) return result;
                }
                return PluginUiValidationResult.Valid;

            case PluginUiGridNode grid:
                if (grid.Columns is < 1 or > MaxColumns)
                    return PluginUiValidationResult.Invalid("INVALID_COLUMNS");
                if (!IsFiniteInRange(grid.ColumnSpacing, 0, MaxSpacing)
                    || !IsFiniteInRange(grid.RowSpacing, 0, MaxSpacing))
                    return PluginUiValidationResult.Invalid("INVALID_SPACING");
                if (!ValidateColumnWeights(grid))
                    return PluginUiValidationResult.Invalid("INVALID_COLUMN_WEIGHTS");
                if (grid.Children is null)
                    return PluginUiValidationResult.Invalid("MISSING_CHILDREN");
                foreach (var item in grid.Children)
                {
                    if (item is null || item.Content is null)
                        return PluginUiValidationResult.Invalid("NULL_CHILD");
                    if (item.ColumnSpan < 1 || item.ColumnSpan > grid.Columns)
                        return PluginUiValidationResult.Invalid("INVALID_COLUMN_SPAN");
                    var result = ValidateNode(item.Content, depth + 1, ref nodeCount, interactive);
                    if (!result.IsValid) return result;
                }
                return PluginUiValidationResult.Valid;

            case PluginUiSurfaceNode surface:
                if (!IsFiniteInRange(surface.Padding, 0, MaxSpacing)
                    || !IsFiniteInRange(surface.CornerRadius, 0, MaxSpacing))
                    return PluginUiValidationResult.Invalid("INVALID_SURFACE_SIZE");
                return surface.Content is null
                    ? PluginUiValidationResult.Invalid("NULL_CHILD")
                    : ValidateNode(surface.Content, depth + 1, ref nodeCount, interactive);

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

            case PluginUiButtonNode button:
                if (!interactive) return RejectInteractive();
                if (!IsValidIdentifier(button.CommandId))
                    return PluginUiValidationResult.Invalid("INVALID_COMMAND_ID");
                if (!IsValidLabel(button.Label))
                    return PluginUiValidationResult.Invalid("INVALID_LABEL");
                if (button.ToolTip is { } toolTip && !IsValidLabel(toolTip))
                    return PluginUiValidationResult.Invalid("INVALID_LABEL");
                if (button.IconData is { } iconData
                    && (string.IsNullOrWhiteSpace(iconData) || iconData.Length > MaxGeometryLength))
                    return PluginUiValidationResult.Invalid("INVALID_ICON_DATA");
                var readsFields = ValidateReadsFields(button.ReadsFields);
                return readsFields ?? ValidateArguments(button.Arguments);

            case PluginUiTextFieldNode field:
                if (!interactive) return RejectInteractive();
                if (!IsValidIdentifier(field.FieldId))
                    return PluginUiValidationResult.Invalid("INVALID_FIELD_ID");
                if (!IsValidLabel(field.Label))
                    return PluginUiValidationResult.Invalid("INVALID_LABEL");
                if (field.Value is null || field.Value.Length > MaxTextLength)
                    return PluginUiValidationResult.Invalid("TEXT_TOO_LONG");
                return field.Watermark is { Length: > MaxLabelLength }
                    ? PluginUiValidationResult.Invalid("INVALID_LABEL")
                    : PluginUiValidationResult.Valid;

            case PluginUiToggleFieldNode toggle:
                if (!interactive) return RejectInteractive();
                if (!IsValidIdentifier(toggle.FieldId))
                    return PluginUiValidationResult.Invalid("INVALID_FIELD_ID");
                if (!IsValidLabel(toggle.Label))
                    return PluginUiValidationResult.Invalid("INVALID_LABEL");
                if (toggle.Caption is { Length: > MaxTextLength })
                    return PluginUiValidationResult.Invalid("TEXT_TOO_LONG");
                return ValidateInstantCommand(toggle.CommandId, toggle.Arguments, interactive);

            case PluginUiChoiceFieldNode choice:
                if (!interactive) return RejectInteractive();
                if (!IsValidIdentifier(choice.FieldId))
                    return PluginUiValidationResult.Invalid("INVALID_FIELD_ID");
                if (!IsValidLabel(choice.Label))
                    return PluginUiValidationResult.Invalid("INVALID_LABEL");
                if (choice.Options is null || choice.Options.Count > MaxOptions)
                    return PluginUiValidationResult.Invalid("INVALID_CHOICE_OPTIONS");
                if (choice.SelectedValue is { Length: > MaxTextLength })
                    return PluginUiValidationResult.Invalid("TEXT_TOO_LONG");
                foreach (var option in choice.Options)
                {
                    if (string.IsNullOrWhiteSpace(option) || option.Length > MaxOptionLength)
                        return PluginUiValidationResult.Invalid("INVALID_CHOICE_OPTIONS");
                }
                return ValidateInstantCommand(choice.CommandId, choice.Arguments, interactive);

            default:
                return PluginUiValidationResult.Invalid("UNKNOWN_NODE_TYPE");
        }
    }

    private static bool IsFiniteInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    /// <summary>列权重缺省即等宽；给出时必须逐项为有限数且不超过列数上限（0/负值表示按内容自适应）。</summary>
    private static bool ValidateColumnWeights(PluginUiGridNode grid)
    {
        if (grid.ColumnWeights is not { } weights) return true;
        if (weights.Count > MaxColumnWeights || weights.Count > grid.Columns) return false;
        foreach (var weight in weights)
            if (!double.IsFinite(weight) || Math.Abs(weight) > MaxColumnWeight)
                return false;
        return true;
    }

    /// <summary>按钮显式声明要读取的表单字段：数量有上限，且每项都必须是合法标识符。</summary>
    private static PluginUiValidationResult? ValidateReadsFields(IReadOnlyList<string>? readsFields)
    {
        if (readsFields is not { } fields) return null;
        if (fields.Count > MaxArguments)
            return PluginUiValidationResult.Invalid("TOO_MANY_READ_FIELDS");
        foreach (var field in fields)
            if (!IsValidIdentifier(field))
                return PluginUiValidationResult.Invalid("INVALID_READ_FIELD");
        return null;
    }

    private static PluginUiValidationResult RejectInteractive() =>
        PluginUiValidationResult.Invalid("INTERACTIVE_NODE_REQUIRES_V2");

    private static bool IsValidIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxIdentifierLength;

    private static bool IsValidLabel(string? value) =>
        value is not null && value.Length <= MaxLabelLength;

    /// <summary>字段节点上的即时命令（值变化即回传）校验；未声明命令时直接通过。</summary>
    private static PluginUiValidationResult ValidateInstantCommand(
        string? commandId,
        IReadOnlyDictionary<string, string>? arguments,
        bool interactive)
    {
        if (commandId is null) return PluginUiValidationResult.Valid;
        if (!interactive) return RejectInteractive();
        return !IsValidIdentifier(commandId)
            ? PluginUiValidationResult.Invalid("INVALID_COMMAND_ID")
            : ValidateArguments(arguments);
    }

    private static PluginUiValidationResult ValidateArguments(
        IReadOnlyDictionary<string, string>? arguments)
    {
        if (arguments is null) return PluginUiValidationResult.Valid;
        if (arguments.Count > MaxArguments)
            return PluginUiValidationResult.Invalid("TOO_MANY_ARGUMENTS");
        foreach (var (key, value) in arguments)
        {
            if (!IsValidIdentifier(key))
                return PluginUiValidationResult.Invalid("INVALID_ARGUMENT_KEY");
            if (value is null || value.Length > MaxTextLength)
                return PluginUiValidationResult.Invalid("TEXT_TOO_LONG");
        }
        return PluginUiValidationResult.Valid;
    }
}
