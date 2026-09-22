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
    public void Mask_ReplacesJsonFieldValues_WithRestorablePlaceholder()
    {
        var engine = CreateEngine();
        const string secret = "sk-abcdefghij0123456789abcd";

        var sanitized = engine.Sanitize(
            $$"""{"tool":"read_config","api_key":"{{secret}}","note":"前缀 {{secret}} 后缀","status":200}""",
            out var changed);

        Assert.True(changed);
        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, sanitized, StringComparison.Ordinal);
        Assert.Contains("\"status\":200", sanitized, StringComparison.Ordinal); // 非敏感字段保留

        var restored = engine.RestoreTransportPayload(sanitized, out var restoredChanged);
        Assert.True(restoredChanged);
        Assert.Contains(secret, restored, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialEngine.PlaceholderPrefix, restored, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreTransportPayload_PreservesJson_WhenSecretContainsEscapes()
    {
        var engine = CreateEngine();
        const string secret = "line1\n\"quoted\"\\tail";
        var sanitized = engine.Sanitize(
            $$"""{"api_key":{{System.Text.Json.JsonSerializer.Serialize(secret)}}}""",
            out var masked);

        var restored = engine.RestoreTransportPayload(sanitized, out var changed);

        Assert.True(masked);
        Assert.True(changed);
        using var document = System.Text.Json.JsonDocument.Parse(restored);
        Assert.Equal(secret, document.RootElement.GetProperty("api_key").GetString());
    }

    [Fact]
    public void Restore_LeavesUnknownOrForgedPlaceholderUnchanged()
    {
        var engine = CreateEngine();
        const string forged = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";

        var restored = engine.Restore(forged, out var changed);

        Assert.False(changed);
        Assert.Equal(forged, restored);
    }

    [Fact]
    public void Mask_SameSecret_ReusesPlaceholderWithinEngineLifetime()
    {
        var engine = CreateEngine();
        const string secret = "sk-abcdefghij0123456789abcd";

        var first = engine.Sanitize(secret, out _);
        var second = engine.Sanitize(secret, out _);

        Assert.Equal(first, second);
        Assert.StartsWith(CredentialEngine.PlaceholderPrefix, first, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreTransportPayload_PreservesEmbeddedToolArgumentsJson()
    {
        var engine = CreateEngine();
        const string secret = "line1\n\"quoted\"\\tail";
        var sanitizedSource = engine.Sanitize(
            System.Text.Json.JsonSerializer.Serialize(new { api_key = secret }),
            out _);
        using var sanitizedDocument = System.Text.Json.JsonDocument.Parse(sanitizedSource);
        var token = sanitizedDocument.RootElement.GetProperty("api_key").GetString()!;
        var arguments = System.Text.Json.JsonSerializer.Serialize(new { api_key = token });
        var response = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { tool_calls = new[] { new { function = new { arguments } } } } },
            },
        });

        var restored = engine.RestoreTransportPayload(response, out var changed);

        Assert.True(changed);
        using var outer = System.Text.Json.JsonDocument.Parse(restored);
        var restoredArguments = outer.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("tool_calls")[0]
            .GetProperty("function").GetProperty("arguments").GetString();
        using var inner = System.Text.Json.JsonDocument.Parse(restoredArguments!);
        Assert.Equal(secret, inner.RootElement.GetProperty("api_key").GetString());
    }

    [Fact]
    public void TokenMapping_PersistsAcrossEngineRestart_WithoutPlaintextInDatabase()
    {
        Directory.CreateDirectory(dataDirectory);
        const string secret = "sk-persistent-abcdefghij0123456789";
        var firstEngine = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var token = firstEngine.Sanitize(secret, out var changed);

        var restartedEngine = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var restored = restartedEngine.Restore(token, out var restoredChanged);

        Assert.True(changed);
        Assert.True(restoredChanged);
        Assert.Equal(secret, restored);
        var databaseBytes = File.ReadAllBytes(Path.Combine(dataDirectory, "credential-tokens.db"));
        Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(databaseBytes), StringComparison.Ordinal);
    }

    [Fact]
    public void TokenMapping_SameSecretAcrossEngineRestart_ReusesStableToken()
    {
        Directory.CreateDirectory(dataDirectory);
        const string secret = "sk-stable-abcdefghij0123456789";
        var first = new CredentialEngine(new SensitiveRuleStore(dataDirectory)).Sanitize(secret, out _);
        var second = new CredentialEngine(new SensitiveRuleStore(dataDirectory)).Sanitize(secret, out _);

        Assert.Equal(first, second);
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
        var extension = new CredentialRequestExtension(new ThrowingEngine(dataDirectory));
        const string payload = "raw sk-abcdefghij0123456789abcd";

        var result = await extension.ProcessRequestAsync(
            new PipelineContext("request"), payload, CancellationToken.None);

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
