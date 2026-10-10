using System.Text.Json;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 停用语义：停用只关闭主动保护（不再识别与 token 化新明文），
/// 但历史会话中已签发的 placeholder 仍必须注入完整性指令并完成恢复。
/// </summary>
public sealed class CredentialProtectionDisableTests : IDisposable
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-credential-disable-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果。
        }
    }

    private (CredentialProtectionPlugin Plugin, CredentialEngine Engine, IPipelineExtension[] Extensions) CreatePlugin()
    {
        Directory.CreateDirectory(dataDirectory);
        var plugin = new CredentialProtectionPlugin();
        plugin.Initialize(new PluginInitializationContext("loomx.credential-protection", dataDirectory));
        // CreateExtensions 是迭代器：必须实际枚举，引擎才会被创建。
        var extensions = plugin.CreateExtensions().ToArray();
        return (plugin, plugin.Engine ?? throw new InvalidOperationException("引擎未创建。"), extensions);
    }

    [Fact]
    public void Plugin_ImplementsEnableStateAware()
    {
        Assert.IsAssignableFrom<IPluginEnableStateAware>(new CredentialProtectionPlugin());
    }

    [Fact]
    public void Disabled_StopsSanitizingNewPlaintext()
    {
        var (plugin, engine, _) = CreatePlugin();

        plugin.OnPluginEnabledChanged(false);

        const string payload = """{"api_key":"sk-live-abcdefghijklmnop1234"}""";
        var sanitized = engine.Sanitize(payload, out var changed);

        Assert.False(changed);
        Assert.Equal(payload, sanitized);
    }

    [Fact]
    public void Disabled_StillRestoresHistoricalPlaceholder()
    {
        var (plugin, engine, _) = CreatePlugin();
        const string secret = "sk-historical-secret-value-123456";

        // 停用前签发 placeholder，模拟历史会话。
        var sanitized = engine.Sanitize($$"""{"api_key":"{{secret}}"}""", out var changed);
        Assert.True(changed);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, sanitized, StringComparison.Ordinal);

        plugin.OnPluginEnabledChanged(false);

        var restored = engine.RestoreTransportPayload(sanitized, out var restoredChanged);

        Assert.True(restoredChanged);
        Assert.Contains(secret, restored, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialEngine.PlaceholderPrefix, restored, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_StillInjectsIntegrityInstructionForExistingPlaceholder()
    {
        var (plugin, engine, extensions) = CreatePlugin();
        var request = extensions.OfType<CredentialRequestExtension>().Single();
        const string secret = "sk-another-historical-secret-987654";

        var sanitized = engine.Sanitize($$"""{"messages":[{"role":"user","content":"{{secret}}"}]}""", out _);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, sanitized, StringComparison.Ordinal);

        plugin.OnPluginEnabledChanged(false);

        // 停用后请求侧仍要识别既有placeholder 并注入完整性指令。
        var result = request.ProcessRequestAsync(
            new PipelineContext("request", "loomx.credential-protection", "credential.request"),
            sanitized,
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotEqual(PipelineOutcome.Blocked, result.Outcome);
        Assert.Equal(PipelineOutcome.Modified, result.Outcome);

        // JSON 会转义指令中的换行，按结构解析后再比对内容。
        using var document = JsonDocument.Parse(result.Payload);
        var injected = document.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .Any(message =>
                message.GetProperty("content").GetString() is { } content
                && content.Contains(CredentialEngine.IntegrityInstruction, StringComparison.Ordinal));
        Assert.True(injected);
    }

    [Fact]
    public void Disabled_ResponseExtensionStillRestores()
    {
        Directory.CreateDirectory(dataDirectory);
        var plugin = new CredentialProtectionPlugin();
        plugin.Initialize(new PluginInitializationContext("loomx.credential-protection", dataDirectory));
        var extensions = plugin.CreateExtensions().ToArray();
        var engine = plugin.Engine!;
        var response = extensions.OfType<CredentialResponseExtension>().Single();

        const string secret = "sk-response-path-secret-55555";
        var sanitized = engine.Sanitize($$"""{"api_key":"{{secret}}"}""", out _);

        plugin.OnPluginEnabledChanged(false);

        var result = response.ProcessResponseAsync(
            new PipelineContext("response", "loomx.credential-protection", "credential.response"),
            sanitized,
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotEqual(PipelineOutcome.Blocked, result.Outcome);
        Assert.Contains(secret, result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Reenabled_ResumesSanitizing()
    {
        var (plugin, engine, _) = CreatePlugin();

        plugin.OnPluginEnabledChanged(false);
        var payload = """{"api_key":"sk-live-abcdefghijklmnop1234"}""";
        engine.Sanitize(payload, out var disabledChanged, out _);
        Assert.False(disabledChanged);

        plugin.OnPluginEnabledChanged(true);
        engine.Sanitize(payload, out var reenabledChanged, out _);
        Assert.True(reenabledChanged);
    }

    [Fact]
    public void Disable_DoesNotTouchVaultSoHistorySurvivesRestart()
    {
        var (plugin, engine, _) = CreatePlugin();
        const string secret = "sk-persist-across-restart-777";

        var sanitized = engine.Sanitize($$"""{"api_key":"{{secret}}"}""", out _);
        plugin.OnPluginEnabledChanged(false);

        // 模拟重启：新建store 与引擎，历史 placeholder 仍可恢复。
        var reloaded = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var restored = reloaded.RestoreTransportPayload(sanitized, out var changed);

        Assert.True(changed);
        Assert.Contains(secret, restored, StringComparison.Ordinal);
    }
}