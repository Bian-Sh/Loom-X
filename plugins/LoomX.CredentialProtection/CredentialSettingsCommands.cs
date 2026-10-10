using System.Globalization;
using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>
/// 设置页命令处理：所有增删改查都在插件内落库。
/// 返回给宿主的 Message 只含安全摘要，绝不回显规则值或凭据原文。
/// </summary>
internal static class CredentialSettingsCommands
{
    public static ValueTask<PluginUiCommandResult> Execute(
        SensitiveRuleStore store,
        PluginUiCommand command,
        string cultureName)
    {
        var text = CredentialSettingsText.Resolve(cultureName);

        switch (command.CommandId)
        {
            case CredentialSettingsUi.AddRule:
            {
                var value = command.GetArgument(CredentialSettingsUi.NewValueField);
                var kind = ResolveKind(command.GetArgument(CredentialSettingsUi.NewKindField), text);
                if (!store.AddRule(kind, value, out _, out var addError))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(
                        DescribeFailure(addError, text)));
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.RuleAdded));
            }

            case CredentialSettingsUi.SaveEdit:
            {
                var ruleId = command.GetArgument("rule_id");
                var value = command.GetArgument(CredentialSettingsUi.EditValueField(ruleId));
                if (!store.UpdateRule(ruleId, value, out var updateError))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(
                        DescribeFailure(updateError, text)));
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.RuleUpdated));
            }

            case CredentialSettingsUi.DeleteRule:
            {
                var ruleId = command.GetArgument("rule_id");
                if (!store.RemoveRule(ruleId))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(text.RuleNotFound));
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.RuleDeleted));
            }

            case CredentialSettingsUi.ToggleRule:
            {
                var ruleId = command.GetArgument("rule_id");
                var enabled = ParseBoolean(command.GetArgument(CredentialSettingsUi.EnabledField(ruleId)));
                if (!store.SetRuleEnabled(ruleId, enabled))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(text.RuleNotFound));
                return ValueTask.FromResult(PluginUiCommandResult.Success(
                    enabled ? text.RuleEnabled : text.RuleDisabled));
            }

            case CredentialSettingsUi.ToggleBuiltin:
            {
                var key = command.GetArgument("key");
                var enabled = ParseBoolean(command.GetArgument($"builtin_{key}"));
                if (!store.SetBuiltinEnabled(key, enabled))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(text.BuiltinNotFound));
                return ValueTask.FromResult(PluginUiCommandResult.Success(
                    enabled ? text.BuiltinEnabled : text.BuiltinDisabled));
            }

            case CredentialSettingsUi.EnableAllBuiltins:
            {
                store.SetAllBuiltinEnabled(true);
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.AllBuiltinsEnabled));
            }

            case CredentialSettingsUi.DisableAllBuiltins:
            {
                store.SetAllBuiltinEnabled(false);
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.AllBuiltinsDisabled));
            }

            case CredentialSettingsUi.AddPrefix:
            {
                var prefix = command.GetArgument(CredentialSettingsUi.NewPrefixField);
                if (!store.AddPrefix(prefix, out var prefixError))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(
                        DescribeFailure(prefixError, text)));
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.PrefixAdded));
            }

            case CredentialSettingsUi.DeletePrefix:
            {
                var prefix = command.GetArgument("prefix");
                if (!store.RemovePrefix(prefix))
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(text.PrefixNotFound));
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.PrefixDeleted));
            }

            default:
                return ValueTask.FromResult(PluginUiCommandResult.Unhandled);
        }
    }

    private static SensitiveRuleKind ResolveKind(string raw, CredentialSettingsText text) =>
        string.Equals(raw, text.KindPattern, StringComparison.Ordinal)
            ? SensitiveRuleKind.Pattern
            : SensitiveRuleKind.Name;

    private static bool ParseBoolean(string raw) =>
        bool.TryParse(raw, out var value)
            ? value
            : raw is "1" or "on" or "true" or "True";

    private static string DescribeFailure(string? error, CredentialSettingsText text) => error switch
    {
        "INVALID_PATTERN" => text.ErrorInvalidPattern,
        "INVALID_VALUE" => text.ErrorInvalidValue,
        "DUPLICATE_RULE" => text.ErrorDuplicateRule,
        "TOO_MANY_RULES" => text.ErrorTooManyRules,
        "INVALID_PREFIX" => text.ErrorInvalidPrefix,
        "DUPLICATE_PREFIX" => text.ErrorDuplicatePrefix,
        "TOO_MANY_PREFIXES" => text.ErrorTooManyPrefixes,
        _ => text.ErrorUnknown,
    };
}