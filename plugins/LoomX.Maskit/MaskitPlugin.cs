using System.Globalization;
using LoomX.Plugins;

namespace LoomX.Maskit;

/// <summary>
/// maskit 脱敏插件：按本地规则在请求离开本机前脱敏、在响应返回后还原。
/// 规则数据 Plugin-owned；设置页通过声明式 UI 动态贡献，宿主不内置任何 maskit 页面。
/// </summary>
public sealed class MaskitPlugin : ILoomXPlugin, IPluginUiContributionProvider, IPluginUiCommandHandler
{
    public const string PluginId = "loomx.maskit";

    private readonly object gate = new();
    private MaskitRuleStore? store;
    private MaskitEngine? engine;
    private string? editingRuleId;
    private string cultureName = "zh-CN";
    private event EventHandler? uiInvalidated;

    public string Id => PluginId;

    /// <summary>规则存储（宿主 Initialize 后可用；测试也可直接注入）。</summary>
    public MaskitRuleStore RuleStore =>
        store ?? throw new InvalidOperationException("插件尚未初始化。");

    public event EventHandler? UiInvalidated
    {
        add { lock (gate) uiInvalidated += value; }
        remove { lock (gate) uiInvalidated -= value; }
    }

    public void Initialize(PluginInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var created = new MaskitRuleStore(context.DataDirectory);
        created.Changed += (_, _) => RaiseUiInvalidated();
        lock (gate)
        {
            store = created;
            engine = new MaskitEngine(created);
        }
    }

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshotStore = RuleStore;
        var text = MaskitUiText.Resolve(context.CultureName);
        var culture = ResolveCulture(context.CultureName);
        lock (gate) cultureName = context.CultureName;

        string? editing;
        lock (gate) editing = editingRuleId;

        return
        [
            new PluginUiContribution(
                PluginUiContribution.CurrentSchemaVersion,
                MaskitSettingsUi.OverviewContributionId,
                PluginUiSlot.CardBody,
                MaskitSettingsUi.BuildCard(snapshotStore, text, culture)),
            new PluginUiContribution(
                PluginUiContribution.CurrentSchemaVersion,
                MaskitSettingsUi.SettingsContributionId,
                PluginUiSlot.DetailBody,
                MaskitSettingsUi.BuildSettings(snapshotStore, text, culture, editing)),
        ];
    }

    public ValueTask<PluginUiCommandResult> ExecuteUiCommandAsync(
        PluginUiCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var text = MaskitUiText.Resolve(CurrentCultureName());
        var result = command.CommandId switch
        {
            MaskitSettingsUi.AddRule => AddRule(command, text),
            MaskitSettingsUi.BeginEdit => BeginEdit(command),
            MaskitSettingsUi.CancelEdit => CancelEdit(),
            MaskitSettingsUi.SaveEdit => SaveEdit(command, text),
            MaskitSettingsUi.DeleteRule => DeleteRule(command, text),
            MaskitSettingsUi.ToggleRule => ToggleRule(command, text),
            MaskitSettingsUi.ToggleBuiltin => ToggleBuiltin(command, text),
            MaskitSettingsUi.AddPrefix => AddPrefix(command, text),
            MaskitSettingsUi.DeletePrefix => DeletePrefix(command, text),
            _ => PluginUiCommandResult.Failure(text.Message("COMMAND_UNKNOWN")),
        };
        return ValueTask.FromResult(result);
    }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        // 宿主保证 Initialize 先于 CreateExtensions 调用；防御性兜底避免空引用。
        MaskitRuleStore snapshotStore;
        MaskitEngine snapshotEngine;
        lock (gate)
        {
            snapshotStore = store ?? new MaskitRuleStore(
                Path.Combine(Path.GetTempPath(), PluginId));
            snapshotEngine = engine ?? new MaskitEngine(snapshotStore);
        }
        yield return new MaskitRequestExtension(snapshotEngine, snapshotStore);
        yield return new MaskitResponseExtension(snapshotEngine, snapshotStore);
    }

    private PluginUiCommandResult AddRule(PluginUiCommand command, MaskitUiText text)
    {
        var category = command.GetArgument(MaskitSettingsUi.NewCategoryField);
        var value = command.GetArgument(MaskitSettingsUi.NewValueField);
        var kind = string.Equals(
            command.GetArgument(MaskitSettingsUi.NewKindField),
            text.KindPattern,
            StringComparison.Ordinal)
            ? MaskitRuleKind.Pattern
            : MaskitRuleKind.Word;
        var wholeWord = IsTrue(command.GetArgument(MaskitSettingsUi.NewWholeField));

        if (RuleStore.AddRule(category, value, kind, wholeWord, out _, out var error))
            return PluginUiCommandResult.Success(text.Message("RULE_ADDED"));
        return PluginUiCommandResult.Failure(text.Message(MapRuleError(error)));
    }

    private PluginUiCommandResult BeginEdit(PluginUiCommand command)
    {
        var ruleId = command.GetArgument("rule_id");
        if (string.IsNullOrEmpty(ruleId)) return PluginUiCommandResult.Unhandled;
        lock (gate) editingRuleId = ruleId;
        RaiseUiInvalidated();
        return PluginUiCommandResult.Notice();
    }

    private PluginUiCommandResult CancelEdit()
    {
        lock (gate) editingRuleId = null;
        RaiseUiInvalidated();
        return PluginUiCommandResult.Notice();
    }

    private PluginUiCommandResult SaveEdit(PluginUiCommand command, MaskitUiText text)
    {
        var ruleId = command.GetArgument("rule_id");
        if (string.IsNullOrEmpty(ruleId)) return PluginUiCommandResult.Unhandled;

        var category = command.GetArgument(MaskitSettingsUi.EditCategoryField(ruleId));
        var value = command.GetArgument(MaskitSettingsUi.EditValueField(ruleId));
        var wholeWord = IsTrue(command.GetArgument(MaskitSettingsUi.EditWholeField(ruleId)));

        if (!RuleStore.UpdateRule(ruleId, category, value, wholeWord, out var error))
        {
            return error == "RULE_NOT_FOUND"
                ? PluginUiCommandResult.Failure(text.Message("RULE_MISSING"))
                : PluginUiCommandResult.Failure(text.Message(MapRuleError(error)));
        }

        lock (gate) editingRuleId = null;
        RaiseUiInvalidated();
        return PluginUiCommandResult.Success(text.Message("RULE_UPDATED"));
    }

    private PluginUiCommandResult DeleteRule(PluginUiCommand command, MaskitUiText text)
    {
        var ruleId = command.GetArgument("rule_id");
        if (string.IsNullOrEmpty(ruleId)) return PluginUiCommandResult.Unhandled;
        return RuleStore.RemoveRule(ruleId)
            ? PluginUiCommandResult.Success(text.Message("RULE_DELETED"))
            : PluginUiCommandResult.Failure(text.Message("RULE_MISSING"));
    }

    private PluginUiCommandResult ToggleRule(PluginUiCommand command, MaskitUiText text)
    {
        var ruleId = command.GetArgument("rule_id");
        if (string.IsNullOrEmpty(ruleId)) return PluginUiCommandResult.Unhandled;
        var enabled = IsTrue(command.GetArgument(MaskitSettingsUi.EnabledField(ruleId)));
        RuleStore.SetRuleEnabled(ruleId, enabled);
        return PluginUiCommandResult.Notice(text.Message("RULE_TOGGLED"));
    }

    private PluginUiCommandResult ToggleBuiltin(PluginUiCommand command, MaskitUiText text)
    {
        var key = command.GetArgument("key");
        if (string.IsNullOrEmpty(key)) return PluginUiCommandResult.Unhandled;
        var enabled = IsTrue(command.GetArgument($"builtin_{key}"));
        RuleStore.SetBuiltinEnabled(key, enabled);
        return PluginUiCommandResult.Notice(text.Message("BUILTIN_TOGGLED"));
    }

    private PluginUiCommandResult AddPrefix(PluginUiCommand command, MaskitUiText text)
    {
        var prefix = command.GetArgument(MaskitSettingsUi.NewPrefixField);
        if (RuleStore.AddPrefix(prefix, out var error))
            return PluginUiCommandResult.Success(text.Message("PREFIX_ADDED"));

        return error switch
        {
            "DUPLICATE_PREFIX" => PluginUiCommandResult.Failure(text.Message("PREFIX_EXISTS")),
            _ => PluginUiCommandResult.Failure(text.Message("PREFIX_INVALID")),
        };
    }

    private PluginUiCommandResult DeletePrefix(PluginUiCommand command, MaskitUiText text)
    {
        var prefix = command.GetArgument("prefix");
        if (string.IsNullOrEmpty(prefix)) return PluginUiCommandResult.Unhandled;
        return RuleStore.RemovePrefix(prefix)
            ? PluginUiCommandResult.Success(text.Message("PREFIX_DELETED"))
            : PluginUiCommandResult.Failure(text.Message("PREFIX_INVALID"));
    }

    private static string MapRuleError(string? error) => error switch
    {
        "DUPLICATE_RULE" => "RULE_EXISTS",
        "INVALID_PATTERN" => "RULE_PATTERN_INVALID",
        "TOO_MANY_RULES" => "RULE_LIMIT",
        _ => "RULE_INVALID",
    };

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private string CurrentCultureName()
    {
        lock (gate) return cultureName;
    }

    private void RaiseUiInvalidated()
    {
        EventHandler? handler;
        lock (gate) handler = uiInvalidated;
        handler?.Invoke(this, EventArgs.Empty);
    }

    private static CultureInfo ResolveCulture(string cultureName)
    {
        try { return CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}

/// <summary>Router 请求扩展点：请求正文发送给外部 Provider 前按规则脱敏。</summary>
public sealed class MaskitRequestExtension(MaskitEngine engine, MaskitRuleStore store) : IRequestExtension
{
    public string ExtensionId => "maskit.request";

    public ExtensionKind Kind => ExtensionKind.Request;

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public IReadOnlyList<string> Capabilities => ["maskit.detect", "maskit.mask"];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var sanitized = engine.Sanitize(payload, out var replacements);
            store.RecordSanitized(replacements);
            return ValueTask.FromResult(replacements > 0
                ? PipelineResult.Modify(sanitized, "敏感信息已按 maskit 规则脱敏。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
            // fail closed：脱敏失败不放行可能含敏感信息的原始数据。
            return ValueTask.FromResult(PipelineResult.Block("maskit 脱敏失败，已阻止原始数据。"));
        }
    }
}

/// <summary>Router 响应扩展点：只还原本进程签发的占位符，未知占位符保持原样。</summary>
public sealed class MaskitResponseExtension(MaskitEngine engine, MaskitRuleStore store) : IResponseExtension
{
    public string ExtensionId => "maskit.response";

    public ExtensionKind Kind => ExtensionKind.Response;

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public IReadOnlyList<string> Capabilities => ["maskit.restore"];

    public ValueTask<PipelineResult> ProcessResponseAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var restored = engine.Restore(payload, out var replacements);
            store.RecordRestored(replacements);
            return ValueTask.FromResult(replacements > 0
                ? PipelineResult.Modify(restored, "占位符已还原。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
            return ValueTask.FromResult(PipelineResult.Block("maskit 还原失败，已阻止未处理响应。"));
        }
    }
}
