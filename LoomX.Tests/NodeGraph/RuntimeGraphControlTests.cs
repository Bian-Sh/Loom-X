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
        Assert.InRange(control.Zoom, RuntimeGraphControl.MinZoom, RuntimeGraphControl.MaxZoom);
    }

    [Fact]
    public void SelectionAndInspectorFollowTransformedNodeBounds()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout, Zoom = 1.5, Pan = new Vector(80, 35) };
        var graphNode = layout.Nodes["provider-a|model-a"].Bounds;
        var viewportPoint = new Point(
            graphNode.Center.X * control.Zoom + control.Pan.X,
            graphNode.Center.Y * control.Zoom + control.Pan.Y);

        var selection = control.SelectAt(viewportPoint);
        var details = control.GetSelectionDetails();

        Assert.Equal(new RuntimeGraphSelection(RuntimeGraphSelectionKind.Node, "provider-a|model-a"), selection);
        Assert.NotNull(details);
        Assert.Equal("model-a", details.DisplayName);
        Assert.Equal("provider-a", details.ProviderId);
        Assert.Equal(RuntimeGraphSelectionKind.Node, details.Kind);
    }

    [Fact]
    public void ProviderGroupSelectionReturnsGroupInspectorDetails()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout };

        var providerBounds = layout.ProviderGroups["provider-a"].Bounds;
        var selection = control.SelectAt(new Point(providerBounds.Center.X, providerBounds.Top + 18));
        var details = control.GetSelectionDetails();

        Assert.Equal(new RuntimeGraphSelection(RuntimeGraphSelectionKind.ProviderGroup, "provider-a"), selection);
        Assert.NotNull(details);
        Assert.Equal("Provider A", details.DisplayName);
        Assert.Equal(1, details.ModelCount);
        Assert.Equal("openai", details.Protocol);
    }

    [Fact]
    public void FocusEndpointCentersAndSelectsTheEndpoint()
    {
        var snapshot = CreateSnapshot();
        var layout = RuntimeGraphLayout.Create(snapshot);
        var control = new RuntimeGraphControl { Snapshot = snapshot, Layout = layout };

        Assert.True(control.FocusEndpoint("endpoint-a", new Size(900, 500)));

        var endpointBounds = layout.Nodes["endpoint-a"].Bounds;
        var viewportCenter = new Point(
            endpointBounds.Center.X * control.Zoom + control.Pan.X,
            endpointBounds.Center.Y * control.Zoom + control.Pan.Y);
        Assert.Equal(new Point(450, 250), viewportCenter);
        Assert.Equal(new RuntimeGraphSelection(RuntimeGraphSelectionKind.Node, "endpoint-a"), control.Selection);
        Assert.False(control.FocusEndpoint("missing-endpoint", new Size(900, 500)));
    }

    [Fact]
    public void KindWatermarksOnlyAppearAfterTheReadableZoomThreshold()
    {
        Assert.Equal(0.45, RuntimeGraphControl.KindWatermarkMinZoom);
        Assert.True(RuntimeGraphControl.KindWatermarkMinZoom > RuntimeGraphControl.MinZoom);
        Assert.True(RuntimeGraphControl.KindWatermarkMinZoom < RuntimeGraphControl.MaxZoom);
        Assert.Equal(10, RuntimeGraphControl.KindWatermarkMinFontSize);
        Assert.Equal(15, RuntimeGraphControl.KindWatermarkMaxFontSize);
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
