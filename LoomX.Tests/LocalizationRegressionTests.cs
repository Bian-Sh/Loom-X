using System.Globalization;
using LoomX.Activity;
using LoomX.Localization;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests;

public sealed class LocalizationRegressionTests
{
    [Fact]
    public void ActivityCultureChangesRebuildExistingItemsWithLocalizedValues()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "ActivityViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("ApplyPage(new ActivityPage(dataStore.ActivityWindow, null, dataStore.ActivityHasMore))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityPassthroughRouteUsesTheCurrentUiLanguage()
    {
        var enUs = new CultureInfo("en-US");

        Assert.Equal("OpenAI passthrough", ActivityItemViewModel.LocalizeRoute("OpenAI 直通", enUs));
        Assert.Equal("Anthropic passthrough", ActivityItemViewModel.LocalizeRoute("Anthropic 直通", enUs));
        Assert.Equal("Ollama passthrough", ActivityItemViewModel.LocalizeRoute("Ollama 直通", enUs));
    }

    [Fact]
    public void ActivityDetailFallbacksDoNotRenderBindingObjects()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ActivityView.axaml");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("FallbackValue={l:Locale", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedModelLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedRequestIdLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedLogSummary}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityFilterOptionsDoNotEmbedChineseLabels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "ActivityViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("activity.filter.status.all", source, StringComparison.Ordinal);
        Assert.Contains("activity.filter.protocol.all", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"全部状态\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"全部入口协议\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOptionsUseLocalizedDisplayNamesAfterCultureSwitch()
    {
        var enUs = new CultureInfo("en-US");

        Assert.Equal("System", SettingsViewModel.ThemeOptions[0].GetDisplayName(enUs));
        Assert.Equal("Direct", SettingsViewModel.ProxyModeOptions[0].GetDisplayName(enUs));
        Assert.Equal("7 days", SettingsViewModel.LogRetentionOptions[0].GetDisplayName(enUs));
    }

    [Fact]
    public void ProxyStatusUsesAsciiPunctuation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "SettingsViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("：{ProxyHost}", source, StringComparison.Ordinal);
        Assert.Contains("}: {ProxyHost}", source, StringComparison.Ordinal);
    }
}
