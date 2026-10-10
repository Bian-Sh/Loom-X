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
    public const string EnableAllBuiltins = "enable_all_builtins";
    public const string DisableAllBuiltins = "disable_all_builtins";
    public const string AddPrefix = "add_prefix";
    public const string DeletePrefix = "delete_prefix";

    public const string NewValueField = "new_value";
    public const string NewKindField = "new_kind";
    public const string NewPrefixField = "new_prefix";

    /// <summary>列表一次渲染的规则上限，避免节点数突破宿主校验限制；超出时给出总数提示。</summary>
    private const int MaxVisibleRules = 24;

    /// <summary>内置形态规则卡片每行的列宽策略：开关按内容自适应，名称与说明按 1 : 2.4 分配。</summary>
    private static readonly double[] BuiltinCardColumns = [0, 1, 2.4];

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
                BuildRuleSection(store, text, editingRuleId),
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
                            // 只读取新增表单的两个字段：命令参数必须按需收集，
                            // 整页字段并入会让参数数量随页面规模增长并被宿主拒绝。
                            new PluginUiGridItem(new PluginUiButtonNode(
                                text.ButtonAdd,
                                AddRule,
                                Tone: PluginUiTone.Accent,
                                IsPrimary: true,
                                ReadsFields: [NewValueField, NewKindField])),
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

    /// <summary>
    /// 自定义规则区：只列用户自己新增的规则。
    /// 内置基线规则（name.* / pattern.*）属于引擎的识别底座而非用户资产，
    /// 混进列表只会得到一屏与用户配置无关的内置规则，因此归入内置规则区统一开关。
    /// </summary>
    private static PluginUiNode BuildRuleSection(
        SensitiveRuleStore store,
        CredentialSettingsText text,
        string? editingRuleId)
    {
        var custom = store.Rules.Where(item => !item.Builtin).ToArray();
        var rows = new List<PluginUiNode>
        {
            new PluginUiTextNode(
                custom.Length == 0
                    ? text.RulesSectionTitle
                    : $"{text.RulesSectionTitle} · {custom.Length}",
                PluginUiTextRole.Title),
        };

        if (custom.Length == 0)
        {
            rows.Add(new PluginUiTextNode(text.EmptyRulesHint, PluginUiTextRole.Caption, Wrap: true));
        }
        else
        {
            foreach (var rule in custom.Take(MaxVisibleRules))
                rows.Add(BuildRuleRow(text, rule, editingRuleId));

            if (custom.Length > MaxVisibleRules)
                rows.Add(new PluginUiTextNode(
                    string.Format(text.Culture, text.MoreRulesHint, MaxVisibleRules, custom.Length),
                    PluginUiTextRole.Caption,
                    Wrap: true));
        }

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(rows, Spacing: 8),
            Padding: 14,
            CornerRadius: 10);
    }

    /// <summary>
    /// 自定义规则条目：开关前置且不显示任何标签文字，
    /// 右侧依次是原样的规则值、形态标签与编辑/删除操作。
    /// </summary>
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
                        IsPrimary: true,
                        ReadsFields: [EditValueField(rule.Id)]),
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
                // 开关在最前，Label 传空串：宿主只渲染开关本体，不加「启用」文字。
                new PluginUiGridItem(
                    new PluginUiToggleFieldNode(
                        EnabledField(rule.Id),
                        string.Empty,
                        rule.Enabled,
                        CommandId: ToggleRule,
                        Arguments: new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id }),
                    HorizontalAlignment: PluginUiAlignment.Start,
                    VerticalAlignment: PluginUiAlignment.Center),
                new PluginUiGridItem(new PluginUiTextNode(
                    rule.Value, PluginUiTextRole.Body, Wrap: true)),
                new PluginUiGridItem(new PluginUiTextNode(tag, PluginUiTextRole.Caption, Wrap: true)),
                new PluginUiGridItem(
                    new PluginUiButtonNode(
                        text.ButtonEdit,
                        BeginEdit,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id }),
                    HorizontalAlignment: PluginUiAlignment.End,
                    VerticalAlignment: PluginUiAlignment.Center),
                new PluginUiGridItem(
                    new PluginUiButtonNode(
                        text.ButtonDelete,
                        DeleteRule,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id },
                        PluginUiTone.Danger),
                    HorizontalAlignment: PluginUiAlignment.End,
                    VerticalAlignment: PluginUiAlignment.Center),
            ],
            ColumnSpacing: 8,
            ColumnWeights: [0, 1, 0, 0, 0]);
    }

    /// <summary>
    /// 内置规则区：按场景分组，每组标题带启用计数徽章，
    /// 组内是三列卡片（开关前置 + 名称 + 说明），顶部提供全部开启/关闭。
    /// </summary>
    private static PluginUiNode BuildBuiltinSection(SensitiveRuleStore store, CredentialSettingsText text)
    {
        var enabled = store.BuiltinRules;
        var rows = new List<PluginUiNode>
        {
            new PluginUiGridNode(
                2,
                [
                    new PluginUiGridItem(new PluginUiStackNode(
                        [
                            new PluginUiTextNode(text.BuiltinSectionTitle, PluginUiTextRole.Title),
                            new PluginUiTextNode(text.BuiltinSectionHint, PluginUiTextRole.Caption, Wrap: true),
                        ],
                        Spacing: 3)),
                    new PluginUiGridItem(
                        new PluginUiStackNode(
                            [
                                new PluginUiButtonNode(
                                    text.ButtonAllOn,
                                    EnableAllBuiltins,
                                    Tone: PluginUiTone.Accent),
                                new PluginUiButtonNode(text.ButtonAllOff, DisableAllBuiltins),
                            ],
                            PluginUiOrientation.Horizontal,
                            Spacing: 8),
                        HorizontalAlignment: PluginUiAlignment.End,
                        VerticalAlignment: PluginUiAlignment.Center),
                ],
                ColumnSpacing: 12),
        };

        foreach (var group in CredentialBuiltinGroups.Order)
        {
            var members = CredentialBuiltinRules.Defaults
                .Where(rule => CredentialBuiltinGroups.GroupOf(rule.Key) == group)
                .ToArray();
            if (members.Length == 0) continue;

            var onCount = members.Count(rule =>
                enabled.TryGetValue(rule.Key, out var on) ? on : rule.EnabledByDefault);

            rows.Add(new PluginUiTextNode(
                $"{text.GroupTitle(group)}（{text.RuleCount(onCount, members.Length)}）",
                PluginUiTextRole.Body));
            rows.Add(new PluginUiTextNode(text.GroupHint(group), PluginUiTextRole.Caption, Wrap: true));
            rows.Add(new PluginUiGridNode(
                3,
                [.. members.Select(rule => new PluginUiGridItem(
                    BuildBuiltinCard(text, rule, IsBuiltinEnabled(enabled, rule)),
                    VerticalAlignment: PluginUiAlignment.Start))],
                ColumnSpacing: 10,
                RowSpacing: 10));
        }

        // 引擎识别底座：字段名与凭据形态基线，与形态规则同样只可开关。
        var baseline = store.Rules.Where(item => item.Builtin).ToArray();
        if (baseline.Length > 0)
        {
            var baselineOn = baseline.Count(item => item.Enabled);
            rows.Add(new PluginUiTextNode(
                $"{text.BaselineSectionTitle}（{text.RuleCount(baselineOn, baseline.Length)}）",
                PluginUiTextRole.Body));
            rows.Add(new PluginUiTextNode(text.BaselineSectionHint, PluginUiTextRole.Caption, Wrap: true));
            rows.Add(new PluginUiGridNode(
                3,
                [.. baseline.Select(rule => new PluginUiGridItem(
                    BuildBaselineCard(text, rule),
                    VerticalAlignment: PluginUiAlignment.Start))],
                ColumnSpacing: 10,
                RowSpacing: 10));
        }

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(rows, Spacing: 10),
            Padding: 14,
            CornerRadius: 10);
    }

    private static bool IsBuiltinEnabled(
        IReadOnlyDictionary<string, bool> enabled,
        CredentialBuiltinRule rule) =>
        enabled.TryGetValue(rule.Key, out var on) ? on : rule.EnabledByDefault;

    /// <summary>内置形态规则卡片：开关在前，右侧是名称与一行说明。</summary>
    private static PluginUiNode BuildBuiltinCard(
        CredentialSettingsText text,
        CredentialBuiltinRule rule,
        bool isEnabled) =>
        new PluginUiSurfaceNode(
            new PluginUiGridNode(
                3,
                [
                    new PluginUiGridItem(
                        new PluginUiToggleFieldNode(
                            $"builtin_{rule.Key}",
                            string.Empty,
                            isEnabled,
                            CommandId: ToggleBuiltin,
                            Arguments: new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = rule.Key }),
                        HorizontalAlignment: PluginUiAlignment.Start,
                        VerticalAlignment: PluginUiAlignment.Center),
                    new PluginUiGridItem(
                        new PluginUiTextNode(text.BuiltinName(rule.Key), PluginUiTextRole.Body, Wrap: true),
                        VerticalAlignment: PluginUiAlignment.Center),
                    new PluginUiGridItem(
                        new PluginUiTextNode(text.BuiltinHint(rule.Key), PluginUiTextRole.Caption, Wrap: true),
                        VerticalAlignment: PluginUiAlignment.Center),
                ],
                ColumnSpacing: 8,
                ColumnWeights: BuiltinCardColumns),
            // 开启时用强调底色区分，关闭时回落默认表面，与 maskit 的开关卡片一致。
            Tone: isEnabled ? PluginUiTone.Accent : PluginUiTone.Default,
            Padding: 10,
            CornerRadius: 8);

    /// <summary>
    /// 基线规则卡片：形状同形态规则卡片。
    /// 基线规则来自插件自身的只读基线表、不是用户凭据，因此展示的是由规则 id 派生的可读标签
    /// （如 <c>pattern.openai-sk</c> → OpenAI API Key）；直接铺开原值正则会占满整张卡片，
    /// 而一律打码又只剩认不出内容的星号。
    /// </summary>
    private static PluginUiNode BuildBaselineCard(CredentialSettingsText text, SensitiveRule rule)
    {
        var isName = rule.Kind == SensitiveRuleKind.Name;
        return new PluginUiSurfaceNode(
            new PluginUiGridNode(
                3,
                [
                    new PluginUiGridItem(
                        new PluginUiToggleFieldNode(
                            EnabledField(rule.Id),
                            string.Empty,
                            rule.Enabled,
                            CommandId: ToggleRule,
                            Arguments: new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id }),
                        HorizontalAlignment: PluginUiAlignment.Start,
                        VerticalAlignment: PluginUiAlignment.Center),
                    new PluginUiGridItem(
                        new PluginUiTextNode(
                            isName ? text.BaselineNameLabel : text.BaselinePatternLabel,
                            PluginUiTextRole.Caption,
                            Wrap: true),
                        VerticalAlignment: PluginUiAlignment.Center),
                    new PluginUiGridItem(
                        new PluginUiTextNode(BaselineLabel(rule), PluginUiTextRole.Body, Wrap: true),
                        VerticalAlignment: PluginUiAlignment.Center),
                ],
                ColumnSpacing: 8,
                ColumnWeights: BuiltinCardColumns),
            Tone: rule.Enabled ? PluginUiTone.Accent : PluginUiTone.Default,
            Padding: 10,
            CornerRadius: 8);
    }

    /// <summary>由基线规则 id 派生可读标签：<c>pattern.openai-sk</c> → <c>Openai Sk</c>。</summary>
    private static string BaselineLabel(SensitiveRule rule)
    {
        var name = rule.Id.StartsWith("pattern.", StringComparison.Ordinal)
            ? rule.Id["pattern.".Length..]
            : rule.Id.StartsWith("name.", StringComparison.Ordinal)
                ? rule.Id["name.".Length..]
                : rule.Id;

        var builder = new System.Text.StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '-' || character == '_')
            {
                // 分隔符转成空格，避免留下 "Openai- Sk" 这种断词。
                if (builder.Length > 0) builder.Append(' ');
                continue;
            }
            builder.Append(index == 0 || name[index - 1] is '-' or '_'
                ? char.ToUpperInvariant(character)
                : character);
        }
        return builder.ToString();
    }

    /// <summary>
    /// 密钥前缀区：已配置前缀以标签（chip）形式平铺，末尾是新增输入框，
    /// 与 maskit 的前缀标签 + 内联新增一致。
    /// </summary>
    private static PluginUiNode BuildPrefixSection(SensitiveRuleStore store, CredentialSettingsText text)
    {
        var prefixes = store.SecretPrefixes;
        var chips = prefixes
            .Select(prefix => new PluginUiSurfaceNode(
                new PluginUiStackNode(
                    [
                        new PluginUiTextNode(prefix, PluginUiTextRole.Body),
                        new PluginUiButtonNode(
                            text.ButtonDelete,
                            DeletePrefix,
                            new Dictionary<string, string>(StringComparer.Ordinal) { ["prefix"] = prefix },
                            PluginUiTone.Danger),
                    ],
                    PluginUiOrientation.Horizontal,
                    Spacing: 6),
                Padding: 8,
                CornerRadius: 8))
            .Cast<PluginUiNode>()
            .ToArray();

        var rows = new List<PluginUiNode>
        {
            new PluginUiTextNode(text.PrefixSectionTitle, PluginUiTextRole.Title),
            new PluginUiTextNode(text.PrefixSectionHint, PluginUiTextRole.Caption, Wrap: true),
        };

        if (chips.Length == 0)
        {
            rows.Add(new PluginUiTextNode(text.EmptyPrefixHint, PluginUiTextRole.Caption, Wrap: true));
        }
        else
        {
            rows.Add(new PluginUiGridNode(
                3,
                [.. chips.Select(chip => new PluginUiGridItem(
                    chip,
                    HorizontalAlignment: PluginUiAlignment.Stretch,
                    VerticalAlignment: PluginUiAlignment.Center))],
                ColumnSpacing: 8,
                RowSpacing: 8));
        }

        rows.Add(new PluginUiGridNode(
            2,
            [
                new PluginUiGridItem(new PluginUiTextFieldNode(
                    NewPrefixField, text.FieldPrefix, Watermark: "sk-")),
                new PluginUiGridItem(
                    new PluginUiButtonNode(
                        text.ButtonAddPrefix,
                        AddPrefix,
                        Tone: PluginUiTone.Accent,
                        IsPrimary: true,
                        ReadsFields: [NewPrefixField]),
                    HorizontalAlignment: PluginUiAlignment.Start,
                    VerticalAlignment: PluginUiAlignment.Center),
            ],
            ColumnSpacing: 10,
            // 输入框占满剩余宽度，新增按钮按内容自适应。
            ColumnWeights: [1, 0]));

        return new PluginUiSurfaceNode(
            new PluginUiStackNode(rows, Spacing: 10),
            Padding: 14,
            CornerRadius: 10);
    }
}
