using Avalonia;
using LoomX.NodeGraph;
using Xunit;

namespace LoomX.Tests.NodeGraph;

public sealed class RuntimeGraphLayoutTests
{
    [Fact]
    public void LayoutIsDeterministicRegardlessOfInputOrder()
    {
        var first = RuntimeGraphLayout.Create(CreateSnapshot(false));
        var second = RuntimeGraphLayout.Create(CreateSnapshot(true));

        Assert.Equal(first.ContentBounds, second.ContentBounds);
        Assert.Equal(first.Nodes.Keys.OrderBy(item => item), second.Nodes.Keys.OrderBy(item => item));
        foreach (var nodeId in first.Nodes.Keys)
            Assert.Equal(first.Nodes[nodeId].Bounds, second.Nodes[nodeId].Bounds);
    }

    [Fact]
    public void LayoutUsesEndpointComboModelColumnsAndDrawsOnlyDirectEdges()
    {
        var snapshot = CreateSnapshot(false);
        var layout = RuntimeGraphLayout.Create(snapshot);
        var endpoint = layout.Nodes["endpoint-a"];
        var combo = layout.Nodes["combo|coding"];
        var model = layout.Nodes["model|provider-a|model-a"];

        Assert.True(endpoint.Bounds.Right < combo.Bounds.Left);
        Assert.True(combo.Bounds.Right < model.Bounds.Left);
        Assert.Equal(2, layout.Edges.Count);
        Assert.All(layout.Edges, edge => Assert.True(edge.Source.X < edge.Target.X));
    }

    [Fact]
    public void ModelNodesUseCompactTwoLineDimensions()
    {
        var layout = RuntimeGraphLayout.Create(CreateSnapshot(false));
        var model = layout.Nodes["model|provider-a|model-a"];

        Assert.Equal(220, model.Bounds.Width);
        Assert.Equal(58, model.Bounds.Height);
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
            new RuntimeGraphNode("combo|coding", RuntimeGraphNodeKind.Combo, "coding", true, SortOrder: 0),
            new RuntimeGraphNode("combo|default", RuntimeGraphNodeKind.Combo, "default", true, SortOrder: 1)
        };
        var models = new[]
        {
            new RuntimeGraphNode("model|provider-a|model-a", RuntimeGraphNodeKind.Model, "model-a", true, ProviderId: "provider-a", ProviderDisplayName: "Provider A", ModelId: "model-a")
        };
        var edges = new[]
        {
            new RuntimeGraphEdge("endpoint-combo|endpoint-a|combo|coding", RuntimeGraphEdgeKind.EndpointToCombo, "endpoint-a", "combo|coding", true, "endpoint-a"),
            new RuntimeGraphEdge("combo-model|combo|coding|provider-a|model-a", RuntimeGraphEdgeKind.ComboToModel, "combo|coding", "model|provider-a|model-a", true, "", "combo|coding", "provider-a", "model-a")
        };
        return new RuntimeGraphSnapshot(
            reverse ? endpoints.Reverse().ToArray() : endpoints,
            reverse ? combos.Reverse().ToArray() : combos,
            models,
            reverse ? edges.Reverse().ToArray() : edges,
            []);
    }
}
