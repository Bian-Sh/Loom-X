using OllamaHub.Configuration;
using Xunit;

namespace OllamaHub.Tests.Configuration;

public sealed class GatewayComboCatalogTests
{
    [Fact]
    public void ExcludesComboFromAnotherEndpoint()
    {
        var combo = CreateCombo("deepseek-v4");
        var config = new ResolvedAppConfig
        {
            GatewayEndpoints =
            [
                new ResolvedGatewayEndpointConfig { Key = "openai", PublicPath = "/openai", Enabled = true },
                new ResolvedGatewayEndpointConfig { Key = "ollama", PublicPath = "/", Enabled = true, Combos = [combo] }
            ]
        };

        var result = GatewayComboCatalog.ForEndpoint(config, "openai");

        Assert.Empty(result);
    }

    [Fact]
    public void UsesCurrentEndpointWhenComboNamesOverlap()
    {
        var local = CreateCombo("shared", "local-model", sortOrder: 10);
        var global = CreateCombo("shared", "global-model", sortOrder: 1);
        var config = new ResolvedAppConfig
        {
            GatewayEndpoints =
            [
                new ResolvedGatewayEndpointConfig { Key = "openai", PublicPath = "/openai", Enabled = true, Combos = [local] },
                new ResolvedGatewayEndpointConfig { Key = "ollama", PublicPath = "/", Enabled = true, Combos = [global] }
            ]
        };

        var result = GatewayComboCatalog.ForEndpoint(config, "openai");

        Assert.Single(result);
        Assert.Single(result);
        Assert.Equal("local-model", result[0].Routes[0].Model.ModelId);
    }

    private static ResolvedGatewayComboConfig CreateCombo(string name, string modelId = "model", int sortOrder = 0) => new()
    {
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
