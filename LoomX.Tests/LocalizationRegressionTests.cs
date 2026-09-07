using System.Globalization;
using System.ComponentModel;
using System.Text.RegularExpressions;
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
    public void LanguageOptionsKeepStableNativeNamesAcrossUiCultures()
    {
        var zhCn = new CultureInfo("zh-CN");
        var enUs = new CultureInfo("en-US");

        Assert.Equal("简体中文", SettingsViewModel.LanguageOptions[0].GetDisplayName(zhCn));
        Assert.Equal("简体中文", SettingsViewModel.LanguageOptions[0].GetDisplayName(enUs));
        Assert.Equal("English", SettingsViewModel.LanguageOptions[1].GetDisplayName(zhCn));
        Assert.Equal("English", SettingsViewModel.LanguageOptions[1].GetDisplayName(enUs));
        Assert.Equal("日本語", SettingsViewModel.LanguageOptions[2].GetDisplayName(zhCn));
        Assert.Equal("日本語", SettingsViewModel.LanguageOptions[2].GetDisplayName(enUs));
    }

    [Fact]
    public void ResourceLookupFollowsLocaleServiceCultureInsteadOfThreadCulture()
    {
        var previousLocale = LocaleService.CurrentCulture.Name;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            LocaleService.SetCulture("en-US");
            Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

            Assert.Equal("Local gateway", ResourceLookup.Resolve("overview.gateway.label"));
        }
        finally
        {
            LocaleService.SetCulture(previousLocale);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void LocaleBindingResolvesUsingCultureChangeEvent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Localization", "Locale.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("ResourceLookup.Resolve(Key, culture)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProxyStatusUsesAsciiPunctuation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "SettingsViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("：{ProxyHost}", source, StringComparison.Ordinal);
        Assert.Contains("}: {ProxyHost}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingOptionKeepsStableValueAndNotifiesDisplayNameChanges()
    {
        var option = SettingsViewModel.ProxyModeOptions.Single(item => item.Value == "direct");
        var displayNameNotifications = 0;
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SettingOption.DisplayName)) displayNameNotifications++;
        }

        option.PropertyChanged += OnPropertyChanged;
        try
        {
            Assert.Equal("直连", option.GetDisplayName(new CultureInfo("zh-CN")));
            Assert.Equal("Direct", option.GetDisplayName(new CultureInfo("en-US")));
            Assert.Equal("direct", option.Value);
            option.NotifyDisplayNameChanged();
            Assert.Equal(1, displayNameNotifications);

            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "SettingsViewModel.cs");
            var source = File.ReadAllText(path);
            Assert.Contains("LocaleService.CultureChanged += OnCultureChanged", source, StringComparison.Ordinal);
        }
        finally
        {
            option.PropertyChanged -= OnPropertyChanged;
        }
    }

    [Fact]
    public void SettingsDropdownsBindDisplayNameForItemsAndSelectionBox()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "SettingsView.axaml");
        var source = File.ReadAllText(path);

        Assert.Equal(4, Regex.Matches(source, @"(?<!SelectionBox)ItemTemplate=""\{StaticResource SettingsOptionTemplate\}""").Count);
        Assert.Equal(4, source.Split("SelectionBoxItemTemplate=\"{StaticResource SettingsOptionTemplate}\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("x:Key=\"SettingsOptionTemplate\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayName}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityDropdownsBindDisplayNameForItemsAndSelectionBox()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ActivityView.axaml");
        var source = File.ReadAllText(path);

        Assert.Equal(2, Regex.Matches(source, @"(?<!SelectionBox)ItemTemplate=""\{StaticResource ActivityFilterTemplate\}""").Count);
        Assert.Equal(2, source.Split("SelectionBoxItemTemplate=\"{StaticResource ActivityFilterTemplate}\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("x:Key=\"ActivityFilterTemplate\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayName}\"", source, StringComparison.Ordinal);
    }
}
