using System.Text.Json;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class CredentialProtectionUiTests
{
    [Fact]
    public void PluginDeclaresThreeColumnLocalizedObservabilityCard()
    {
        var plugin = new CredentialProtectionPlugin();
        plugin.Observability.RecordRequest(3);
        plugin.Observability.RecordRequest(0);
        plugin.Observability.RecordRestoredResponse();
        plugin.Observability.RecordError();
        var invalidated = 0;
        plugin.UiInvalidated += (_, args) =>
        {
            Assert.Same(EventArgs.Empty, args);
            invalidated++;
        };

        plugin.Observability.RecordRequest(0);
        var contribution = Assert.Single(plugin.GetUiContributions(new PluginUiContext("zh-CN")));

        Assert.Equal("observability", contribution.Id);
        Assert.Equal(PluginUiSlot.CardBody, contribution.Slot);
        var grid = Assert.IsType<PluginUiGridNode>(contribution.Root);
        Assert.Equal(3, grid.Columns);
        Assert.Equal(3, grid.Children.Count);
        var text = FlattenText(contribution.Root);
        Assert.Contains("已脱敏请求", text);
        Assert.Contains("1/3", text);
        Assert.Contains("累计脱敏词项 3", text);
        Assert.Contains("已还原回复", text);
        Assert.Contains("异常与告警", text);
        Assert.Contains("1", text);
        Assert.Equal(1, invalidated);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialEngine.PlaceholderPrefix, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestDeclaresOnlyCardBodyContribution()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "plugins", "LoomX.CredentialProtection");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plugin.manifest.json")));
        var contributions = document.RootElement.GetProperty("ui").GetProperty("contributions");

        var contribution = Assert.Single(contributions.EnumerateArray());
        Assert.Equal("observability", contribution.GetProperty("id").GetString());
        Assert.Equal("card-body", contribution.GetProperty("slot").GetString());
    }

    private static string FlattenText(PluginUiNode node) => node switch
    {
        PluginUiTextNode text => text.Text,
        PluginUiStackNode stack => string.Join("\n", stack.Children.Select(FlattenText)),
        PluginUiGridNode grid => string.Join("\n", grid.Children.Select(item => FlattenText(item.Content))),
        PluginUiSurfaceNode surface => FlattenText(surface.Content),
        _ => string.Empty,
    };
}