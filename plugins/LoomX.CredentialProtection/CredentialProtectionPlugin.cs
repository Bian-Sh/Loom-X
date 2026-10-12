using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>
/// 第一方 Credential Protection Router 插件：在请求正文离开本地安全边界、
/// 发送给外部 Provider 前完成凭据检测与脱敏。规则数据 Plugin-owned，脱敏失败 fail closed。
/// </summary>
public sealed partial class CredentialProtectionPlugin : ILoomXPlugin, IPluginUiContributionProvider, IPluginUiCommandHandler, IPluginEnableStateAware, IAsyncDisposable
{
    public const string PluginId = "loomx.credential-protection";

    private readonly CredentialProtectionObservability observability = new();
    private SensitiveRuleStore? ruleStore;
    private CredentialTokenStore? tokenStore;

    public string Id => PluginId;

    public CredentialProtectionObservability Observability => observability;

    /// <summary>停用后仍保持可用的历史引用解析能力，不随停用关闭。</summary>
    public bool ActiveProtectionEnabled { get; private set; } = true;

    public event EventHandler? UiInvalidated
    {
        add => observability.Changed += value;
        remove => observability.Changed -= value;
    }

    /// <summary>规则存储（宿主 Initialize 后可用；测试也可直接注入）。</summary>
    public SensitiveRuleStore RuleStore =>
        ruleStore ?? throw new InvalidOperationException("插件尚未初始化。");

    public void Initialize(PluginInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (tokenStore is not null) throw new InvalidOperationException("插件不得重复初始化。");
        tokenStore = new CredentialTokenStore(context.Bridge.Storage, context.Bridge.Protection);
        CredentialSynchronous.Run<bool>(async () =>
        {
            await tokenStore.InitializeAsync().ConfigureAwait(false);
            return true;
        });
        ruleStore = new SensitiveRuleStore(context.DataDirectory);
        // 规则变更让宿主重新拉取声明式 UI 快照。
        ruleStore.Changed += (_, _) => observability.RaiseChanged();
    }

    /// <summary>
    /// 语义停用：停止识别与 token 化新明文，但保留 placeholder 恢复与完整性指令注入，
    /// 否则历史会话中已签发的凭据引用会永久失效。
    /// </summary>
    public void OnPluginEnabledChanged(bool enabled)
    {
        ActiveProtectionEnabled = enabled;
        if (Engine is not null) Engine.ActiveProtectionEnabled = enabled;
        observability.RecordProtectionToggled(enabled);
        observability.RaiseChanged();
    }

    private CredentialEngine? engine;

    /// <summary>当前处于编辑态的规则 id；非编辑态为 null。</summary>
    private string? editingRuleId;

    /// <summary>脱敏引擎（CreateExtensions 后可用）。</summary>
    public CredentialEngine? Engine => engine;

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = observability.Snapshot();
        var text = CredentialProtectionUiText.Resolve(context.CultureName);
        var settingsText = CredentialSettingsText.Resolve(context.CultureName);
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(context.CultureName); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }

        var contributions = new List<PluginUiContribution>
        {
            new(
                PluginUiContribution.CurrentSchemaVersion,
                CredentialSettingsUi.OverviewContributionId,
                PluginUiSlot.CardBody,
                new PluginUiGridNode(
                    3,
                    [
                        new PluginUiGridItem(BuildMetricCard(
                            text.SanitizedRequests,
                            $"{snapshot.SanitizedRequests.ToString("N0", culture)}/{snapshot.TotalRequests.ToString("N0", culture)}",
                            string.Format(culture, text.SanitizedTerms, snapshot.SanitizedTerms),
                            PluginUiTone.Success,
                            "M 16,3 L 27,8 L 27,16 C 27,23 22,28 16,30 C 10,28 5,23 5,16 L 5,8 Z M 11,16 L 15,20 L 22,12")),
                        new PluginUiGridItem(BuildMetricCard(
                            text.RestoredResponses,
                            snapshot.RestoredResponses.ToString("N0", culture),
                            text.RestoredHint,
                            PluginUiTone.Accent,
                            "M 16,3 L 27,8 L 27,16 C 27,23 22,28 16,30 C 10,28 5,23 5,16 L 5,8 Z M 11,16 L 15,20 L 22,12")),
                        new PluginUiGridItem(BuildMetricCard(
                            text.Errors,
                            snapshot.Errors.ToString("N0", culture),
                            text.ErrorsHint,
                            PluginUiTone.Warning,
                            "M 16,4 A 12,12 0 1 1 16,28 A 12,12 0 1 1 16,4 M 16,10 L 16,18 M 16,23 L 16,23")),
                    ],
                    ColumnSpacing: 12)),
        };

        if (ruleStore is not null)
        {
            contributions.Add(new PluginUiContribution(
                PluginUiContribution.CurrentSchemaVersion,
                CredentialSettingsUi.SettingsContributionId,
                PluginUiSlot.DetailBody,
                CredentialSettingsUi.BuildSettings(
                    ruleStore,
                    context.CultureName,
                    editingRuleId,
                    ActiveProtectionEnabled)));
        }

        return contributions;
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

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        // 必须使用初始化时授予的 Bridge，禁止兜底创建另一份 Vault。
        var created = new CredentialEngine(RuleStore, tokenStore
            ?? throw new InvalidOperationException("插件尚未初始化。")) { ActiveProtectionEnabled = ActiveProtectionEnabled };
        engine = created;
        yield return new CredentialRequestExtension(created, observability);
        yield return new CredentialResponseExtension(created, observability);
    }

    public ValueTask DisposeAsync() => tokenStore?.DisposeAsync() ?? ValueTask.CompletedTask;

    public async ValueTask<PluginUiCommandResult> ExecuteUiCommandAsync(
        PluginUiCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var store = ruleStore;
        if (store is null)
            return PluginUiCommandResult.Failure("PLUGIN_NOT_INITIALIZED");

        // 编辑态是纯 UI 关注点：只切换状态并刷新快照，不改动规则数据。
        if (command.CommandId == CredentialSettingsUi.BeginEdit)
        {
            editingRuleId = command.GetArgument("rule_id");
            observability.RaiseChanged();
            return PluginUiCommandResult.Success();
        }

        if (command.CommandId == CredentialSettingsUi.CancelEdit)
        {
            editingRuleId = null;
            observability.RaiseChanged();
            return PluginUiCommandResult.Success();
        }

        var result = await CredentialSettingsCommands
            .Execute(store, command, CultureInfo.CurrentUICulture.Name)
            .ConfigureAwait(false);

        if (result.Handled) editingRuleId = null;
        return result;
    }
}

/// <summary>凭据保护 Extension 基类：脱敏失败时 fail closed，不放行原始数据。</summary>
public abstract class CredentialExtensionBase(CredentialEngine engine) : IPipelineExtension
{
    private readonly CredentialEngine engine = engine;

    public abstract string ExtensionId { get; }

    public abstract ExtensionKind Kind { get; }

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public virtual IReadOnlyList<string> Capabilities => ["credential.detect", "credential.mask"];

    protected ValueTask<PipelineResult> Process(string payload)
    {
        try
        {
            var sanitized = engine.Sanitize(payload, out var changed);
            return ValueTask.FromResult(changed
                ? PipelineResult.Modify(sanitized, "敏感数据已脱敏。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
            // fail closed：处理失败阻止原始数据，返回安全失败结果。
            return ValueTask.FromResult(PipelineResult.Block("凭据保护处理失败，已阻止原始数据。"));
        }
    }
}

/// <summary>Router 请求扩展点：请求正文发送给外部 Provider 前脱敏。</summary>
public sealed class CredentialRequestExtension(
    CredentialEngine engine,
    CredentialProtectionObservability? observability = null) : IRequestExtension
{
    private readonly CredentialProtectionObservability observability = observability ?? new();
    public string ExtensionId => "credential.request";

    public ExtensionKind Kind => ExtensionKind.Request;

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public IReadOnlyList<string> Capabilities =>
        ["credential.detect", "credential.mask", "credential.integrity-instruction"];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var (sanitized, sanitizedChanged, replacementCount) = engine.WithCancellation(cancellationToken, () =>
            {
                var value = engine.Sanitize(payload, out var changed, out var count);
                return (value, changed, count);
            });
            var transformed = sanitized;
            var instructionChanged = false;
            if (CredentialEngine.ContainsPlaceholderCandidate(sanitized))
            {
                transformed = CredentialIntegrityInstructionInjector.Inject(
                    sanitized,
                    context.Metadata is not null
                        && context.Metadata.TryGetValue("api_mode", out var apiMode)
                            ? apiMode
                            : null,
                    out instructionChanged);
            }
            observability.RecordRequest(replacementCount);
            var changed = sanitizedChanged || instructionChanged;
            return ValueTask.FromResult(changed
                ? PipelineResult.Modify(transformed, "敏感数据已脱敏并应用凭据引用完整性指令。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
            observability.RecordRequest(0);
            observability.RecordError();
            return ValueTask.FromResult(PipelineResult.Block("凭据保护处理失败，已阻止原始数据。"));
        }
    }
}

internal static class CredentialIntegrityInstructionInjector
{
    public static string Inject(string payload, string? apiMode, out bool changed)
    {
        changed = false;
        JsonNode? root;
        try { root = JsonNode.Parse(payload); }
        catch (JsonException) { return payload; }
        if (root is not JsonObject jsonObject) return payload;

        var mode = apiMode?.Trim().ToLowerInvariant();
        if (mode == "anthropic" || jsonObject["system"] is not null && jsonObject["messages"] is JsonArray)
        {
            changed = InjectAnthropicSystem(jsonObject);
        }
        else if (jsonObject["messages"] is JsonArray messages)
        {
            changed = InjectOpenAiMessages(messages);
        }
        else if (jsonObject["contents"] is JsonArray)
        {
            changed = InjectGeminiSystemInstruction(jsonObject);
        }
        else if (jsonObject["prompt"] is JsonValue promptValue
                 && promptValue.TryGetValue<string>(out var prompt))
        {
            if (prompt.Contains(CredentialEngine.IntegrityInstruction, StringComparison.Ordinal)) return payload;
            jsonObject["prompt"] = CredentialEngine.IntegrityInstruction + "\n\n" + prompt;
            changed = true;
        }

        return changed ? jsonObject.ToJsonString() : payload;
    }

    private static bool InjectOpenAiMessages(JsonArray messages)
    {
        foreach (var item in messages)
        {
            if (item is not JsonObject message) continue;
            var role = message["role"]?.GetValue<string>();
            if (role is not ("system" or "developer")) continue;
            if (TryAppendContent(message, CredentialEngine.IntegrityInstruction)) return true;
            if (ContainsInstruction(message["content"])) return false;
        }

        messages.Insert(0, new JsonObject
        {
            ["role"] = "system",
            ["content"] = CredentialEngine.IntegrityInstruction,
        });
        return true;
    }

    private static bool InjectAnthropicSystem(JsonObject root)
    {
        var system = root["system"];
        if (system is null)
        {
            root["system"] = CredentialEngine.IntegrityInstruction;
            return true;
        }
        if (ContainsInstruction(system)) return false;
        if (system is JsonValue value && value.TryGetValue<string>(out var text))
        {
            root["system"] = text + "\n\n" + CredentialEngine.IntegrityInstruction;
            return true;
        }
        if (system is JsonArray array)
        {
            array.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = CredentialEngine.IntegrityInstruction,
            });
            return true;
        }
        return false;
    }

    private static bool InjectGeminiSystemInstruction(JsonObject root)
    {
        var systemInstruction = root["systemInstruction"] as JsonObject;
        if (systemInstruction is null)
        {
            root["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = CredentialEngine.IntegrityInstruction }),
            };
            return true;
        }
        if (ContainsInstruction(systemInstruction)) return false;
        var parts = systemInstruction["parts"] as JsonArray ?? new JsonArray();
        systemInstruction["parts"] = parts;
        parts.Add(new JsonObject { ["text"] = CredentialEngine.IntegrityInstruction });
        return true;
    }

    private static bool TryAppendContent(JsonObject message, string instruction)
    {
        if (message["content"] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (text.Contains(instruction, StringComparison.Ordinal)) return false;
            message["content"] = text + "\n\n" + instruction;
            return true;
        }
        if (message["content"] is JsonArray array)
        {
            if (ContainsInstruction(array)) return false;
            array.Add(new JsonObject { ["type"] = "text", ["text"] = instruction });
            return true;
        }
        return false;
    }

    private static bool ContainsInstruction(JsonNode? node) =>
        node?.ToJsonString().Contains(CredentialEngine.IntegrityInstruction, StringComparison.Ordinal) == true;
}

/// <summary>Router 响应扩展点：只恢复本地签发的有效占位符，未知 token 保持原样。</summary>
public sealed class CredentialResponseExtension(
    CredentialEngine engine,
    CredentialProtectionObservability? observability = null) : IResponseExtension
{
    private readonly CredentialProtectionObservability observability = observability ?? new();
    public string ExtensionId => "credential.response";

    public ExtensionKind Kind => ExtensionKind.Response;

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public IReadOnlyList<string> Capabilities => ["credential.restore"];

    public ValueTask<PipelineResult> ProcessResponseAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var (restored, changed) = engine.WithCancellation(cancellationToken, () =>
            {
                var value = engine.RestoreTransportPayload(payload, out var didChange);
                return (value, didChange);
            });
            if (changed) observability.RecordRestoredResponse();
            return ValueTask.FromResult(changed
                ? PipelineResult.Modify(restored, "本地占位符已恢复。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
            observability.RecordError();
            return ValueTask.FromResult(PipelineResult.Block("凭据恢复处理失败，已阻止未处理响应。"));
        }
    }
}

/// <summary>凭据保护卡片概览的多语言文案，来自插件自有资源。</summary>
internal sealed record CredentialProtectionUiText(PluginTextCatalog Catalog)
{
    public static CredentialProtectionUiText Resolve(string cultureName) =>
        new(PluginTextCatalog.For(
            typeof(CredentialProtectionUiText),
            CredentialProtectionPlugin.PluginId,
            cultureName));

    public string SanitizedRequests => Catalog.Get("sanitizedRequests");
    public string SanitizedTerms => Catalog.Get("sanitizedTerms");
    public string RestoredResponses => Catalog.Get("restoredResponses");
    public string RestoredHint => Catalog.Get("restoredHint");
    public string Errors => Catalog.Get("errors");
    public string ErrorsHint => Catalog.Get("errorsHint");
}
