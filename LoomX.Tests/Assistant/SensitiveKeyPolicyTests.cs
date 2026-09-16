using LoomX.Assistant.Configuration;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class SensitiveKeyPolicyTests
{
    [Theory]
    [InlineData("api_key")]
    [InlineData("Authorization")]
    [InlineData("refresh-token")]
    [InlineData("database_password")]
    [InlineData("client-secret")]
    [InlineData("service_credential")]
    public void SensitiveKeyPolicy_识别敏感片段(string segment)
    {
        Assert.True(SensitiveKeyPolicy.IsSensitivePath(["provider", segment]));
    }

    [Theory]
    [InlineData("monkey")]
    [InlineData("tokenizer")]
    [InlineData("secretary")]
    [InlineData("password_policy")]
    public void SensitiveKeyPolicy_不误判普通片段(string segment)
    {
        Assert.False(SensitiveKeyPolicy.IsSensitivePath(["provider", segment]));
    }

    [Fact]
    public void SensitiveKeyPolicy_敏感值固定替换为三个星号()
    {
        var original = TomlValue.FromObject("sk-very-long-secret-value");

        var redacted = SensitiveKeyPolicy.Redact(original, ["providers", "api-key"]);

        Assert.Equal(TomlValueKind.String, redacted.Kind);
        Assert.Equal("***", redacted.Value);
        Assert.DoesNotContain("secret", (string)redacted.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SensitiveKeyPolicy_递归脱敏对象中的敏感键()
    {
        var original = TomlValue.FromObject(new Dictionary<string, object?>
        {
            ["model"] = "loomx",
            ["api_key"] = "sk-sensitive",
            ["nested"] = new Dictionary<string, object?>
            {
                ["refresh-token"] = "refresh-sensitive",
                ["enabled"] = true,
            },
        });

        var redacted = SensitiveKeyPolicy.Redact(original, ["provider"]);
        var root = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(redacted.Value);
        Assert.Equal("loomx", root["model"].Value);
        Assert.Equal("***", root["api_key"].Value);
        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(root["nested"].Value);
        Assert.Equal("***", nested["refresh-token"].Value);
        Assert.Equal(true, nested["enabled"].Value);
    }

    [Fact]
    public void SensitiveKeyPolicy_非敏感标量保持原实例()
    {
        var original = TomlValue.FromObject("loomx");

        var redacted = SensitiveKeyPolicy.Redact(original, ["provider", "model"]);

        Assert.Same(original, redacted);
    }
}
