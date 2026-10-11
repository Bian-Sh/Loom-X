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
    public void ManifestDeclaresCardBodyAndInteractiveDetailBody()
    {
        var root = Path.Combine(
            RepositoryRoot(), "plugins", "LoomX.CredentialProtection");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plugin.manifest.json")));
        var contributions = document.RootElement.GetProperty("ui").GetProperty("contributions");

        var card = Assert.Single(contributions.EnumerateArray(), item => item.GetProperty("slot").GetString() == "card-body");
        Assert.Equal("observability", card.GetProperty("id").GetString());

        var detail = Assert.Single(contributions.EnumerateArray(), item => item.GetProperty("slot").GetString() == "detail-body");
        Assert.Equal("settings", detail.GetProperty("id").GetString());
        Assert.True(detail.GetProperty("interactive").GetBoolean());
    }

    private static string FlattenText(PluginUiNode node) => node switch
    {
        PluginUiTextNode text => text.Text,
        PluginUiStackNode stack => string.Join("\n", stack.Children.Select(FlattenText)),
        PluginUiGridNode grid => string.Join("\n", grid.Children.Select(item => FlattenText(item.Content))),
        PluginUiSurfaceNode surface => FlattenText(surface.Content),
        _ => string.Empty,
    };

    /// <summary>向上找解决方案根目录，避免依赖固定的 bin 层数。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}