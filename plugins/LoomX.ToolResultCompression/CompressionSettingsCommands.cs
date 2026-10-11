using System.Globalization;
using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 设置页命令处理：所有改动都在插件内落库。
/// 返回给宿主的 Message 只含安全摘要，绝不回显工具结果内容。
/// </summary>
internal static class CompressionSettingsCommands
{
    public static ValueTask<PluginUiCommandResult> Execute(
        ToolResultCompressionOptionsStore store,
        ToolResultCompressionObservability observability,
        PluginUiCommand command,
        string cultureName)
    {
        var text = CompressionSettingsText.Resolve(cultureName);
        var options = store.Current;

        switch (command.CommandId)
        {
            case CompressionSettingsUi.ToggleEnabled:
            {
                var enabled = ParseBoolean(command.GetArgument(CompressionSettingsUi.EnabledField));
                store.Save(options with { Enabled = enabled });
                return ValueTask.FromResult(PluginUiCommandResult.Success(
                    enabled ? text.MsgEnabled : text.MsgDisabled));
            }

            case CompressionSettingsUi.SetLevel:
            {
                var level = CompressionSettingsUi.ResolveLevel(
                    command.GetArgument(CompressionSettingsUi.LevelField), text);
                var preset = ToolResultCompressionOptions.FromLevel(level) with { Enabled = options.Enabled };
                store.Save(preset);
                return ValueTask.FromResult(PluginUiCommandResult.Success(
                    string.Format(text.Culture, text.MsgLevelApplied, LevelLabel(level, text))));
            }

            case CompressionSettingsUi.ApplyPolicy:
            {
                var keepRecent = command.GetArgument(CompressionSettingsUi.KeepRecentField).Trim();
                var minSaving = command.GetArgument(CompressionSettingsUi.MinSavingField).Trim();

                if (!int.TryParse(keepRecent, NumberStyles.Integer, text.Culture, out var keepValue)
                    || !double.TryParse(minSaving, NumberStyles.Float, text.Culture, out var ratioValue))
                {
                    return ValueTask.FromResult(PluginUiCommandResult.Failure(text.MsgInvalidNumber));
                }

                store.Save(options with
                {
                    KeepRecentToolResults = Math.Clamp(keepValue, 0, 32),
                    MinSavingsRatio = Math.Clamp(ratioValue / 100d, 0, 0.95),
                });
                return ValueTask.FromResult(PluginUiCommandResult.Success(
                    string.Format(text.Culture, text.MsgKeepRecentApplied, Math.Clamp(keepValue, 0, 32))));
            }

            case CompressionSettingsUi.ToggleJsonFold:
                return ValueTask.FromResult(ToggleStrategy(
                    store, text, command,
                    CompressionSettingsUi.JsonFoldField,
                    current => current with { EnableJsonFolding = !current.EnableJsonFolding }));

            case CompressionSettingsUi.ToggleDedupe:
                return ValueTask.FromResult(ToggleStrategy(
                    store, text, command,
                    CompressionSettingsUi.DedupeField,
                    current => current with { EnableDedupe = !current.EnableDedupe }));

            case CompressionSettingsUi.ToggleCommandFilters:
                return ValueTask.FromResult(ToggleStrategy(
                    store, text, command,
                    CompressionSettingsUi.CommandFiltersField,
                    current => current with { EnableCommandFilters = !current.EnableCommandFilters }));

            case CompressionSettingsUi.ResetStats:
                // 只清零统计：配置与压缩行为不受影响。
                observability.Reset();
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.MsgStatsReset));

            case CompressionSettingsUi.RestoreDefaults:
                store.Save(ToolResultCompressionOptions.Default with { Enabled = options.Enabled });
                return ValueTask.FromResult(PluginUiCommandResult.Success(text.MsgDefaultsRestored));

            default:
                return ValueTask.FromResult(PluginUiCommandResult.Unhandled);
        }
    }

    /// <summary>
    /// 开关类命令：以插件内的当前值为准取反，不以宿主回传的勾选值为准。
    /// 宿主只回传它渲染时的值，用它当真值会在刷新竞态下把开关按反。
    /// </summary>
    private static PluginUiCommandResult ToggleStrategy(
        ToolResultCompressionOptionsStore store,
        CompressionSettingsText text,
        PluginUiCommand command,
        string fieldId,
        Func<ToolResultCompressionOptions, ToolResultCompressionOptions> mutate)
    {
        var current = store.Current;
        store.Save(mutate(current));
        _ = command.GetArgument(fieldId);
        return PluginUiCommandResult.Success(text.MsgStrategyUpdated);
    }

    private static string LevelLabel(string level, CompressionSettingsText text) => level switch
    {
        CompressionLevel.Conservative => text.LevelConservative,
        CompressionLevel.Aggressive => text.LevelAggressive,
        _ => text.LevelStandard,
    };

    private static bool ParseBoolean(string raw) =>
        bool.TryParse(raw, out var value)
            ? value
            : raw is "1" or "on" or "true" or "True";
}
