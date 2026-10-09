using System.Globalization;
using LoomX.Plugins;

namespace LoomX.Maskit;

/// <summary>
/// maskit 插件的声明式设置页。这里只产出平台无关的节点树与命令标识，
/// 不引用任何 Avalonia 控件，也不直接改状态：所有增删改查都通过命令回传由插件执行。
/// </summary>
public static class MaskitSettingsUi
{
    public const string SettingsContributionId = "settings";
    public const string OverviewContributionId = "overview";

    public const string AddRule = "add_rule";
    public const string BeginEdit = "begin_edit";
    public const string CancelEdit = "cancel_edit";
    public const string SaveEdit = "save_edit";
    public const string DeleteRule = "delete_rule";
    public const string ToggleRule = "toggle_rule";
    public const string ToggleBuiltin = "toggle_builtin";
    public const string AddPrefix = "add_prefix";
    public const string DeletePrefix = "delete_prefix";

    public const string NewCategoryField = "new_category";
    public const string NewValueField = "new_value";
    public const string NewKindField = "new_kind";
    public const string NewWholeField = "new_whole";
    public const string NewPrefixField = "new_prefix";

    /// <summary>列表一次渲染的规则上限，避免节点数突破宿主校验限制；超出时给出总数提示。</summary>
    private const int MaxVisibleRules = 24;

    public static string EnabledField(string ruleId) => $"enabled_{ruleId}";
    public static string EditCategoryField(string ruleId) => $"edit_category_{ruleId}";
    public static string EditValueField(string ruleId) => $"edit_value_{ruleId}";
    public static string EditWholeField(string ruleId) => $"edit_whole_{ruleId}";

    /// <summary>插件卡片正文：三个只读指标卡。</summary>
    public static PluginUiNode BuildCard(MaskitRuleStore store, MaskitUiText text, CultureInfo culture)
    {
        var builtinEnabled = store.BuiltinRules.Count(item => item.Value);
        return new PluginUiGridNode(
            3,
            [
                new PluginUiGridItem(BuildMetricCard(
                    text.CardCustomRules,
                    store.Rules.Count.ToString("N0", culture),
                    text.CardCustomRulesHint,
                    PluginUiTone.Accent)),
                new PluginUiGridItem(BuildMetricCard(
                    text.CardBuiltinRules,
                    $"{builtinEnabled.ToString("N0", culture)}/{MaskitBuiltinRules.Defaults.Count.ToString("N0", culture)}",
                    text.CardBuiltinRulesHint,
                    PluginUiTone.Success)),
                new PluginUiGridItem(BuildMetricCard(
                    text.CardSanitized,
                    store.SanitizedCount.ToString("N0", culture),
                    text.CardSanitizedHint,
                    PluginUiTone.Default)),
            ],
            ColumnSpacing: 12);
    }

    /// <summary>插件详情正文：可交互的规则设置页（新增 / 列表 / 编辑 / 删除 / 内置开关 / 前缀）。</summary>
    public static PluginUiNode BuildSettings(
        MaskitRuleStore store,
        MaskitUiText text,
        CultureInfo culture,
        string? editingRuleId)
    {
        var rules = store.Rules;
        return new PluginUiStackNode(
            [
                new PluginUiSurfaceNode(
                    new PluginUiStackNode(
                        [
                            new PluginUiTextNode(text.SettingsTitle, PluginUiTextRole.Title),
                            new PluginUiTextNode(text.SettingsHint, PluginUiTextRole.Caption, Wrap: true),
                        ],
                        Spacing: 6),
                    Padding: 14,
                    CornerRadius: 10),

                BuildAddSection(text),
                BuildRuleSection(store, text, culture, rules, editingRuleId),
                BuildBuiltinSection(store, text),
                BuildPrefixSection(store, text),
            ],
            Spacing: 14);
    }

    private static PluginUiNode BuildAddSection(MaskitUiText text) =>
        new PluginUiSurfaceNode(
            new PluginUiStackNode(
                [
                    new PluginUiTextNode(text.AddSectionTitle, PluginUiTextRole.Title),
                    new PluginUiTextNode(text.AddSectionHint, PluginUiTextRole.Caption, Wrap: true),
                    new PluginUiGridNode(
                        4,
                        [
                            new PluginUiGridItem(new PluginUiTextFieldNode(
                                NewCategoryField, text.FieldCategory, Watermark: "NAME")),
                            new PluginUiGridItem(new PluginUiTextFieldNode(
                                NewValueField, text.FieldValue, Watermark: "Alice")),
                            new PluginUiGridItem(new PluginUiChoiceFieldNode(
                                NewKindField, text.FieldKind, [text.KindWord, text.KindPattern], text.KindWord)),
                            new PluginUiGridItem(new PluginUiToggleFieldNode(
                                NewWholeField, text.FieldWholeWord, IsChecked: true)),
                        ],
                        ColumnSpacing: 10),
                    new PluginUiButtonNode(text.ButtonAdd, AddRule, Tone: PluginUiTone.Accent, IsPrimary: true),
                ],
                Spacing: 10),
            Padding: 14,
            CornerRadius: 10);

    private static PluginUiNode BuildRuleSection(
        MaskitRuleStore store,
        MaskitUiText text,
        CultureInfo culture,
        IReadOnlyList<MaskitRule> rules,
        string? editingRuleId)
    {
        var rows = new List<PluginUiNode>
        {
            new PluginUiTextNode(
                $"{text.RulesSectionTitle} · {rules.Count.ToString("N0", culture)}",
                PluginUiTextRole.Title),
        };

        if (rules.Count == 0)
        {
            rows.Add(new PluginUiTextNode(text.EmptyRulesHint, PluginUiTextRole.Caption, Wrap: true));
        }
        else
        {
            foreach (var rule in rules.Take(MaxVisibleRules))
                rows.Add(BuildRuleRow(store, text, rule, editingRuleId));

            if (rules.Count > MaxVisibleRules)
            {
                rows.Add(new PluginUiTextNode(
                    string.Format(culture, text.MoreRulesHint, MaxVisibleRules, rules.Count),
                    PluginUiTextRole.Caption,
                    Wrap: true));
            }
        }

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(rows, Spacing: 8),
            Padding: 14,
            CornerRadius: 10);
    }

    private static PluginUiNode BuildRuleRow(
        MaskitRuleStore store,
        MaskitUiText text,
        MaskitRule rule,
        string? editingRuleId)
    {
        if (string.Equals(rule.Id, editingRuleId, StringComparison.Ordinal))
        {
            return new PluginUiStackNode(
                [
                    new PluginUiGridNode(
                        3,
                        [
                            new PluginUiGridItem(new PluginUiTextFieldNode(
                                EditCategoryField(rule.Id), text.FieldCategory, rule.Category)),
                            new PluginUiGridItem(new PluginUiTextFieldNode(
                                EditValueField(rule.Id), text.FieldValue, rule.Value)),
                            new PluginUiGridItem(new PluginUiToggleFieldNode(
                                EditWholeField(rule.Id), text.FieldWholeWord, rule.WholeWord)),
                        ],
                        ColumnSpacing: 10),
                    new PluginUiStackNode(
                        [
                            new PluginUiButtonNode(
                                text.ButtonSave,
                                SaveEdit,
                                new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id },
                                PluginUiTone.Success,
                                IsPrimary: true),
                            new PluginUiButtonNode(text.ButtonCancel, CancelEdit),
                        ],
                        PluginUiOrientation.Horizontal,
                        Spacing: 8),
                ],
                Spacing: 8);
        }

        var tags = new List<string>();
        if (rule.Kind == MaskitRuleKind.Pattern) tags.Add(text.TagPattern);
        if (rule.WholeWord) tags.Add(text.TagWholeWord);
        var tag = tags.Count == 0 ? string.Empty : string.Join(" · ", tags);

        return new PluginUiGridNode(
            6,
            [
                new PluginUiGridItem(new PluginUiTextNode(rule.Category, PluginUiTextRole.Body, Wrap: true)),
                new PluginUiGridItem(new PluginUiTextNode(
                    MaskWord(rule.Value, rule.Category),
                    PluginUiTextRole.Body,
                    Wrap: true)),
                new PluginUiGridItem(new PluginUiTextNode(tag, PluginUiTextRole.Caption, Wrap: true)),
                new PluginUiGridItem(new PluginUiToggleFieldNode(
                    EnabledField(rule.Id),
                    text.EnableToggleLabel,
                    rule.Enabled,
                    CommandId: ToggleRule,
                    Arguments: new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id })),
                new PluginUiGridItem(new PluginUiButtonNode(
                    text.ButtonEdit,
                    BeginEdit,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id })),
                new PluginUiGridItem(new PluginUiButtonNode(
                    text.ButtonDelete,
                    DeleteRule,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id },
                    PluginUiTone.Danger)),
            ],
            ColumnSpacing: 8);
    }

    private static PluginUiNode BuildBuiltinSection(MaskitRuleStore store, MaskitUiText text)
    {
        var enabled = store.BuiltinRules;
        var toggles = new List<PluginUiGridItem>();
        foreach (var rule in MaskitBuiltinRules.Defaults)
        {
            var isEnabled = enabled.TryGetValue(rule.Key, out var value) ? value : rule.EnabledByDefault;
            toggles.Add(new PluginUiGridItem(new PluginUiToggleFieldNode(
                $"builtin_{rule.Key}",
                text.BuiltinName(rule.Key),
                isEnabled,
                CommandId: ToggleBuiltin,
                Arguments: new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = rule.Key })));
        }

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(
                [
                    new PluginUiTextNode(text.BuiltinSectionTitle, PluginUiTextRole.Title),
                    new PluginUiTextNode(text.BuiltinSectionHint, PluginUiTextRole.Caption, Wrap: true),
                    new PluginUiGridNode(3, toggles, ColumnSpacing: 10, RowSpacing: 10),
                ],
                Spacing: 8),
            Padding: 14,
            CornerRadius: 10);
    }

    private static PluginUiNode BuildPrefixSection(MaskitRuleStore store, MaskitUiText text)
    {
        var prefixes = store.SecretPrefixes;
        var rows = new List<PluginUiNode>
        {
            new PluginUiTextNode(text.PrefixSectionTitle, PluginUiTextRole.Title),
            new PluginUiGridNode(
                2,
                [
                    new PluginUiGridItem(new PluginUiTextFieldNode(
                        NewPrefixField, text.FieldPrefix, Watermark: "sk-")),
                    new PluginUiGridItem(new PluginUiButtonNode(
                        text.ButtonAddPrefix, AddPrefix, Tone: PluginUiTone.Accent, IsPrimary: true)),
                ],
                ColumnSpacing: 10),
        };

        if (prefixes.Count == 0)
        {
            rows.Add(new PluginUiTextNode(text.EmptyPrefixHint, PluginUiTextRole.Caption, Wrap: true));
        }
        else
        {
            foreach (var prefix in prefixes)
            {
                rows.Add(new PluginUiStackNode(
                    [
                        new PluginUiTextNode(prefix, PluginUiTextRole.Body),
                        new PluginUiButtonNode(
                            text.ButtonDelete,
                            DeletePrefix,
                            new Dictionary<string, string>(StringComparer.Ordinal) { ["prefix"] = prefix },
                            PluginUiTone.Danger),
                    ],
                    PluginUiOrientation.Horizontal,
                    Spacing: 10));
            }
        }

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(rows, Spacing: 8),
            Padding: 14,
            CornerRadius: 10);
    }

    private static PluginUiSurfaceNode BuildMetricCard(
        string title,
        string value,
        string caption,
        PluginUiTone tone) =>
        new(
            new PluginUiStackNode(
                [
                    new PluginUiTextNode(title, PluginUiTextRole.Title),
                    new PluginUiTextNode(value, PluginUiTextRole.Metric, tone),
                    new PluginUiDividerNode(),
                    new PluginUiTextNode(caption, PluginUiTextRole.Caption, Wrap: true),
                ],
                Spacing: 8),
            Tone: PluginUiTone.Default,
            Padding: 14,
            CornerRadius: 10);

    /// <summary>
    /// 列表展示时给规则值打码：凭据类类别恒打码（对齐 maskit「凭据类永远打码」口径），
    /// 编辑态走明文以便修改，规则值本身仍只存在插件数据目录。
    /// </summary>
    public static string MaskWord(string word, string category)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;
        if (MaskitBuiltinRules.CredentialKeys.Contains(category) || word.Length <= 2)
            return new string('*', Math.Min(8, Math.Max(2, word.Length)));
        if (word.Length <= 6) return word[0] + new string('*', word.Length - 2) + word[^1];
        var keep = Math.Min(3, word.Length / 4);
        return word[..keep] + new string('*', Math.Min(12, word.Length - keep * 2)) + word[^keep..];
    }
}
