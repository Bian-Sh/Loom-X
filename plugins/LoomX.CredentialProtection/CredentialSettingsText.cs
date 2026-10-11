using System.Globalization;
using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>凭据保护设置页的多语言文案。凭据值本身绝不出现在这里。</summary>
/// <remarks>
/// 文案来自插件自有资源 <c>i18n/&lt;culture&gt;.json</c>，由 <see cref="PluginTextCatalog"/> 统一解析。
/// 字段名保持不变，UI 与命令层无需感知文案来源。
/// </remarks>
internal sealed record CredentialSettingsText(
    CultureInfo Culture,
    PluginTextCatalog Catalog,
    string AddSectionTitle,
    string AddSectionHint,
    string FieldValue,
    string FieldKind,
    string KindName,
    string KindPattern,
    string ButtonAdd,
    string ProtectionTitle,
    string StateActive,
    string StateInactive,
    string ProtectionBody,
    string ProtectionKeepsTitle,
    string ProtectionKeepsBody,
    string ProtectionRiskTitle,
    string ProtectionRiskBody,
    string RulesSectionTitle,
    string EmptyRulesHint,
    string MoreRulesHint,
    string TagName,
    string TagPattern,
    string BaselineNameLabel,
    string BaselinePatternLabel,
    string EnableToggleLabel,
    string ButtonEdit,
    string ButtonDelete,
    string ButtonSave,
    string ButtonCancel,
    string BuiltinSectionTitle,
    string BuiltinSectionHint,
    string BaselineSectionTitle,
    string BaselineSectionHint,
    string GroupCoreTitle,
    string GroupCoreHint,
    string GroupCredentialsTitle,
    string GroupCredentialsHint,
    string GroupNetworkTitle,
    string GroupNetworkHint,
    string GroupEntitiesTitle,
    string GroupEntitiesHint,
    string ButtonAllOn,
    string ButtonAllOff,
    string RuleCountFormat,
    string PrefixSectionTitle,
    string PrefixSectionHint,
    string FieldPrefix,
    string ButtonAddPrefix,
    string EmptyPrefixHint,
    string RuleAdded,
    string RuleUpdated,
    string RuleDeleted,
    string RuleEnabled,
    string RuleDisabled,
    string RuleNotFound,
    string BuiltinEnabled,
    string BuiltinDisabled,
    string BuiltinNotFound,
    string AllBuiltinsEnabled,
    string AllBuiltinsDisabled,
    string PrefixAdded,
    string PrefixDeleted,
    string PrefixNotFound,
    string ErrorInvalidPattern,
    string ErrorInvalidValue,
    string ErrorDuplicateRule,
    string ErrorTooManyRules,
    string ErrorInvalidPrefix,
    string ErrorDuplicatePrefix,
    string ErrorTooManyPrefixes,
    string ErrorUnknown)
{
    public static CredentialSettingsText Resolve(string cultureName)
    {
        var catalog = PluginTextCatalog.For(
            typeof(CredentialSettingsText),
            CredentialProtectionPlugin.PluginId,
            cultureName);
        var get = catalog.Get;
        return new CredentialSettingsText(
            catalog.Culture,
            catalog,
            get("addSectionTitle"),
            get("addSectionHint"),
            get("fieldValue"),
            get("fieldKind"),
            get("kindName"),
            get("kindPattern"),
            get("buttonAdd"),
            get("protectionTitle"),
            get("stateActive"),
            get("stateInactive"),
            get("protectionBody"),
            get("protectionKeepsTitle"),
            get("protectionKeepsBody"),
            get("protectionRiskTitle"),
            get("protectionRiskBody"),
            get("rulesSectionTitle"),
            get("emptyRulesHint"),
            get("moreRulesHint"),
            get("tagName"),
            get("tagPattern"),
            get("baselineNameLabel"),
            get("baselinePatternLabel"),
            get("enableToggleLabel"),
            get("buttonEdit"),
            get("buttonDelete"),
            get("buttonSave"),
            get("buttonCancel"),
            get("builtinSectionTitle"),
            get("builtinSectionHint"),
            get("baselineSectionTitle"),
            get("baselineSectionHint"),
            get("groupCoreTitle"),
            get("groupCoreHint"),
            get("groupCredentialsTitle"),
            get("groupCredentialsHint"),
            get("groupNetworkTitle"),
            get("groupNetworkHint"),
            get("groupEntitiesTitle"),
            get("groupEntitiesHint"),
            get("buttonAllOn"),
            get("buttonAllOff"),
            get("ruleCountFormat"),
            get("prefixSectionTitle"),
            get("prefixSectionHint"),
            get("fieldPrefix"),
            get("buttonAddPrefix"),
            get("emptyPrefixHint"),
            get("ruleAdded"),
            get("ruleUpdated"),
            get("ruleDeleted"),
            get("ruleEnabled"),
            get("ruleDisabled"),
            get("ruleNotFound"),
            get("builtinEnabled"),
            get("builtinDisabled"),
            get("builtinNotFound"),
            get("allBuiltinsEnabled"),
            get("allBuiltinsDisabled"),
            get("prefixAdded"),
            get("prefixDeleted"),
            get("prefixNotFound"),
            get("errorInvalidPattern"),
            get("errorInvalidValue"),
            get("errorDuplicateRule"),
            get("errorTooManyRules"),
            get("errorInvalidPrefix"),
            get("errorDuplicatePrefix"),
            get("errorTooManyPrefixes"),
            get("errorUnknown"));
    }

    /// <summary>内建规则卡片的名称；未登记时保留原始标识，不显示空白也不泄漏资源键名。</summary>
    public string BuiltinName(string key)
    {
        var name = Catalog.Get("builtin." + key + ".name");
        return name.StartsWith("builtin.", StringComparison.Ordinal) ? key : name;
    }

    /// <summary>规则卡片的第二行说明；缺省时只展示名称，不留空行。</summary>
    public string BuiltinHint(string key)
    {
        var hint = Catalog.Get("builtin." + key + ".hint");
        return hint.StartsWith("builtin.", StringComparison.Ordinal) ? string.Empty : hint;
    }

    public string GroupTitle(CredentialBuiltinGroup group) => group switch
    {
        CredentialBuiltinGroup.Core => GroupCoreTitle,
        CredentialBuiltinGroup.Credentials => GroupCredentialsTitle,
        CredentialBuiltinGroup.Network => GroupNetworkTitle,
        _ => GroupEntitiesTitle,
    };

    public string GroupHint(CredentialBuiltinGroup group) => group switch
    {
        CredentialBuiltinGroup.Core => GroupCoreHint,
        CredentialBuiltinGroup.Credentials => GroupCredentialsHint,
        CredentialBuiltinGroup.Network => GroupNetworkHint,
        _ => GroupEntitiesHint,
    };

    /// <summary>分组标题右侧的启用计数徽章，形如 <c>3/5</c>。</summary>
    public string RuleCount(int enabled, int total) =>
        string.Format(Culture, RuleCountFormat, enabled, total);
}
