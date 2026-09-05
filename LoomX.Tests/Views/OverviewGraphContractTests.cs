using System.IO;
using System.Text.Json;
using LoomX.Configuration;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class OverviewGraphContractTests
{
    [Fact]
    public void EdgeKeyIncludesProviderToDisambiguateSameModelIds()
    {
        Assert.Equal("openai|provider-a|shared-model", OverviewGraphEdgeKey.Create("openai", "provider-a", "shared-model"));
        Assert.NotEqual(
            OverviewGraphEdgeKey.Create("openai", "provider-a", "shared-model"),
            OverviewGraphEdgeKey.Create("openai", "provider-b", "shared-model"));
    }

    [Fact]
    public void OverviewGraphHostUsesBaseUriNavigationForLocalModules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "OverviewGraphHost.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("var assetDirectory = Path.GetDirectoryName(Path.GetFullPath(htmlPath))!", source, StringComparison.Ordinal);
        Assert.Contains("GetFreeLoopbackPort", source, StringComparison.Ordinal);
        Assert.Contains("assetServer.Prefixes.Add($\"http://127.0.0.1:{port}/\")", source, StringComparison.Ordinal);
        Assert.Contains("webView.Navigate(pageUri)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("webView.Source =", source, StringComparison.Ordinal);
        Assert.Contains("PendingTelemetryLimit", source, StringComparison.Ordinal);
        Assert.Contains("pendingTelemetry", source, StringComparison.Ordinal);
        Assert.Contains("FlushPendingTelemetryAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewLayoutUsesExpandedGraphAndSingleGatewayToggle()
    {
        var source = ReadDesktopFile("Views", "OverviewView.axaml");

        Assert.Contains("<Grid RowDefinitions=\"*,Auto\" RowSpacing=\"16\">", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">", source, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("网关实时拓扑", source, StringComparison.Ordinal);
        Assert.DoesNotContain("metric-card", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopCommand", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"360\"", source, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding GatewayActionLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ToggleGatewayCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"刷新\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewUsesNativeRuntimeGraphControl()
    {
        var source = ReadDesktopFile("Views", "OverviewView.axaml");

        Assert.Contains("xmlns:nodegraph=\"using:LoomX.NodeGraph\"", source, StringComparison.Ordinal);
        Assert.Contains("<nodegraph:RuntimeGraphControl", source, StringComparison.Ordinal);
        Assert.Contains("Snapshot=\"{Binding GraphSnapshot}\"", source, StringComparison.Ordinal);
        Assert.Contains("滚轮缩放 · 拖动平移 · Fit 重置视野", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"适合视图\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Endpoint\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<NativeWebView", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewProvidesEndpointNavigationButtons()
    {
        var source = ReadDesktopFile("Views", "OverviewView.axaml");
        var codeBehind = ReadDesktopFile("Views", "OverviewView.axaml.cs");

        Assert.Contains("ItemsSource=\"{Binding Endpoints}\"", source, StringComparison.Ordinal);
        Assert.Contains("<ItemsPanelTemplate><Grid/></ItemsPanelTemplate>", source, StringComparison.Ordinal);
        Assert.Contains("Snapshot=\"{Binding GraphSnapshot}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsGraphVisible}\"", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"FocusEndpoint_OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("Button.graph-endpoint-link:pointerover", source, StringComparison.Ordinal);
        Assert.Contains("TextDecorations=\"Underline\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GraphStatus", source, StringComparison.Ordinal);
        Assert.Contains("viewModel.SelectEndpoint(endpoint)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FindActiveGraph()?.FitToView()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewWebHudContainsMetricsWithoutActiveEdgeLegend()
    {
        var source = ReadDesktopFile("Assets", "Overview", "index.html");

        Assert.Contains("id=\"active\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"throughput\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"p95\"", source, StringComparison.Ordinal);
        Assert.Contains("window.applyMetrics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("活跃边", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"meta\"", source, StringComparison.Ordinal);
        Assert.Contains("function updateLabelScale", source, StringComparison.Ordinal);
        Assert.Contains("function setCameraState", source, StringComparison.Ordinal);
        Assert.Contains("cameraDistance+e.deltaY*.012", source, StringComparison.Ordinal);
        Assert.Contains("combos", source, StringComparison.Ordinal);
        Assert.Contains("providers", source, StringComparison.Ordinal);
        Assert.Contains("comboId:edge.comboId", source, StringComparison.Ordinal);
        Assert.Contains("function escapeHtml", source, StringComparison.Ordinal);
        Assert.Contains("getBoundingClientRect", source, StringComparison.Ordinal);
        Assert.Contains("routeBindings", source, StringComparison.Ordinal);
        Assert.Contains("if(edge.type==='route')", source, StringComparison.Ordinal);
        Assert.Contains("function providerContainer", source, StringComparison.Ordinal);
        Assert.Contains("new THREE.EdgesGeometry(new THREE.BoxGeometry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("makeEdge(edge,from,to,key)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TopologyProjectionKeepsProviderWithoutModelAndExposesCombos()
    {
        var config = new ResolvedAppConfig
        {
            GatewayEndpoints =
            [
                new ResolvedGatewayEndpointConfig
                {
                    Key = "openai",
                    PublicPath = "/v1",
                    Enabled = true,
                    Combos =
                    [
                        new ResolvedGatewayComboConfig
                        {
                            Name = "coding",
                            Enabled = true,
                            Routes =
                            [
                                new ResolvedGatewayRouteConfig
                                {
                                    Enabled = true,
                                    Model = new ResolvedModelConfig
                                    {
                                        ModelId = "m1", OllamaModelName = "m1", DisplayName = "模型一", ProviderId = "provider-a",
                                        BaseUrl = "https://example.invalid", ApiKey = "", AnthropicModel = "m1"
                                    }
                                }
                            ]
                        }
                    ]
                }
            ],
            Models =
            [new ResolvedModelConfig
            {
                ModelId = "m1", OllamaModelName = "m1", DisplayName = "模型一", ProviderId = "provider-a",
                BaseUrl = "https://example.invalid", ApiKey = "", AnthropicModel = "m1"
            }]
        };
        var providers = new List<ProviderResponse>
        {
            new(Guid.NewGuid(), "provider-a", "Provider A", "https://example.invalid", "openai", true, false, false, 1, "{}", []),
            new(Guid.NewGuid(), "provider-empty", "空 Provider", "https://example.invalid", "openai", true, false, false, 0, "{}", [])
        };

        using var document = JsonDocument.Parse(OverviewViewModel.CreateTopologyJson(config, providers));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("endpoints").GetArrayLength());
        Assert.Equal(1, root.GetProperty("combos").GetArrayLength());
        Assert.Equal(2, root.GetProperty("providers").GetArrayLength());
        Assert.Equal(1, root.GetProperty("models").GetArrayLength());
        Assert.Contains(root.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("type").GetString() == "endpoint-combo");
        Assert.Contains(root.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("type").GetString() == "combo-provider");
        Assert.Contains(root.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("type").GetString() == "provider-model");
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
