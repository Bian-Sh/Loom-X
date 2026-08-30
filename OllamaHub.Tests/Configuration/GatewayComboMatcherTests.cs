using OllamaHub.Configuration;
using Xunit;

namespace OllamaHub.Tests.Configuration;

public sealed class GatewayComboMatcherTests
{
    [Fact]
    public void MatchesOnlyDeclaredComboName()
    {
        var model = new ResolvedModelConfig
        {
            ModelId = "deepseek-v4",
            DisplayName = "DeepSeek V4",
            OllamaModelName = "DeepSeek V4::default",
            ProviderId = "provider",
            BaseUrl = "https://example.com",
            ApiKey = string.Empty,
            AnthropicModel = "deepseek-v4"
        };
        var combo = new ResolvedGatewayComboConfig
        {
            Name = "公共模型",
            Enabled = true,
            Routes = [new ResolvedGatewayRouteConfig { Model = model, Enabled = true }]
        };

        Assert.True(GatewayComboMatcher.Matches(combo, "公共模型"));
        Assert.False(GatewayComboMatcher.Matches(combo, " deepseek-v4 "));
        Assert.False(GatewayComboMatcher.Matches(combo, "DeepSeek V4"));
        Assert.False(GatewayComboMatcher.Matches(combo, "DeepSeek V4::default"));
    }

    [Fact]
    public void IgnoresDisabledMemberRoutes()
    {
        var combo = new ResolvedGatewayComboConfig
        {
            Name = "公共模型",
            Enabled = true,
            Routes =
            [
                new ResolvedGatewayRouteConfig
                {
                    Enabled = false,
                    Model = new ResolvedModelConfig
                    {
                        ModelId = "disabled-model",
                        DisplayName = "禁用模型",
                        OllamaModelName = "禁用模型",
                        ProviderId = "provider",
                        BaseUrl = "https://example.com",
                        ApiKey = string.Empty,
                        AnthropicModel = "disabled-model"
                    }
                }
            ]
        };

        Assert.False(GatewayComboMatcher.Matches(combo, "disabled-model"));
    }
}
