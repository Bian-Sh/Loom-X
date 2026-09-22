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
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout };

        control.Measure(new Size(2000, 2000));

        Assert.Equal(layout.ContentBounds.Size, control.DesiredSize);
    }

    [Fact]
    public void FitToViewKeepsContentInsideRequestedViewport()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout };

        control.FitToView(new Size(900, 500));

        var left = layout.ContentBounds.Left * control.Zoom + control.Pan.X;
        var top = layout.ContentBounds.Top * control.Zoom + control.Pan.Y;
        var right = layout.ContentBounds.Right * control.Zoom + control.Pan.X;
        var bottom = layout.ContentBounds.Bottom * control.Zoom + control.Pan.Y;
        Assert.InRange(left, 23.99, 876.01);
        Assert.InRange(top, 23.99, 476.01);
        Assert.InRange(right, 23.99, 876.01);
        Assert.InRange(bottom, 23.99, 476.01);
    }

    [Fact]
    public void ModelSelectionExposesProviderMetadataWithoutProviderNode()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout };
        var bounds = layout.Nodes["model|provider-a|model-a"].Bounds;

        var selection = control.SelectAt(bounds.Center);
        var details = control.GetSelectionDetails();

        Assert.Equal(new RuntimeGraphSelection(RuntimeGraphSelectionKind.Node, "model|provider-a|model-a"), selection);
        Assert.NotNull(details);
        Assert.Equal("model-a", details.DisplayName);
        Assert.Equal("provider-a", details.ProviderId);
        Assert.Equal("https://example.invalid", details.BaseUrl);
        Assert.Equal("openai", details.Protocol);
    }

    [Fact]
    public void FocusEndpointCentersAndSelectsTheEndpoint()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout };

        Assert.True(control.FocusEndpoint("endpoint-a", new Size(900, 500)));
        Assert.Equal(new RuntimeGraphSelection(RuntimeGraphSelectionKind.Node, "endpoint-a"), control.Selection);
        Assert.False(control.FocusEndpoint("missing-endpoint", new Size(900, 500)));
    }

    private static RuntimeGraphSnapshot CreateSnapshot()
    {
        var model = new RuntimeGraphNode(
            "model|provider-a|model-a",
            RuntimeGraphNodeKind.Model,
            "model-a",
            true,
            ProviderId: "provider-a",
            ProviderDisplayName: "Provider A",
            ProviderBaseUrl: "https://example.invalid",
            ProviderProtocol: "openai",
            ModelId: "model-a");
        return new RuntimeGraphSnapshot(
            [new RuntimeGraphNode("endpoint-a", RuntimeGraphNodeKind.Endpoint, "OpenAI", true)],
            [new RuntimeGraphNode("combo|coding", RuntimeGraphNodeKind.Combo, "coding", true)],
            [model],
            [
                new RuntimeGraphEdge("endpoint-combo|endpoint-a|combo|coding", RuntimeGraphEdgeKind.EndpointToCombo, "endpoint-a", "combo|coding", true, "endpoint-a"),
                new RuntimeGraphEdge("combo-model|combo|coding|provider-a|model-a", RuntimeGraphEdgeKind.ComboToModel, "combo|coding", "model|provider-a|model-a", true, "", "combo|coding", "provider-a", "model-a")
            ],
            []);
    }
}
