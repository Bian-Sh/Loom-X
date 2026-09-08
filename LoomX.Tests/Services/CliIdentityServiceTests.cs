using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class CliIdentityServiceTests
{
    [Fact]
    public void BuildCliIdentityHeaders_ClaudeCode_ReturnsFullHeaderSet()
    {
        var headers = CliIdentityService.BuildCliIdentityHeaders(CliIdentityType.ClaudeCode, "2.1.263");

        Assert.Equal("claude-cli/2.1.263 (external, cli)", headers["User-Agent"]);
        Assert.Equal("2023-06-01", headers["anthropic-version"]);
        Assert.Equal("js", headers["x-stainless-lang"]);
        Assert.Equal("Linux", headers["x-stainless-os"]);
        Assert.Equal("x64", headers["x-stainless-arch"]);
        Assert.Equal("node", headers["x-stainless-runtime"]);
        Assert.Equal("2.1.263", headers["x-stainless-package-version"]);
        Assert.Equal("600000", headers["x-stainless-timeout"]);
        Assert.Equal("2", headers["x-stainless-retries"]);
    }

    [Fact]
    public void BuildCliIdentityHeaders_Codex_ReturnsOriginatorAndVersionHeaders()
    {
        var headers = CliIdentityService.BuildCliIdentityHeaders(CliIdentityType.Codex, "0.153.4");

        Assert.Equal("codex-cli/0.153.4", headers["User-Agent"]);
        Assert.Equal("codex-cli", headers["originator"]);
        Assert.Equal("0.153.4", headers["version"]);
    }

    [Fact]
    public void BuildCliIdentityHeaders_Grok_ReturnsXaiClientHeaders()
    {
        var headers = CliIdentityService.BuildCliIdentityHeaders(CliIdentityType.Grok, "1.0.6");

        Assert.Equal("grok-cli/1.0.6 (external, cli)", headers["User-Agent"]);
        Assert.Equal("grok-cli", headers["x-grok-client-identifier"]);
        Assert.Equal("1.0.6", headers["x-grok-client-version"]);
        Assert.Equal("cli", headers["x-grok-client-mode"]);
        Assert.Equal("true", headers["X-XAI-Token-Auth"]);
        Assert.Equal("true", headers["x-authenticateresponse"]);
    }

    [Fact]
    public void ApplyCliIdentity_RemovesOtherFamilyHeaders()
    {
        // 预置三家混杂头
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = "grok-cli/1.0.6 (external, cli)",
            ["x-grok-client-identifier"] = "grok-cli",
            ["x-grok-client-version"] = "1.0.6",
            ["originator"] = "codex-cli",
            ["version"] = "0.153.4",
            ["x-stainless-lang"] = "js",
            ["anthropic-version"] = "2023-06-01",
            // 非家族头（应保留）
            ["X-Custom-Header"] = "keep-me",
            ["Accept"] = "application/json",
        };

        var result = CliIdentityService.ApplyCliIdentity(input, CliIdentityType.ClaudeCode, "2.1.263");

        // 只保留 Claude 家族头 + 非家族头
        Assert.Equal("claude-cli/2.1.263 (external, cli)", result["User-Agent"]);
        Assert.Equal("js", result["x-stainless-lang"]);
        Assert.Equal("2023-06-01", result["anthropic-version"]);

        // 其他家族头被清除
        Assert.False(result.ContainsKey("x-grok-client-identifier"));
        Assert.False(result.ContainsKey("x-grok-client-version"));
        Assert.False(result.ContainsKey("x-grok-client-mode"));
        Assert.False(result.ContainsKey("originator"));
        Assert.False(result.ContainsKey("version"));
        Assert.False(result.ContainsKey("x-authenticateresponse"));

        // 非家族头保留
        Assert.Equal("keep-me", result["X-Custom-Header"]);
        Assert.Equal("application/json", result["Accept"]);

        // 输入字典不被修改
        Assert.Equal("grok-cli/1.0.6 (external, cli)", input["User-Agent"]);
        Assert.True(input.ContainsKey("originator"));
    }

    [Fact]
    public void ApplyCliIdentity_SameFamilyOverwritesVersion()
    {
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = "codex-cli/0.150.0",
            ["originator"] = "codex-cli",
            ["version"] = "0.150.0",
        };

        var result = CliIdentityService.ApplyCliIdentity(input, CliIdentityType.Codex, "0.153.4");

        Assert.Equal("codex-cli/0.153.4", result["User-Agent"]);
        Assert.Equal("0.153.4", result["version"]);
    }

    [Fact]
    public void ApplyCliIdentity_PreservesNonFamilyHeaders()
    {
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer xxx",
            ["Accept"] = "text/event-stream",
            ["Content-Type"] = "application/json",
        };

        var result = CliIdentityService.ApplyCliIdentity(input, CliIdentityType.ClaudeCode, "2.1.263");

        Assert.Equal("Bearer xxx", result["Authorization"]);
        Assert.Equal("text/event-stream", result["Accept"]);
        Assert.Equal("application/json", result["Content-Type"]);
        Assert.Equal("claude-cli/2.1.263 (external, cli)", result["User-Agent"]);
    }

    [Fact]
    public void ApplyCliIdentity_RemovesUnknownStainlessHeaders()
    {
        var result = CliIdentityService.ApplyCliIdentity(
            new Dictionary<string, string> { ["x-stainless-future-header"] = "old" },
            CliIdentityType.Codex,
            "0.153.4");

        Assert.False(result.ContainsKey("x-stainless-future-header"));
    }

    [Theory]
    [InlineData(CliIdentityType.ClaudeCode, "claude-cli/2.1.263 (external, cli)")]
    [InlineData(CliIdentityType.Codex, "codex-cli/0.153.4")]
    [InlineData(CliIdentityType.Grok, "grok-cli/1.0.6 (external, cli)")]
    public void DetectCliIdentity_ReturnsMatchedFamily(CliIdentityType expected, string userAgent)
    {
        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = userAgent,
        };

        Assert.Equal(expected, CliIdentityService.DetectCliIdentity(headers));
    }

    [Fact]
    public void DetectCliIdentity_ReturnsNoneWhenEmpty()
    {
        var headers = new Dictionary<string, string>();

        Assert.Null(CliIdentityService.DetectCliIdentity(headers));
    }

    [Fact]
    public void DetectCliIdentity_ReturnsNoneWhenUaMismatched()
    {
        var headers = new Dictionary<string, string>
        {
            // 手改过的 UA 前缀，不再是任何已知 CLI
            ["User-Agent"] = "custom-client/1.0.0 (external)",
            ["x-grok-client-identifier"] = "grok-cli",
        };

        Assert.Null(CliIdentityService.DetectCliIdentity(headers));
    }

    [Fact]
    public void DetectCliVersion_ClaudeCode_ExtractsVersionFromUa()
    {
        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = "claude-cli/2.1.263 (external, cli)",
        };

        Assert.Equal("2.1.263", CliIdentityService.DetectCliVersion(headers, CliIdentityType.ClaudeCode));
    }

    [Fact]
    public void DetectCliVersion_Codex_ExtractsVersionFromUa()
    {
        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = "codex-cli/0.153.4",
        };

        Assert.Equal("0.153.4", CliIdentityService.DetectCliVersion(headers, CliIdentityType.Codex));
    }

    [Fact]
    public void DetectCliVersion_ReturnsNullWhenUaMissing()
    {
        var headers = new Dictionary<string, string>();

        Assert.Null(CliIdentityService.DetectCliVersion(headers, CliIdentityType.ClaudeCode));
    }
}
