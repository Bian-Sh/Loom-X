using System.Xml.Linq;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class SearchBoxContractTests
{
    private static readonly IReadOnlyDictionary<string, string[]> SearchWatermarksByView =
        new Dictionary<string, string[]>
        {
            ["ActivityView.axaml"] = ["activity.search.placeholder"],
            ["AssistantView.axaml"] = ["assistant.model.search.watermark"],
            ["ConsoleView.axaml"] = ["console.search.placeholder"],
            ["GatewayView.axaml"] = ["gateway.combo.search.watermark"],
            ["ProvidersView.axaml"] = ["providers.search.watermark", "providers.models.search.watermark"]
        };

    [Fact]
    public void AllSearchInputsUseSharedEmbeddedClearButtonStyle()
    {
        var app = LoadDesktopDocument("App.axaml");
        var embeddedClearStyle = app.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("Selector") == "TextBox.search:not(:empty)");

        var innerRightContent = embeddedClearStyle.Descendants()
            .Single(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "InnerRightContent");
        var clearButton = innerRightContent.Descendants()
            .Single(element => element.Name.LocalName == "Button");

        Assert.Contains("search-clear", ((string?)clearButton.Attribute("Classes") ?? string.Empty).Split(' '));
        Assert.Equal("{Binding $parent[TextBox].Clear}", (string?)clearButton.Attribute("Command"));

        var searchInputCount = 0;
        foreach (var (viewFile, watermarks) in SearchWatermarksByView)
        {
            var view = LoadDesktopDocument("Views", viewFile);
            foreach (var watermark in watermarks)
            {
                var searchInput = view.Descendants()
                    .Single(element => element.Name.LocalName == "TextBox"
                        && ((string?)element.Attribute("Watermark"))?.Contains(watermark, StringComparison.Ordinal) == true);

                Assert.Contains("search", ((string?)searchInput.Attribute("Classes") ?? string.Empty).Split(' '));
                searchInputCount++;
            }
        }

        Assert.Equal(6, searchInputCount);
    }

    private static XDocument LoadDesktopDocument(params string[] relativePath)
    {
        var segments = new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX" }
            .Concat(relativePath)
            .ToArray();
        return XDocument.Load(Path.GetFullPath(Path.Combine(segments)));
    }
}