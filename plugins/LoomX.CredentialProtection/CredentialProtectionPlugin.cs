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
    }
}

/// <summary>凭据保护 Extension 基类：脱敏失败时 fail closed，不放行原始数据。</summary>
public abstract class CredentialExtensionBase(CredentialEngine engine) : IPipelineExtension
{
    private readonly CredentialEngine engine = engine;

    public abstract string ExtensionId { get; }

    public abstract ExtensionKind Kind { get; }

    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;

    public IReadOnlyList<string> Capabilities => ["credential.detect", "credential.mask"];

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
public sealed class CredentialRequestExtension(CredentialEngine engine)
    : CredentialExtensionBase(engine), IRequestExtension
{
    public override string ExtensionId => "credential.request";

    public override ExtensionKind Kind => ExtensionKind.Request;

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        Process(payload);
}
