using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class CredentialProtectionBridgeFailureTests : IDisposable
{
    private readonly CredentialTestFactory factory = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "loomx-protection-failure-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task 加密能力不可用时请求阻断且事务不落明文()
    {
        var storage = factory.CreateStorage(directory);
        await using var store = new CredentialTokenStore(storage, PluginBridgeDefaults.Unavailable.Protection);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        const string canary = "sk-protection-failure-abcdefghijkl0123456789";
        var result = await new CredentialRequestExtension(engine).ProcessRequestAsync(new PipelineContext("request"), canary, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
        Assert.DoesNotContain(canary, result.Diagnostic ?? "");
        await using var db = await storage.OpenSqliteAsync("credential-tokens.db");
        var row = await db.QuerySingleAsync(new("SELECT COUNT(*) FROM CredentialTokens;"));
        Assert.Equal(0, row![0].Integer);
        // 加密失败事务已经释放，后续真实保护能够在同一原库正常写入。
        await using var recovered = new CredentialTokenStore(storage, CredentialTestFactory.CreateProtection());
        var token = await recovered.GetOrCreateTokenAsync(canary, () => "{{LOOMX_CREDENTIAL_AAAAAAAAAAAAAAAAAAAA}}");
        Assert.Equal(canary, (await recovered.ResolveTokensAsync([token]))[token]);
    }

    [Fact]
    public async Task 解密能力不可用时历史响应阻断且原映射保留()
    {
        var storage = factory.CreateStorage(directory);
        const string original = "sk-history-protection-abcdefghijkl0123456789";
        string token;
        await using (var first = new CredentialTokenStore(storage, CredentialTestFactory.CreateProtection()))
            token = await first.GetOrCreateTokenAsync(original, () => "{{LOOMX_CREDENTIAL_BBBBBBBBBBBBBBBBBBBB}}");
        await using var unavailable = new CredentialTokenStore(storage, PluginBridgeDefaults.Unavailable.Protection);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), unavailable);
        var result = await new CredentialResponseExtension(engine).ProcessResponseAsync(new PipelineContext("response"), token, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
        await using var restored = new CredentialTokenStore(storage, CredentialTestFactory.CreateProtection());
        Assert.Equal(original, (await restored.ResolveTokensAsync([token]))[token]);
        Assert.Equal(new[] { "credential-tokens.db" }, Directory.GetFiles(directory, "*.db").Select(Path.GetFileName));
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}