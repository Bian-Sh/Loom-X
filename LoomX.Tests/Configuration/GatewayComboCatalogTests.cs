using LoomX.Configuration;
using Xunit;

namespace LoomX.Tests.Configuration;

public sealed class GatewayComboCatalogTests
{
    [Fact]
    public void ExcludesGlobalComboWhenEndpointIsNotBound()
    {
        var combo = CreateCombo("deepseek-v4");
        var config = new ResolvedAppConfig
        {
            GatewayCombos = [combo],
            GatewayEndpoints =
            [
                new ResolvedGatewayEndpointConfig { Key = "openai", PublicPath = "/openai", Enabled = true }
            ]
        };

        Assert.Empty(GatewayComboCatalog.ForEndpoint(config, "openai"));
    }

    [Fact]
    public void SharesTheSameGlobalComboAcrossEndpoints()
    {
        var combo = CreateCombo("shared", "shared-model");
        var config = new ResolvedAppConfig
        {
            GatewayCombos = [combo],
            GatewayEndpoints =
            [
                new ResolvedGatewayEndpointConfig
                {
                    Key = "openai", PublicPath = "/openai", Enabled = true,
                    ComboBindings = [new ResolvedGatewayComboBindingConfig { ComboId = combo.Id, Enabled = true, SortOrder = 0 }]
                },
                new ResolvedGatewayEndpointConfig
                {
                    Key = "ollama", PublicPath = "/", Enabled = true,
                    ComboBindings = [new ResolvedGatewayComboBindingConfig { ComboId = combo.Id, Enabled = true, SortOrder = 0 }]
                }
            ]
        };

        Assert.Same(combo, Assert.Single(GatewayComboCatalog.ForEndpoint(config, "openai")));
        Assert.Same(combo, Assert.Single(GatewayComboCatalog.ForEndpoint(config, "ollama")));
    }

    private static ResolvedGatewayComboConfig CreateCombo(string name, string modelId = "model", int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Enabled = true,
        SortOrder = sortOrder,
        Routes =
        [
            new ResolvedGatewayRouteConfig
            {
                Enabled = true,
                Model = new ResolvedModelConfig
                {
                    ModelId = modelId,
                    DisplayName = modelId,
                    OllamaModelName = modelId,
                    ProviderId = "provider",
                    BaseUrl = "https://example.com",
                    ApiKey = string.Empty,
                    AnthropicModel = modelId
                }
            }
        ]
    };
}
