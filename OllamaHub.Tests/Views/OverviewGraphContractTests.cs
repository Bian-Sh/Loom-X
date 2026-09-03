using System.IO;
using OllamaHub.Desktop.ViewModels;
using Xunit;

namespace OllamaHub.Tests.Views;

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
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "OverviewGraphHost.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("var assetDirectory = Path.GetDirectoryName(Path.GetFullPath(htmlPath))!", source, StringComparison.Ordinal);
        Assert.Contains("GetFreeLoopbackPort", source, StringComparison.Ordinal);
        Assert.Contains("assetServer.Prefixes.Add($\"http://127.0.0.1:{port}/\")", source, StringComparison.Ordinal);
        Assert.Contains("webView.Navigate(pageUri)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("webView.Source =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewLayoutUsesExpandedGraphAndSingleGatewayToggle()
    {
        var source = ReadDesktopFile("Views", "OverviewView.axaml");

        Assert.DoesNotContain("网关实时拓扑", source, StringComparison.Ordinal);
        Assert.DoesNotContain("metric-card", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopCommand", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"360\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding GatewayActionLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ToggleGatewayCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"刷新\"", source, StringComparison.Ordinal);
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
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
