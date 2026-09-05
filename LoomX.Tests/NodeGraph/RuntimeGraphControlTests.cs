using Avalonia;
using LoomX.NodeGraph;
using Xunit;

namespace LoomX.Tests.NodeGraph;

public sealed class RuntimeGraphControlTests
{
    [Fact]
    public void ControlMeasuresFromProvidedLayout()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl
        {
            Snapshot = snapshot,
            Layout = layout
        };

        control.Measure(new Size(2000, 2000));

        Assert.Equal(layout.ContentBounds.Size, control.DesiredSize);
    }

    [Fact]
    public void ControlCanDeriveLayoutFromRuntimeSnapshot()
    {
        var snapshot = CreateSnapshot();
        var expected = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot };

        control.Measure(new Size(2000, 2000));

        Assert.Equal(expected.ContentBounds.Size, control.DesiredSize);
    }

    private static RuntimeGraphSnapshot CreateSnapshot()
    {
        var model = new RuntimeGraphNode(
            "provider-a|model-a",
            RuntimeGraphNodeKind.Model,
            "model-a",
            true,
            ProviderId: "provider-a",
            ModelId: "model-a");
        return new RuntimeGraphSnapshot(
            [new RuntimeGraphNode("endpoint-a", RuntimeGraphNodeKind.Endpoint, "OpenAI", true)],
            [new RuntimeGraphNode("endpoint-a|coding", RuntimeGraphNodeKind.Combo, "coding", true, EndpointId: "endpoint-a")],
            [new RuntimeGraphProviderGroup("provider-a", "Provider A", true, "https://example.invalid", "openai", [model])],
            [
                new RuntimeGraphEdge("endpoint-combo|endpoint-a|coding", RuntimeGraphEdgeKind.EndpointToCombo, "endpoint-a", "endpoint-a|coding", true, "endpoint-a"),
                new RuntimeGraphEdge("combo-provider|endpoint-a|coding|provider-a", RuntimeGraphEdgeKind.ComboToProvider, "endpoint-a|coding", "provider-a", true, "endpoint-a", "endpoint-a|coding", "provider-a"),
                new RuntimeGraphEdge("provider-model|provider-a|model-a", RuntimeGraphEdgeKind.ProviderToModel, "provider-a", "provider-a|model-a", true, "", ProviderId: "provider-a", ModelId: "model-a")
            ],
            []);
    }
}
