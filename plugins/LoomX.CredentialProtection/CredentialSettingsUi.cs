using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>
/// 凭据保护的声明式设置页。只产出平台无关的节点树与命令标识，
/// 不引用任何 Avalonia 控件，也不直接改状态：所有增删改查都通过命令回传由插件执行。
/// </summary>
public static class CredentialSettingsUi
{
    public const string SettingsContributionId = "settings";
    public const string OverviewContributionId = "observability";

    public const string AddRule = "add_rule";
    public const string BeginEdit = "begin_edit";
    public const string CancelEdit = "cancel_edit";
    public const string SaveEdit = "save_edit";
    public const string DeleteRule = "delete_rule";
    public const string ToggleRule = "toggle_rule";
    public const string ToggleBuiltin = "toggle_builtin";
    public const string AddPrefix = "add_prefix";
    public const string DeletePrefix = "delete_prefix";

    public const string NewValueField = "new_value";
    public const string NewKindField = "new_kind";
    public const string NewPrefixField = "new_prefix";

    /// <summary>列表一次渲染的规则上限，避免节点数突破宿主校验限制；超出时给出总数提示。</summary>
    private const int MaxVisibleRules = 24;

    public static string EnabledField(string ruleId) => $"enabled_{ruleId}";
    public static string EditValueField(string ruleId) => $"edit_value_{ruleId}";

    /// <summary>插件详情正文：可交互的规则设置页。</summary>
    public static PluginUiNode BuildSettings(
        SensitiveRuleStore store,
        string cultureName,
        string? editingRuleId,
        bool activeProtectionEnabled) =>
        Build(
            store,
            CredentialSettingsText.Resolve(cultureName),
            editingRuleId,
            activeProtectionEnabled);

    private static PluginUiNode Build(
        SensitiveRuleStore store,
        CredentialSettingsText text,
        string? editingRuleId,
        bool activeProtectionEnabled)
    {
        return new PluginUiStackNode(
            [
                BuildAddSection(text),
                BuildProtectionNoticeSection(text, activeProtectionEnabled),
                BuildRuleSection(store, text, rules: null, editingRuleId),
                BuildBuiltinSection(store, text),
                BuildPrefixSection(store, text),
            ],
            Spacing: 14);
    }

    private static PluginUiNode BuildAddSection(CredentialSettingsText text) =>
        new PluginUiSurfaceNode(
            new PluginUiStackNode(
                [
                    new PluginUiTextNode(text.AddSectionTitle, PluginUiTextRole.Title),
                    new PluginUiTextNode(text.AddSectionHint, PluginUiTextRole.Caption, Wrap: true),
                    new PluginUiGridNode(
                        3,
                        [
                            new PluginUiGridItem(new PluginUiTextFieldNode(
                                NewValueField, text.FieldValue, Watermark: "Alice")),
                            new PluginUiGridItem(new PluginUiChoiceFieldNode(
                                NewKindField,
                                text.FieldKind,
                                [text.KindName, text.KindPattern],
                                text.KindName)),
                            new PluginUiGridItem(new PluginUiButtonNode(
                                text.ButtonAdd, AddRule, Tone: PluginUiTone.Accent, IsPrimary: true)),
                        ],
                        ColumnSpacing: 10),
                ],
                Spacing: 10),
            Padding: 14,
            CornerRadius: 10);

    /// <summary>
    /// 停用利害关系声明：明确说明「停用停止什么、仍保留什么」。
    /// 停用只关闭主动保护，历史引用解析与恢复必须继续，否则旧会话中的占位符会永久失效。
    /// </summary>
    private static PluginUiNode BuildProtectionNoticeSection(
        CredentialSettingsText text,
        bool activeProtectionEnabled) =>
        new PluginUiSurfaceNode(
            new PluginUiStackNode(
                [
                    new PluginUiGridNode(
                        2,
                        [
                            new PluginUiGridItem(new PluginUiTextNode(
                                text.ProtectionTitle, PluginUiTextRole.Title)),
                            new PluginUiGridItem(new PluginUiTextNode(
                                activeProtectionEnabled ? text.StateActive : text.StateInactive,
                                PluginUiTextRole.Body,
                                activeProtectionEnabled ? PluginUiTone.Success : PluginUiTone.Warning),
                                HorizontalAlignment: PluginUiAlignment.End),
                        ]),
                    new PluginUiTextNode(text.ProtectionBody, PluginUiTextRole.Caption, Wrap: true),
                    new PluginUiDividerNode(),
                    new PluginUiTextNode(text.ProtectionKeepsTitle, PluginUiTextRole.Body, Wrap: true),
                    new PluginUiTextNode(text.ProtectionKeepsBody, PluginUiTextRole.Caption, Wrap: true),
                    new PluginUiDividerNode(),
                    new PluginUiTextNode(text.ProtectionRiskTitle, PluginUiTextRole.Body, Wrap: true),
                    new PluginUiTextNode(text.ProtectionRiskBody, PluginUiTextRole.Caption, Wrap: true),
                ],
                Spacing: 8),
            Tone: activeProtectionEnabled ? PluginUiTone.Default : PluginUiTone.Warning,
            Padding: 14,
            CornerRadius: 10);

    private static PluginUiNode BuildRuleSection(
        SensitiveRuleStore store,
        CredentialSettingsText text,
        IReadOnlyList<SensitiveRule>? rules,
        string? editingRuleId)
    {
        var all = store.Rules;
        var visible = rules ?? all;
        var rows = new List<PluginUiNode>
        {
            new PluginUiTextNode($"{text.RulesSectionTitle} · {all.Count}", PluginUiTextRole.Title),
        };

        if (all.Count == 0)
        {
            rows.Add(new PluginUiTextNode(text.EmptyRulesHint, PluginUiTextRole.Caption, Wrap: true));
        }
        else
        {
            foreach (var rule in visible.Take(MaxVisibleRules))
                rows.Add(BuildRuleRow(text, rule, editingRuleId));

            if (all.Count > MaxVisibleRules)
                rows.Add(new PluginUiTextNode(
                    string.Format(text.Culture, text.MoreRulesHint, MaxVisibleRules, all.Count),
                    PluginUiTextRole.Caption,
                    Wrap: true));
        }

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(rows, Spacing: 8),
            Padding: 14,
            CornerRadius: 10);
    }

    private static PluginUiNode BuildRuleRow(
        CredentialSettingsText text,
        SensitiveRule rule,
        string? editingRuleId)
    {
        if (string.Equals(rule.Id, editingRuleId, StringComparison.Ordinal))
        {
return new PluginUiStackNode(
                [
                    new PluginUiTextFieldNode(
                        EditValueField(rule.Id), text.FieldValue, rule.Value),
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

        var tag = rule.Kind == SensitiveRuleKind.Pattern ? text.TagPattern : text.TagName;
        return new PluginUiGridNode(
            5,
            [
                new PluginUiGridItem(new PluginUiTextNode(
                    MaskValue(rule.Value, rule.Kind), PluginUiTextRole.Body, Wrap: true)),
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

    private static PluginUiNode BuildBuiltinSection(SensitiveRuleStore store, CredentialSettingsText text)
    {
        var enabled = store.BuiltinRules;
        var toggles = new List<PluginUiGridItem>();
        foreach (var rule in CredentialBuiltinRules.Defaults)
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

    private static PluginUiNode BuildPrefixSection(SensitiveRuleStore store, CredentialSettingsText text)
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

    /// <summary>
    /// 列表展示时给规则值打码：正则规则恒打码（用户可能写入真实密钥作为模式），
    /// 名称规则仅做部分遮蔽以便辨认。编辑态走明文以便修改。
    /// </summary>
    public static string MaskValue(string value, SensitiveRuleKind kind)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (kind == SensitiveRuleKind.Pattern || value.Length <= 2)
            return new string('*', Math.Min(8, Math.Max(2, value.Length)));
        if (value.Length <= 6) return value[0] + new string('*', value.Length - 2) + value[^1];
        var keep = Math.Min(3, value.Length / 4);
        return value[..keep] + new string('*', Math.Min(12, value.Length - keep * 2)) + value[^keep..];
    }
}