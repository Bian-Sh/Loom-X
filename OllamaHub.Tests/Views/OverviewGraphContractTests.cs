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
}
