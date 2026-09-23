using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>
/// 第一方 Credential Protection Router 插件：在请求正文离开本地安全边界、
/// 发送给外部 Provider 前完成凭据检测与脱敏。规则数据 Plugin-owned，脱敏失败 fail closed。
/// </summary>
public sealed class CredentialProtectionPlugin : ILoomXPlugin
{
    public const string PluginId = "loomx.credential-protection";

    private SensitiveRuleStore? ruleStore;

    public string Id => PluginId;

    /// <summary>规则存储（宿主 Initialize 后可用；测试也可直接注入）。</summary>
    public SensitiveRuleStore RuleStore =>
        ruleStore ?? throw new InvalidOperationException("插件尚未初始化。");

    public void Initialize(PluginInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ruleStore = new SensitiveRuleStore(context.DataDirectory);
    }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        // 宿主保证 Initialize 先于 CreateExtensions 调用；防御性兜底避免空引用。
        var store = ruleStore ?? new SensitiveRuleStore(
            Path.Combine(Path.GetTempPath(), PluginId));
        var engine = new CredentialEngine(store);
        yield return new CredentialRequestExtension(engine);
        yield return new CredentialResponseExtension(engine);
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
public sealed class CredentialRequestExtension(CredentialEngine engine) : IRequestExtension
{
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
            var sanitized = engine.Sanitize(payload, out var sanitizedChanged);
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
            var changed = sanitizedChanged || instructionChanged;
            return ValueTask.FromResult(changed
                ? PipelineResult.Modify(transformed, "敏感数据已脱敏并应用凭据引用完整性指令。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
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
public sealed class CredentialResponseExtension(CredentialEngine engine) : IResponseExtension
{
    public string ExtensionId => "credential.response";

    public ExtensionKind Kind => ExtensionKind.Response;

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public IReadOnlyList<string> Capabilities => ["credential.restore"];

    public ValueTask<PipelineResult> ProcessResponseAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var restored = engine.RestoreTransportPayload(payload, out var changed);
            return ValueTask.FromResult(changed
                ? PipelineResult.Modify(restored, "本地占位符已恢复。")
                : PipelineResult.Pass(payload));
        }
        catch (Exception)
        {
            return ValueTask.FromResult(PipelineResult.Block("凭据恢复处理失败，已阻止未处理响应。"));
        }
    }
}
