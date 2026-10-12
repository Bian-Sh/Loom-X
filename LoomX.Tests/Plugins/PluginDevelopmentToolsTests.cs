using System.Text.Json.Nodes;
using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginDevelopmentToolsTests
{
    [Fact]
    public async Task 六个注册工具提供稳定风险和安全参数投影()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var registry = new ToolRegistry();
        PluginDevelopmentTools.RegisterAll(registry, fixture.Service);
        Assert.Equal(PluginDevelopmentTools.Names, registry.All.Select(tool => tool.Name));
        foreach (var tool in registry.All)
        {
            Assert.Equal(tool.Name.EndsWith("validate") || tool.Name.EndsWith("diagnostics") ? ToolRiskLevel.Read : ToolRiskLevel.Write, tool.RiskLevel);
            Assert.False(tool.ParametersSchema["additionalProperties"]!.GetValue<bool>());
            var projection = tool.SafeArgumentsProjector!(new JsonObject { ["plugin_id"] = "sk-private-argument-sentinel", ["payload"] = "敏感正文" })!.ToJsonString();
            Assert.DoesNotContain("sk-private", projection);
            Assert.DoesNotContain("敏感正文", projection);
            Assert.NotNull(tool.Handler);
        }
    }

    [Fact]
    public async Task 参数错误和信任拒绝仍返回安全机器可读PluginDiagnostic()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var registry = new ToolRegistry();
        PluginDevelopmentTools.RegisterAll(registry, fixture.Service);
        Assert.True(registry.TryGet("loomx.plugin.test", out var tool));
        var refusal = await tool!.Handler(new JsonObject { ["plugin_id"] = "demo" }, default);
        Assert.False(refusal.Success);
        var json = JsonNode.Parse(refusal.Content)!;
        Assert.Equal("LX_TRUST_REQUIRED", json["diagnostics"]![0]!["code"]!.GetValue<string>());
        Assert.NotNull(json["diagnostics"]![0]!["stage"]);
        Assert.NotNull(json["diagnostics"]![0]!["severity"]);
        var invalid = await tool.Handler(new JsonObject { ["plugin_id"] = 42, ["unexpected"] = "sk-private-sentinel" }, default);
        Assert.False(invalid.Success);
        Assert.Equal("LX_DEVELOPMENT_ARGUMENT_INVALID", JsonNode.Parse(invalid.Content)!["diagnostics"]![0]!["code"]!.GetValue<string>());
        Assert.DoesNotContain("sk-private", invalid.Content);
    }

    [Fact]
    public async Task 用户可以仅通过稳定工具完成创建验证编译诊断和测试()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var registry = new ToolRegistry();
        PluginDevelopmentTools.RegisterAll(registry, fixture.Service);
        foreach (var operation in new[] { "create", "validate", "compile", "diagnostics", "test" })
        {
            Assert.True(registry.TryGet("loomx.plugin." + operation, out var tool));
            var args = new JsonObject { ["plugin_id"] = "demo", ["plugin_api_major"] = 1 };
            if (operation == "test") args["trust_code"] = true;
            var result = await tool!.Handler(args, default);
            Assert.True(result.Success, result.Content);
            Assert.True(JsonNode.Parse(result.Content)!["success"]!.GetValue<bool>());
            Assert.DoesNotContain(fixture.Root, result.Content);
        }
        Assert.Empty(fixture.Runtime.PluginInfos);
    }
}
