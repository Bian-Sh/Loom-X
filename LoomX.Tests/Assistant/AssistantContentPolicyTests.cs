using LoomX.Assistant.Configuration;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class AssistantContentPolicyTests
{
    [Theory]
    [InlineData("api_key")]
    [InlineData("Authorization")]
    [InlineData("refresh-token")]
    [InlineData("database_password")]
    [InlineData("client-secret")]
    [InlineData("service_credential")]
    public void AssistantContentPolicy_识别敏感片段(string segment)
    {
        Assert.True(AssistantContentPolicy.IsSensitivePath(["provider", segment]));
    }

    [Theory]
    [InlineData("monkey")]
    [InlineData("tokenizer")]
    [InlineData("secretary")]
    [InlineData("password_policy")]
    public void AssistantContentPolicy_不误判普通片段(string segment)
    {
        Assert.False(AssistantContentPolicy.IsSensitivePath(["provider", segment]));
    }

    [Theory]
    [InlineData("accessToken")]
    [InlineData("api-keys")]
    [InlineData("headers[refresh_tokens]")]
    [InlineData("clientSecrets")]
    [InlineData("serviceKeys")]
    [InlineData("provider.credentials.value")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature")]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuvwxyz123456")]
    public void AssistantContentPolicy_识别自由文本中的敏感名称和值(string content)
    {
        Assert.True(AssistantContentPolicy.ContainsSensitiveContent(content));
    }

    [Theory]
    [InlineData("请选择运行模式")]
    [InlineData("tokenizer 模型设置")]
    [InlineData("password_policy 保持默认")]
    public void AssistantContentPolicy_不误判普通自由文本(string content)
    {
        Assert.False(AssistantContentPolicy.ContainsSensitiveContent(content));
    }

    [Fact]
    public void AssistantContentPolicy_敏感值固定替换为三个星号()
    {
        var original = TomlValue.FromObject("sk-very-long-secret-value");

        var redacted = AssistantContentPolicy.Redact(original, ["providers", "api-key"]);

        Assert.Equal(TomlValueKind.String, redacted.Kind);
        Assert.Equal("***", redacted.Value);
        Assert.DoesNotContain("secret", (string)redacted.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssistantContentPolicy_递归脱敏对象中的敏感键()
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

        var redacted = AssistantContentPolicy.Redact(original, ["provider"]);
        var root = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(redacted.Value);
        Assert.Equal("loomx", root["model"].Value);
        Assert.Equal("***", root["api_key"].Value);
        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(root["nested"].Value);
        Assert.Equal("***", nested["refresh-token"].Value);
        Assert.Equal(true, nested["enabled"].Value);
    }


    [Theory]
    [InlineData("headers")]
    [InlineData("custom_headers")]
    [InlineData("http_headers")]
    public void AssistantContentPolicy_Header容器的所有后代都视为敏感(string container)
    {
        Assert.True(AssistantContentPolicy.IsSensitivePath(["provider", container, "X-Arbitrary-Name"]));
    }

    [Theory]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.signature123")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuvwxyz123456")]
    public void AssistantContentPolicy_非敏感键名下的秘密字符串也脱敏(string secret)
    {
        var original = TomlValue.FromObject(new Dictionary<string, object?>
        {
            ["benign"] = secret,
            ["nested"] = new object?[]
            {
                new Dictionary<string, object?> { ["value"] = secret },
            },
        });

        var redacted = AssistantContentPolicy.Redact(original, ["provider"]);
        var root = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(redacted.Value);
        Assert.Equal("***", root["benign"].Value);
        var array = Assert.IsAssignableFrom<IReadOnlyList<TomlValue>>(root["nested"].Value);
        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(array[0].Value);
        Assert.Equal("***", nested["value"].Value);
    }

    [Fact]
    public void AssistantContentPolicy_Header父容器读取隐藏任意Header值但保留普通值()
    {
        var original = TomlValue.FromObject(new Dictionary<string, object?>
        {
            ["headers"] = new Dictionary<string, object?>
            {
                ["X-Custom"] = "private-header-value",
                ["X-Trace"] = "trace-value",
            },
            ["model"] = "loomx",
        });

        var redacted = AssistantContentPolicy.Redact(original, ["provider"]);
        var root = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(redacted.Value);
        var headers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(root["headers"].Value);
        Assert.All(headers.Values, value => Assert.Equal("***", value.Value));
        Assert.Equal("loomx", root["model"].Value);
    }

    [Fact]
    public void AssistantContentPolicy_非敏感标量保持原实例()
    {
        var original = TomlValue.FromObject("loomx");

        var redacted = AssistantContentPolicy.Redact(original, ["provider", "model"]);

        Assert.Same(original, redacted);
    }
}
