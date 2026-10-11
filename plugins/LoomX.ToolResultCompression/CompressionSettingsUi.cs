using System.Globalization;
using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 压缩插件的声明式设置页。只产出平台无关的节点树与命令标识：
/// 所有配置改动都通过命令回传由插件自己落库，UI 不直接改状态。
/// </summary>
public static class CompressionSettingsUi
{
    public const string OverviewContributionId = "observability";
    public const string SettingsContributionId = "settings";

    public const string ToggleEnabled = "toggle_enabled";
    public const string SetLevel = "set_level";
    public const string ApplyPolicy = "apply_policy";
    public const string ToggleJsonFold = "toggle_json_fold";
    public const string ToggleDedupe = "toggle_dedupe";
    public const string ToggleCommandFilters = "toggle_command_filters";
    public const string ResetStats = "reset_stats";
    public const string RestoreDefaults = "restore_defaults";

    public const string EnabledField = "enabled";
    public const string LevelField = "level";
    public const string KeepRecentField = "keep_recent";
    public const string MinSavingField = "min_saving_pct";
    public const string JsonFoldField = "json_fold";
    public const string DedupeField = "dedupe";
    public const string CommandFiltersField = "command_filters";

    private const int MaxVisibleRecent = 6;

    /// <summary>插件详情正文：节省对比 + 策略开关 + 最近记录。</summary>
    public static PluginUiNode BuildSettings(
        CompressionStats stats,
        ToolResultCompressionOptions options,
        string cultureName)
    {
        var text = CompressionSettingsText.Resolve(cultureName);
        return new PluginUiStackNode(
            [
                BuildSavingsSection(stats, text),
                BuildStrategySection(options, text),
                BuildRecentSection(stats, text),
            ],
            Spacing: 14);
    }

    /// <summary>节省对比：字符数是精确值，token 是估算值，两列并排给出压缩前后。</summary>
    private static PluginUiNode BuildSavingsSection(CompressionStats stats, CompressionSettingsText text)
    {
        var culture = text.Culture;
        var rows = new List<PluginUiGridItem>
        {
            new(new PluginUiTextNode(string.Empty)),
            new(new PluginUiTextNode(text.ColumnOriginal, PluginUiTextRole.Caption, HorizontalAlignment: PluginUiAlignment.End)),
            new(new PluginUiTextNode(text.ColumnCompressed, PluginUiTextRole.Caption, HorizontalAlignment: PluginUiAlignment.End)),

            new(new PluginUiTextNode(text.RowChars, PluginUiTextRole.Body)),
            new(new PluginUiTextNode(FormatChars(stats.OriginalChars, text), PluginUiTextRole.Metric, HorizontalAlignment: PluginUiAlignment.End)),
            new(new PluginUiTextNode(FormatChars(stats.CompressedChars, text), PluginUiTextRole.Metric, HorizontalAlignment: PluginUiAlignment.End)),

            new(new PluginUiTextNode(text.RowTokens, PluginUiTextRole.Body)),
            new(new PluginUiTextNode(FormatTokens(stats.EstimatedOriginalTokens, text), PluginUiTextRole.Metric, HorizontalAlignment: PluginUiAlignment.End)),
            new(new PluginUiTextNode(FormatTokens(stats.EstimatedCompressedTokens, text), PluginUiTextRole.Metric, HorizontalAlignment: PluginUiAlignment.End)),

            new(new PluginUiTextNode(text.RowSaved, PluginUiTextRole.Title)),
            new(
                new PluginUiTextNode(
                    string.Format(
                        culture,
                        text.RowSavedValue,
                        FormatChars(stats.SavedChars, text),
                        FormatTokens(stats.SavedTokens, text),
                        stats.TokenSavingsRatio.ToString("P0", culture)),
                    PluginUiTextRole.Metric,
                    PluginUiTone.Success,
                    HorizontalAlignment: PluginUiAlignment.End),
                ColumnSpan: 2),
        };

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(
                [
                    new PluginUiTextNode(text.SavingsTitle, PluginUiTextRole.Title),
                    new PluginUiGridNode(
                        3,
                        rows,
                        ColumnSpacing: 10,
                        RowSpacing: 6,
                        // 首列按标签自适应，两列数值等宽。
                        ColumnWeights: [0, 1, 1]),
                    new PluginUiDividerNode(),
                    new PluginUiTextNode(text.SavingsHint, PluginUiTextRole.Caption, Wrap: true),
                    new PluginUiTextNode(
                        string.Format(culture, text.SummaryFormat, stats.TotalRequests, stats.CompressedToolResults),
                        PluginUiTextRole.Caption,
                        Wrap: true),
                ],
                Spacing: 10),
            Padding: 14,
            CornerRadius: 10);
    }

    /// <summary>
    /// 策略区：强度预设与细项并存。预设只改数值，不引入隐藏状态，
    /// 手动改过数值后档位显示为空，避免 UI 谎报当前强度。
    /// </summary>
    private static PluginUiNode BuildStrategySection(
        ToolResultCompressionOptions options,
        CompressionSettingsText text)
    {
        var culture = text.Culture;
        var level = options.MatchLevel();
        var selectedLevel = level switch
        {
            CompressionLevel.Conservative => text.LevelConservative,
            CompressionLevel.Standard => text.LevelStandard,
            CompressionLevel.Aggressive => text.LevelAggressive,
            _ => string.Empty,
        };

        var children = new List<PluginUiNode>
        {
            new PluginUiTextNode(text.StrategyTitle, PluginUiTextRole.Title),
            new PluginUiTextNode(text.StrategyHint, PluginUiTextRole.Caption, Wrap: true),
            new PluginUiToggleFieldNode(
                EnabledField,
                text.EnableLabel,
                options.Enabled,
                text.EnableCaption,
                ToggleEnabled),
            new PluginUiChoiceFieldNode(
                LevelField,
                text.LevelLabel,
                [text.LevelConservative, text.LevelStandard, text.LevelAggressive],
                selectedLevel,
                SetLevel),
        };

        if (selectedLevel.Length == 0)
            children.Add(new PluginUiTextNode(text.LevelCustom, PluginUiTextRole.Caption, Wrap: true));

        children.Add(new PluginUiGridNode(
            3,
            [
                new PluginUiGridItem(new PluginUiTextFieldNode(
                    KeepRecentField,
                    text.KeepRecentLabel,
                    options.KeepRecentToolResults.ToString(culture),
                    text.KeepRecentWatermark)),
                new PluginUiGridItem(new PluginUiTextFieldNode(
                    MinSavingField,
                    text.MinSavingLabel,
                    (options.MinSavingsRatio * 100).ToString("0.##", culture),
                    text.MinSavingWatermark)),
                // 只读取这两个字段：命令参数必须按需收集，整页字段并入会被宿主拒绝。
                new PluginUiGridItem(new PluginUiButtonNode(
                    text.ButtonApply,
                    ApplyPolicy,
                    Tone: PluginUiTone.Accent,
                    IsPrimary: true,
                    ReadsFields: [KeepRecentField, MinSavingField])),
            ],
            ColumnSpacing: 10));

        children.Add(new PluginUiToggleFieldNode(
            JsonFoldField, text.JsonFoldLabel, options.EnableJsonFolding, CommandId: ToggleJsonFold));
        children.Add(new PluginUiToggleFieldNode(
            DedupeField, text.DedupeLabel, options.EnableDedupe, CommandId: ToggleDedupe));
        children.Add(new PluginUiToggleFieldNode(
            CommandFiltersField, text.CommandFiltersLabel, options.EnableCommandFilters, CommandId: ToggleCommandFilters));
        children.Add(new PluginUiButtonNode(text.ButtonRestoreDefaults, RestoreDefaults));

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(children, Spacing: 10),
            Padding: 14,
            CornerRadius: 10);
    }

    private static PluginUiNode BuildRecentSection(CompressionStats stats, CompressionSettingsText text)
    {
        var children = new List<PluginUiNode>
        {
            new PluginUiTextNode(text.RecentTitle, PluginUiTextRole.Title),
        };

        var entries = stats.RecentEntries;
        if (entries.Count == 0)
        {
            children.Add(new PluginUiTextNode(text.RecentEmpty, PluginUiTextRole.Caption, Wrap: true));
        }
        else
        {
            foreach (var entry in entries.Take(MaxVisibleRecent))
                children.Add(new PluginUiTextNode(FormatEntry(entry, text), PluginUiTextRole.Caption, Wrap: true));
        }

        children.Add(new PluginUiDividerNode());
        children.Add(new PluginUiTextNode(text.ButtonResetStatsCaption, PluginUiTextRole.Caption, Wrap: true));
        children.Add(new PluginUiButtonNode(text.ButtonResetStats, ResetStats, Tone: PluginUiTone.Danger));

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(children, Spacing: 8),
            Padding: 14,
            CornerRadius: 10);
    }

    private static string FormatEntry(CompressionStatsEntry entry, CompressionSettingsText text) =>
        string.Format(
            text.Culture,
            text.RecentEntryFormat,
            entry.At.ToLocalTime().ToString("HH:mm", text.Culture),
            entry.CompressedToolResults,
            FormatChars(entry.SavedChars, text),
            entry.SavingsRatio.ToString("P0", text.Culture));

    internal static string FormatChars(long chars, CompressionSettingsText text)
    {
        var culture = text.Culture;
        if (chars < 1024) return string.Format(culture, text.CharsUnit, chars);
        if (chars < 1024 * 1024) return string.Format(culture, text.KiloCharsUnit, chars / 1024.0);
        return string.Format(culture, text.MegaCharsUnit, chars / 1024.0 / 1024.0);
    }

    internal static string FormatTokens(long tokens, CompressionSettingsText text) =>
        string.Format(text.Culture, text.TokenUnit, tokens.ToString("N0", text.Culture));

    /// <summary>
    /// 把下拉里选中的值反查为强度标识。宿主回传的是它渲染时的文案，
    /// 因此先按稳定标识再按本地化文案匹配，两者都不命中才回退标准档。
    /// </summary>
    internal static string ResolveLevel(string raw, CompressionSettingsText text)
    {
        if (string.Equals(raw, CompressionLevel.Conservative, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, text.LevelConservative, StringComparison.Ordinal))
        {
            return CompressionLevel.Conservative;
        }

        if (string.Equals(raw, CompressionLevel.Aggressive, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, text.LevelAggressive, StringComparison.Ordinal))
        {
            return CompressionLevel.Aggressive;
        }

        return CompressionLevel.Standard;
    }
}
