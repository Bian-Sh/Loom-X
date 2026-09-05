using Avalonia;
using LoomX.NodeGraph;
using Xunit;

namespace LoomX.Tests.NodeGraph;

public sealed class RuntimeGraphLayoutTests
{
    [Fact]
    public void LayoutIsStableWhenSnapshotCollectionsArriveInDifferentOrders()
    {
        var first = RuntimeGraphLayout.Create(CreateSnapshot(reverse: false));
        var second = RuntimeGraphLayout.Create(CreateSnapshot(reverse: true));

        Assert.Equal(first.ContentBounds, second.ContentBounds);
        foreach (var nodeId in first.Nodes.Keys)
            Assert.Equal(first.Nodes[nodeId].Bounds, second.Nodes[nodeId].Bounds);
        foreach (var providerId in first.ProviderGroups.Keys)
            Assert.Equal(first.ProviderGroups[providerId].Bounds, second.ProviderGroups[providerId].Bounds);
        Assert.Equal(
            first.Edges.Select(edge => (edge.EdgeId, edge.Source, edge.Target)),
            second.Edges.Select(edge => (edge.EdgeId, edge.Source, edge.Target)));
    }

    [Fact]
    public void LayoutUsesLeftToRightLayersAndContainsModelsInsideProviderGroups()
    {
        var layout = RuntimeGraphLayout.Create(CreateSnapshot(reverse: false));
        var endpoint = layout.Nodes["endpoint-a"];
        var combo = layout.Nodes["endpoint-a|coding"];
        var provider = layout.ProviderGroups["provider-a"];
        var model = layout.Nodes["provider-a|model-a"];

        Assert.True(endpoint.Bounds.Right < combo.Bounds.Left);
        Assert.True(combo.Bounds.Right < provider.Bounds.Left);
        Assert.True(provider.Bounds.Contains(model.Bounds));
        Assert.True(model.Bounds.Left > provider.Bounds.Left);
        Assert.True(model.Bounds.Right < provider.Bounds.Right);
        Assert.True(model.Bounds.Top > provider.Bounds.Top);
    }

    [Fact]
    public void EmptyProviderGetsVisibleGroupBoundsAndNoModelLayouts()
    {
        var options = new RuntimeGraphLayoutOptions { EmptyProviderHeight = 120 };
        var layout = RuntimeGraphLayout.Create(CreateSnapshot(reverse: false), options);
        var provider = layout.ProviderGroups["provider-empty"];

        Assert.Empty(provider.ModelIds);
        Assert.Equal(120, provider.Bounds.Height);
        Assert.True(provider.Bounds.Width > 0);
    }

    [Fact]
    public void EdgeAnchorsConnectRightSideToLeftSideOfLayeredObjects()
    {
        var layout = RuntimeGraphLayout.Create(CreateSnapshot(reverse: false));
        var endpointCombo = Assert.Single(layout.Edges, edge => edge.EdgeId == "endpoint-combo|endpoint-a|coding");
        var comboProvider = Assert.Single(layout.Edges, edge => edge.EdgeId == "combo-provider|endpoint-a|coding|provider-a");

        Assert.Equal(layout.Nodes["endpoint-a"].Bounds.Right, endpointCombo.Source.X);
        Assert.Equal(layout.Nodes["endpoint-a|coding"].Bounds.Left, endpointCombo.Target.X);
        Assert.Equal(layout.Nodes["endpoint-a|coding"].Bounds.Right, comboProvider.Source.X);
        Assert.Equal(layout.ProviderGroups["provider-a"].Bounds.Left, comboProvider.Target.X);
    }

    private static RuntimeGraphSnapshot CreateSnapshot(bool reverse)
    {
        var endpoints = new[]
        {
            new RuntimeGraphNode("endpoint-a", RuntimeGraphNodeKind.Endpoint, "OpenAI", true),
            new RuntimeGraphNode("endpoint-b", RuntimeGraphNodeKind.Endpoint, "Azure", true)
        };
        var combos = new[]
        {
            new RuntimeGraphNode("endpoint-a|coding", RuntimeGraphNodeKind.Combo, "coding", true, EndpointId: "endpoint-a"),
            new RuntimeGraphNode("endpoint-b|default", RuntimeGraphNodeKind.Combo, "default", true, EndpointId: "endpoint-b")
        };
        var model = new RuntimeGraphNode("provider-a|model-a", RuntimeGraphNodeKind.Model, "model-a", true, ProviderId: "provider-a", ModelId: "model-a");
        var providers = new[]
        {
            new RuntimeGraphProviderGroup("provider-a", "Provider A", true, "https://example.invalid", "openai", [model]),
            new RuntimeGraphProviderGroup("provider-empty", "Empty Provider", true, "https://example.invalid", "openai", [])
        };
        var edges = new[]
        {
            new RuntimeGraphEdge("endpoint-combo|endpoint-a|coding", RuntimeGraphEdgeKind.EndpointToCombo, "endpoint-a", "endpoint-a|coding", true, "endpoint-a", ComboId: "endpoint-a|coding"),
            new RuntimeGraphEdge("combo-provider|endpoint-a|coding|provider-a", RuntimeGraphEdgeKind.ComboToProvider, "endpoint-a|coding", "provider-a", true, "endpoint-a", "endpoint-a|coding", "provider-a"),
            new RuntimeGraphEdge("provider-model|provider-a|model-a", RuntimeGraphEdgeKind.ProviderToModel, "provider-a", "provider-a|model-a", true, "", ProviderId: "provider-a", ModelId: "model-a")
        };
        var snapshot = new RuntimeGraphSnapshot(endpoints, combos, providers, edges, []);
        return reverse
            ? snapshot with
            {
                Endpoints = endpoints.Reverse().ToArray(),
                Combos = combos.Reverse().ToArray(),
                Providers = providers.Reverse().ToArray(),
                Edges = edges.Reverse().ToArray()
            }
            : snapshot;
    }
}
