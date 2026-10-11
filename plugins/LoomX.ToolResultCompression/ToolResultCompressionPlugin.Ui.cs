using System.Globalization;
using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 插件的声明式 UI：card-body 给概览指标，detail-body 给可交互设置页。
/// 节点树由宿主渲染，插件不引用任何 UI 框架类型。
/// </summary>
public sealed partial class ToolResultCompressionPlugin : IPluginUiContributionProvider
{
    public const string OverviewContributionId = CompressionSettingsUi.OverviewContributionId;
    public const string SettingsContributionId = CompressionSettingsUi.SettingsContributionId;

    public event EventHandler? UiInvalidated
    {
        add => observability.Changed += value;
        remove => observability.Changed -= value;
    }

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stats = observability.Snapshot();
        var options = optionsStore?.Current ?? ToolResultCompressionOptions.Default;
        var culture = ResolveCulture(context.CultureName);

        return
        [
            new PluginUiContribution(
                PluginUiContribution.CurrentSchemaVersion,
                OverviewContributionId,
                PluginUiSlot.CardBody,
                BuildOverview(stats, culture)),
            new PluginUiContribution(
                PluginUiContribution.CurrentSchemaVersion,
                SettingsContributionId,
                PluginUiSlot.DetailBody,
                CompressionSettingsUi.BuildSettings(stats, options, context.CultureName)),
        ];
    }

    private static PluginUiNode BuildOverview(CompressionStats stats, CultureInfo culture)
    {
        var text = ToolResultCompressionUiText.Resolve(culture.Name);
        return new PluginUiGridNode(
            3,
            [
                new PluginUiGridItem(BuildMetricCard(
                    text.SavedTokens,
                    string.Format(culture, text.TokenUnit, stats.SavedTokens.ToString("N0", culture)),
                    string.Format(culture, text.SavedHint, stats.TokenSavingsRatio.ToString("P0", culture)),
                    PluginUiTone.Success,
                    "M 12,2 A 10,10 0 1 1 12,22 A 10,10 0 1 1 12,2 M 12,7 L 12,17 M 8,10 L 12,7 L 16,10")),
                new PluginUiGridItem(BuildMetricCard(
                    text.CompressedRequests,
                    $"{stats.CompressedRequests.ToString("N0", culture)}/{stats.TotalRequests.ToString("N0", culture)}",
                    string.Format(culture, text.CompressedResults, stats.CompressedToolResults.ToString("N0", culture)),
                    PluginUiTone.Accent,
                    "M 12,3 L 12,17 M 7,12 L 12,17 L 17,12 M 4,21 L 20,21")),
                new PluginUiGridItem(BuildMetricCard(
                    text.Errors,
                    stats.Errors.ToString("N0", culture),
                    text.ErrorsHint,
                    PluginUiTone.Warning,
                    "M 16,4 A 12,12 0 1 1 16,28 A 12,12 0 1 1 16,4 M 16,10 L 16,18 M 16,23 L 16,23")),
            ],
            ColumnSpacing: 12);
    }

    private static PluginUiSurfaceNode BuildMetricCard(
        string title,
        string value,
        string caption,
        PluginUiTone tone,
        string iconData) =>
        new(
            new PluginUiStackNode(
                [
                    new PluginUiGridNode(
                        2,
                        [
                            new PluginUiGridItem(new PluginUiTextNode(title, PluginUiTextRole.Title)),
                            new PluginUiGridItem(
                                new PluginUiIconNode(iconData, tone, Size: 18, HorizontalAlignment: PluginUiAlignment.End),
                                HorizontalAlignment: PluginUiAlignment.End),
                        ]),
                    new PluginUiTextNode(value, PluginUiTextRole.Metric, tone),
                    new PluginUiDividerNode(),
                    new PluginUiTextNode(caption, PluginUiTextRole.Caption, Wrap: true),
                ],
                Spacing: 8),
            Tone: PluginUiTone.Default,
            Padding: 14,
            CornerRadius: 10);

    /// <summary>文化名不可信：解析失败时退化为不变文化，避免 UI 快照整体失败。</summary>
    internal static CultureInfo ResolveCulture(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
