using LoomX.Configuration;
using LoomX.NodeGraph;
using Xunit;

namespace LoomX.Tests.NodeGraph;

public sealed class RuntimeGraphProjectionTests
{
    [Fact]
    public void CreatesEndpointComboModelTopologyWithProviderMetadataOnModel()
    {
        var model = CreateModel("provider-a", "model-a");
        var combo = CreateCombo("coding", model);
        var config = CreateConfig(
            [new ResolvedGatewayEndpointConfig
            {
                Key = "openai", PublicPath = "/openai", Enabled = true,
                ComboBindings = [new ResolvedGatewayComboBindingConfig { ComboId = combo.Id, Enabled = true }]
            }],
            [combo],
            [model]);

        var snapshot = RuntimeGraphProjection.Create(config, [CreateProvider("provider-a", "Provider A")]);

        Assert.Single(snapshot.Endpoints);
        var projectedCombo = Assert.Single(snapshot.Combos);
        var projectedModel = Assert.Single(snapshot.Models);
        var route = Assert.Single(snapshot.Routes);
        Assert.Equal(RuntimeGraphIds.Combo(combo.Id), projectedCombo.Id);
        Assert.Equal("model-a", projectedModel.ModelId);
        Assert.Equal("Provider A", projectedModel.ProviderDisplayName);
        Assert.Equal([RuntimeGraphEdgeKind.EndpointToCombo, RuntimeGraphEdgeKind.ComboToModel], snapshot.Edges.Select(item => item.Kind));
        Assert.Equal(
            [RuntimeGraphIds.EndpointComboEdge("openai", projectedCombo.Id), RuntimeGraphIds.ComboModelEdge(projectedCombo.Id, "provider-a", "model-a")],
            route.EdgeIds);
    }

    [Fact]
    public void SharesOneComboAndModelAcrossMultipleEndpointBindings()
    {
        var model = CreateModel("provider-a", "shared-model");
        var combo = CreateCombo("coding", model);
        var endpoints = new[] { "openai", "azure" }.Select(key => new ResolvedGatewayEndpointConfig
        {
            Key = key,
            PublicPath = $"/{key}",
            Enabled = true,
            ComboBindings = [new ResolvedGatewayComboBindingConfig { ComboId = combo.Id, Enabled = true }]
        }).ToArray();

        var snapshot = RuntimeGraphProjection.Create(CreateConfig(endpoints, [combo], [model]), [CreateProvider("provider-a", "Provider A")]);

        Assert.Single(snapshot.Combos);
        Assert.Single(snapshot.Models);
        Assert.Equal(2, snapshot.Routes.Count);
        Assert.Equal(2, snapshot.Edges.Count(item => item.Kind == RuntimeGraphEdgeKind.EndpointToCombo));
        Assert.Single(snapshot.Edges, item => item.Kind == RuntimeGraphEdgeKind.ComboToModel);
    }

    [Fact]
    public void DisabledRouteRemainsVisibleButDoesNotCreateActiveRouteBinding()
    {
        var model = CreateModel("provider-a", "model-a");
        var combo = new ResolvedGatewayComboConfig
        {
            Id = Guid.NewGuid(),
            Name = "disabled-route",
            Enabled = true,
            Routes = [new ResolvedGatewayRouteConfig { Model = model, Enabled = false }]
        };
        var endpoint = new ResolvedGatewayEndpointConfig
        {
            Key = "openai", PublicPath = "/openai", Enabled = true,
            ComboBindings = [new ResolvedGatewayComboBindingConfig { ComboId = combo.Id, Enabled = true }]
        };

        var snapshot = RuntimeGraphProjection.Create(CreateConfig([endpoint], [combo], [model]), [CreateProvider("provider-a", "Provider A")]);

        Assert.Empty(snapshot.Routes);
        Assert.Contains(snapshot.Edges, item => item.Kind == RuntimeGraphEdgeKind.ComboToModel && !item.Enabled);
    }

    private static ResolvedAppConfig CreateConfig(
        IReadOnlyList<ResolvedGatewayEndpointConfig> endpoints,
        IReadOnlyList<ResolvedGatewayComboConfig> combos,
        IReadOnlyList<ResolvedModelConfig> models) => new()
    {
        GatewayEndpoints = endpoints,
        GatewayCombos = combos,
        Models = models
    };

    private static ResolvedGatewayComboConfig CreateCombo(string name, ResolvedModelConfig model) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Enabled = true,
        Routes = [new ResolvedGatewayRouteConfig { Model = model, Enabled = true }]
    };

    private static ResolvedModelConfig CreateModel(string providerId, string modelId) => new()
    {
        ModelId = modelId,
        DisplayName = modelId,
        OllamaModelName = modelId,
        ProviderId = providerId,
        BaseUrl = "https://example.com",
        ApiKey = string.Empty,
        AnthropicModel = modelId
    };

    private static ProviderResponse CreateProvider(string businessId, string displayName) =>
        new(Guid.NewGuid(), businessId, displayName, "https://example.com", "openai", true, false, false, 1, "{}", []);
}
