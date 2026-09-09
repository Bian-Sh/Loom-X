using Xunit;
using System.Text.Json.Nodes;
using LoomX.Assistant.Browser;

namespace LoomX.Tests.Assistant;

public sealed class BrowserSecretHarvesterTests
{
    [Fact]
    public void Vault_StoreAndResolve_RoundTrip()
    {
        var vault = new BrowserSecretVault();

        var reference = vault.Store("sk-livekey0123456789abcdef", "Authorization");

        Assert.StartsWith("secret://browser/", reference);
        Assert.EndsWith("/authorization", reference);
        Assert.True(vault.TryResolve(reference, out var resolved));
        Assert.Equal("sk-livekey0123456789abcdef", resolved);
        Assert.False(vault.TryResolve("secret://browser/nonexistent/x", out _));
    }

    [Fact]
    public void Harvest_AuthorizationHeader_ReplacedWithSecretRef()
    {
        var vault = new BrowserSecretVault();
        var result = JsonNode.Parse("""
            {"requests":[{"url":"https://relay.example.com/v1/models","method":"GET",
              "request_headers":{"Authorization":"Bearer sk-livekey0123456789abcdef","Content-Type":"application/json"}}]}
            """)!;

        var harvested = BrowserSecretHarvester.Harvest(result, vault);
        var json = harvested.ToJsonString();

        Assert.DoesNotContain("sk-livekey0123456789abcdef", json);
        Assert.Contains("secret://browser/", json);
        Assert.Contains("application/json", json);

        var authorization = harvested["requests"]![0]!["request_headers"]!["Authorization"]!.AsObject();
        Assert.True(authorization["configured"]!.GetValue<bool>());
        var reference = authorization["secret_ref"]!.GetValue<string>();
        Assert.True(vault.TryResolve(reference, out var resolved));
        Assert.Equal("sk-livekey0123456789abcdef", resolved);
    }

    [Fact]
    public void Harvest_ApiKeyField_ReplacedWithSecretRef()
    {
        var vault = new BrowserSecretVault();
        var result = JsonNode.Parse("""
            {"content":"令牌列表","api_key":"sk-proj-abc123def456ghi789jkl0","note":"这是页面文本"}
            """)!;

        var harvested = BrowserSecretHarvester.Harvest(result, vault);
        var json = harvested.ToJsonString();

        Assert.DoesNotContain("sk-proj-abc123def456ghi789jkl0", json);
        Assert.Contains("secret://browser/", json);
        Assert.Equal("这是页面文本", harvested["note"]!.GetValue<string>());
    }

    [Fact]
    public void Harvest_BenignValues_Untouched()
    {
        var vault = new BrowserSecretVault();
        var result = JsonNode.Parse("""
            {"models":[{"id":"gpt-4o","owned_by":"openai"}],"count":1}
            """)!;

        var harvested = BrowserSecretHarvester.Harvest(result, vault);

        Assert.Equal(result.ToJsonString(), harvested.ToJsonString());
    }

    [Fact]
    public void Harvest_ShortOrSpacedValues_NotTreatedAsSecret()
    {
        var vault = new BrowserSecretVault();
        var result = JsonNode.Parse("""
            {"authorization":"无","api_key":"待创建","access_token":"hello world"}
            """)!;

        var harvested = BrowserSecretHarvester.Harvest(result, vault);

        Assert.Equal(result.ToJsonString(), harvested.ToJsonString());
    }

    [Fact]
    public void Harvest_DeeplyNestedSecrets_AllRedacted()
    {
        var vault = new BrowserSecretVault();
        var result = JsonNode.Parse("""
            {"data":[{"tokens":[{"api-key":"sk-aaaabbbbccccdddd11112222"}]},{"x-api-key":"sk-eeeeffff0000111122223333"}]}
            """)!;

        var harvested = BrowserSecretHarvester.Harvest(result, vault);
        var json = harvested.ToJsonString();

        Assert.DoesNotContain("sk-aaaabbbbccccdddd11112222", json);
        Assert.DoesNotContain("sk-eeeeffff0000111122223333", json);
    }
}
