using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// Credential Protection 插件：检测、规则管理、脱敏替换与 fail closed
/// （spec: credential-protection 全部需求）。
/// </summary>
public sealed class CredentialProtectionTests : IDisposable
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-credential-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }

    private CredentialEngine CreateEngine()
    {
        Directory.CreateDirectory(dataDirectory);
        return new CredentialEngine(new SensitiveRuleStore(dataDirectory));
    }

    // ── 敏感数据检测 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("sk-abcdefghij0123456789abcd")]                       // sk- 前缀密钥
    [InlineData("sk-proj-abcdefghij0123456789")]                      // sk-proj- 形态
    [InlineData("ghp_abcdefghij0123456789abcd")]                      // GitHub token
    [InlineData("github_pat_11ABCDEFG0_abcdefghij0123456789")]        // GitHub PAT
    [InlineData("xoxb-1234567890-abcdefghij")]                        // Slack token
    [InlineData("AKIAIOSFODNN7EXAMPLE")]                              // AWS Access Key
    [InlineData("AIzaSyD4iE2xVSp8S3n0vQzYwK1mN2bXc")]                // Google API Key
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJVadQssw5c")] // JWT
    public void DetectsSecretValueShapes(string secret)
    {
        var engine = CreateEngine();

        var hits = engine.Detect($"调用结果：{secret}");

        Assert.NotEmpty(hits);
    }

    [Fact]
    public void DetectsBearerToken()
    {
        var engine = CreateEngine();

        var hits = engine.Detect("Authorization: Bearer abcdef0123456789abcdef0123");

        Assert.NotEmpty(hits);
    }

    [Theory]
    [InlineData("""{"api_key":"some-value"}""")]
    [InlineData("""{"Authorization":"some-value"}""")]
    [InlineData("""{"config":{"access_token":"some-value"}}""")]
    [InlineData("""{"client_secret":"some-value"}""")]
    [InlineData("""{"X-Custom-Headers":{"X-Thing":"some-value"}}""")]
    public void DetectsSensitiveFieldNames(string payload)
    {
        var engine = CreateEngine();

        var hits = engine.Detect(payload);

        Assert.Contains(hits, hit => hit.Kind == "name");
    }

    [Fact]
    public void DetectsCustomHeaderValues()
    {
        var engine = CreateEngine();

        var hits = engine.Detect("""{"custom_headers":{"X-Trace-Id":"trace-123"}}""");

        Assert.Contains(hits, hit => hit.Kind == "name");
    }

    [Theory]
    [InlineData("""{"model":"qwen3:8b","provider":"ollama","status":200}""")]
    [InlineData("""{"message":"生成完成","elapsed_ms":1230}""")]
    [InlineData("模型 qwen3:8b 通过 ollama 返回 200")]
    public void BusinessData_IsNotFlagged(string payload)
    {
        var engine = CreateEngine();

        Assert.Empty(engine.Detect(payload));
        var sanitized = engine.Sanitize(payload, out var changed);
        Assert.False(changed);
        Assert.Equal(payload, sanitized);
    }

    // ── 脱敏替换 ────────────────────────────────────────────────────────

    [Fact]
    public void Mask_ReplacesJsonFieldValues_WithFixedPlaceholder()
    {
        var engine = CreateEngine();
        const string secret = "sk-abcdefghij0123456789abcd";

        var sanitized = engine.Sanitize(
            $$"""{"tool":"read_config","api_key":"{{secret}}","note":"前缀 {{secret}} 后缀","status":200}""",
            out var changed);

        Assert.True(changed);
        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.RedactedPlaceholder, sanitized, StringComparison.Ordinal);
        Assert.Contains("\"status\":200", sanitized, StringComparison.Ordinal); // 非敏感字段保留
    }

    [Fact]
    public void Mask_CustomHeaderContainer_RedactsAllValues()
    {
        var engine = CreateEngine();

        var sanitized = engine.Sanitize(
            """{"custom_headers":{"X-Trace-Id":"trace-abc","X-Env":"prod"},"path":"/health"}""",
            out var changed);

        Assert.True(changed);
        Assert.DoesNotContain("trace-abc", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("prod", sanitized, StringComparison.Ordinal);
        Assert.Contains("/health", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_FreeTextSecret_ReplacedWithoutOriginalFragments()
    {
        var engine = CreateEngine();
        const string secret = "sk-proj-abcdefghij0123456789";

        var sanitized = engine.Sanitize($"读取到的密钥是 {secret} 请妥善保管", out var changed);

        Assert.True(changed);
        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        // 不允许出现原值的任何片段（取中段子串验证）
        Assert.DoesNotContain(secret[4..20], sanitized, StringComparison.Ordinal);
    }

    // ── 规则管理（Plugin-owned） ─────────────────────────────────────────

    [Fact]
    public void CustomRule_TakesEffectForSubsequentData()
    {
        Directory.CreateDirectory(dataDirectory);
        var store = new SensitiveRuleStore(dataDirectory);
        var engine = new CredentialEngine(store);
        const string payload = """{"vault_path":"/data/alpha"}""";

        Assert.Empty(engine.Detect(payload)); // 新增前不命中

        Assert.True(store.AddRule(SensitiveRuleKind.Name, "vault_path", out _));

        var hits = engine.Detect(payload);
        Assert.Contains(hits, hit => hit.Kind == "name");
        var sanitized = engine.Sanitize(payload, out var changed);
        Assert.True(changed);
        Assert.DoesNotContain("/data/alpha", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledRule_StopsDetection_ReenableRestores()
    {
        Directory.CreateDirectory(dataDirectory);
        var store = new SensitiveRuleStore(dataDirectory);
        var engine = new CredentialEngine(store);
        const string payload = "sk-abcdefghij0123456789abcd";

        Assert.True(store.SetRuleEnabled("pattern.openai-sk", false));
        Assert.Empty(engine.Detect(payload));

        Assert.True(store.SetRuleEnabled("pattern.openai-sk", true));
        Assert.NotEmpty(engine.Detect(payload));
    }

    [Fact]
    public void Rules_PersistToPluginOwnedFile_SurviveReload()
    {
        Directory.CreateDirectory(dataDirectory);
        var store = new SensitiveRuleStore(dataDirectory);
        Assert.True(store.AddRule(SensitiveRuleKind.Name, "vault_path", out _));

        // 规则文件只写插件自有数据目录
        Assert.True(File.Exists(Path.Combine(dataDirectory, "rules.json")));

        var reloaded = new SensitiveRuleStore(dataDirectory);
        Assert.Contains(reloaded.Rules, rule => rule.Value == "vault_path" && rule.Enabled);
    }

    [Fact]
    public void RemovedRule_NoLongerApplies()
    {
        Directory.CreateDirectory(dataDirectory);
        var store = new SensitiveRuleStore(dataDirectory);
        var engine = new CredentialEngine(store);
        const string payload = "sk-abcdefghij0123456789abcd";

        Assert.True(store.RemoveRule("pattern.openai-sk"));
        Assert.Empty(engine.Detect(payload));
    }

    // ── fail closed ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessingFailure_BlocksRawData()
    {
        var extension = new CredentialToolResultExtension(new ThrowingEngine(dataDirectory));
        const string payload = "raw sk-abcdefghij0123456789abcd";

        var result = await extension.ProcessToolResultAsync(
            new PipelineContext("tool-result"), payload, CancellationToken.None);

        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
        Assert.Equal(ExtensionFailurePolicy.FailClosed, extension.FailurePolicy);
    }

    /// <summary>处理过程必抛异常的引擎，验证插件侧 fail closed。</summary>
    private sealed class ThrowingEngine : CredentialEngine
    {
        public ThrowingEngine(string dataDirectory)
            : base(new SensitiveRuleStore(dataDirectory))
        {
        }

        public override string Sanitize(string? payload, out bool changed) =>
            throw new InvalidOperationException("模拟处理失败。");
    }
}
