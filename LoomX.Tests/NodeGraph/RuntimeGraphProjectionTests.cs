using LoomX.Configuration;
using LoomX.NodeGraph;
using Xunit;

namespace LoomX.Tests.NodeGraph;

public sealed class RuntimeGraphProjectionTests
{
    [Fact]
    public void CreatesLayeredTopologyWithProviderGroupsAndRouteBinding()
    {
        var config = CreateConfig(
            new ResolvedGatewayEndpointConfig
            {
                Key = "openai",
                PublicPath = "/openai",
                Enabled = true,
                Combos =
                [
                    new ResolvedGatewayComboConfig
                    {
                        Name = "coding",
                        Enabled = true,
                        SortOrder = 0,
                        Routes = [new ResolvedGatewayRouteConfig { Model = CreateModel("provider-a", "model-a"), Enabled = true, SortOrder = 0 }]
                    }
                ]
            });
        var providers = new[]
        {
            CreateProvider("provider-a", "Provider A", 1),
            CreateProvider("provider-empty", "Empty Provider", 0)
        };

        var snapshot = RuntimeGraphProjection.Create(config, providers);

        var endpoint = Assert.Single(snapshot.Endpoints);
        var combo = Assert.Single(snapshot.Combos);
        var provider = Assert.Single(snapshot.Providers, item => item.Id == "provider-a");
        var emptyProvider = Assert.Single(snapshot.Providers, item => item.Id == "provider-empty");
        var model = Assert.Single(provider.Models);
        var route = Assert.Single(snapshot.Routes);

        Assert.Equal("openai", endpoint.Id);
        Assert.Equal("openai|coding", combo.Id);
        Assert.Equal("provider-a|model-a", model.Id);
        Assert.Empty(emptyProvider.Models);
        Assert.Equal(
            [RuntimeGraphEdgeKind.EndpointToCombo, RuntimeGraphEdgeKind.ComboToProvider, RuntimeGraphEdgeKind.ProviderToModel],
            snapshot.Edges.Select(item => item.Kind));
        Assert.Equal(
            [
                "endpoint-combo|openai|coding",
                "combo-provider|openai|coding|provider-a",
                "provider-model|provider-a|model-a"
            ],
            route.EdgeIds);
        Assert.Equal("coding", route.ComboName);
        Assert.Same(route, snapshot.FindRoute("openai", "coding", "provider-a", "model-a"));
        Assert.Null(snapshot.FindRoute("openai", "other", "provider-a", "model-a"));
    }

    [Fact]
    public void DeduplicatesSharedProviderAndModelNodesWhileKeepingDistinctRoutes()
    {
        var model = CreateModel("provider-a", "shared-model");
        var config = CreateConfig(
            new ResolvedGatewayEndpointConfig
            {
                Key = "openai",
                PublicPath = "/openai",
                Enabled = true,
                Combos = [CreateCombo("coding", model)]
            },
            new ResolvedGatewayEndpointConfig
            {
                Key = "azure",
                PublicPath = "/azure",
                Enabled = true,
                Combos = [CreateCombo("coding", model)]
            });

        var snapshot = RuntimeGraphProjection.Create(config, [CreateProvider("provider-a", "Provider A", 1)]);

        Assert.Single(snapshot.Providers);
        Assert.Single(Assert.Single(snapshot.Providers).Models);
        Assert.Equal(2, snapshot.Routes.Count);
        Assert.Equal(2, snapshot.Edges.Count(item => item.Kind == RuntimeGraphEdgeKind.EndpointToCombo));
        Assert.Equal(2, snapshot.Edges.Count(item => item.Kind == RuntimeGraphEdgeKind.ComboToProvider));
        Assert.Single(snapshot.Edges, item => item.Kind == RuntimeGraphEdgeKind.ProviderToModel);
        Assert.Equal(
            ["azure|coding", "openai|coding"],
            snapshot.Routes.Select(item => item.ComboId));
    }

    [Fact]
    public void DisabledRouteRemainsVisibleButDoesNotCreateActiveRouteBinding()
    {
        var model = CreateModel("provider-a", "model-a");
        var config = CreateConfig(
            new ResolvedGatewayEndpointConfig
            {
                Key = "openai",
                PublicPath = "/openai",
                Enabled = true,
                Combos =
                [
                    new ResolvedGatewayComboConfig
                    {
                        Name = "disabled-route",
                        Enabled = true,
                        Routes = [new ResolvedGatewayRouteConfig { Model = model, Enabled = false }]
                    }
                ]
            });

        var snapshot = RuntimeGraphProjection.Create(config, [CreateProvider("provider-a", "Provider A", 1)]);

        Assert.Empty(snapshot.Routes);
        Assert.Contains(snapshot.Edges, item => item.Kind == RuntimeGraphEdgeKind.ComboToProvider && !item.Enabled);
        Assert.Contains(snapshot.Edges, item => item.Kind == RuntimeGraphEdgeKind.ProviderToModel);
    }

    private static ResolvedAppConfig CreateConfig(params ResolvedGatewayEndpointConfig[] endpoints) => new()
    {
        GatewayEndpoints = endpoints,
        Models = endpoints
            .SelectMany(endpoint => endpoint.Combos)
            .SelectMany(combo => combo.Routes)
            .Select(route => route.Model)
            .GroupBy(model => $"{model.ProviderId}|{model.ModelId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray()
    };

    private static ResolvedGatewayComboConfig CreateCombo(string name, ResolvedModelConfig model) => new()
    {
        Name = name,
        Enabled = true,
        Routes = [new ResolvedGatewayRouteConfig { Model = model, Enabled = true }]
    };

    private static ResolvedModelConfig CreateModel(string providerId, string modelId) => new()
    {
        ModelId = modelId,
        OllamaModelName = modelId,
        DisplayName = modelId,
        ProviderId = providerId,
        BaseUrl = "https://example.invalid",
        ApiKey = "",
        AnthropicModel = modelId,
        ApiModes = ["openai"]
    };

    private static ProviderResponse CreateProvider(string id, string displayName, int modelCount) => new(
        Guid.NewGuid(),
        id,
        displayName,
        "https://example.invalid",
        "openai",
        true,
        false,
        false,
        modelCount,
        "{}",
        []);
}
