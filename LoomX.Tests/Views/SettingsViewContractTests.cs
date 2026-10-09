using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using LoomX;
using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class SettingsViewContractTests
{
    [Fact]
    public void EditableSettingsUseImmediateSourceUpdatesAndDirectSaveEvents()
    {
        var viewSource = ReadDesktopFile("Views", "SettingsView.axaml");
        var viewModelSource = ReadDesktopFile("ViewModels", "SettingsViewModel.cs");

        Assert.Contains("Text=\"{Binding ProxyHost, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProxyPassword, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", viewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DebouncedAutoSaver", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim saveLock", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("private void SaveAfterEdit()", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeSelectionAppliesAvaloniaVariantAndProvidesDistinctDarkTokens()
    {
        var appSource = ReadDesktopFile("App.axaml.cs");
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");
        var mainViewModelSource = ReadDesktopFile("ViewModels", "MainWindowViewModel.cs");
        var settingsViewModelSource = ReadDesktopFile("ViewModels", "SettingsViewModel.cs");
        var tokenSource = ReadDesktopFile("Styles", "VisualTokens.axaml");

        Assert.Contains("applyTheme: mainWindow.ApplyTheme", appSource, StringComparison.Ordinal);
        Assert.Contains("public void ApplyTheme(string theme)", windowSource, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant.Dark", windowSource, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant.Light", windowSource, StringComparison.Ordinal);
        Assert.Contains("ThemeVariant.Default", windowSource, StringComparison.Ordinal);
        Assert.Contains("applyTheme?.Invoke(settings.Theme);", mainViewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (!suppressAutoSave) ApplyThemePreview();", settingsViewModelSource, StringComparison.Ordinal);
        Assert.Contains("<ResourceDictionary x:Key=\"Light\">", tokenSource, StringComparison.Ordinal);
        Assert.Contains("<ResourceDictionary x:Key=\"Dark\">", tokenSource, StringComparison.Ordinal);
        Assert.Contains("Color=\"#E6172226\"", tokenSource, StringComparison.Ordinal);
        Assert.Contains("Color=\"#F1F6F7\"", tokenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceValuesUseIntegerSlidersWithStableReadouts()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");

        Assert.Contains("<Slider Value=\"{Binding TransparencyOpacity, Mode=TwoWay}\" Minimum=\"0\" Maximum=\"100\" TickFrequency=\"1\" IsSnapToTickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("<Slider Value=\"{Binding BlurAmount, Mode=TwoWay}\" Minimum=\"0\" Maximum=\"64\" TickFrequency=\"1\" IsSnapToTickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{l:Locale settings.opacity.label}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{l:Locale settings.opacity.hint}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{l:Locale settings.blur.label}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{l:Locale settings.blur.hint}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TransparencyOpacity, StringFormat='{}{0}%'}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BlurAmount}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<NumericUpDown Grid.Column=\"1\" Value=\"{Binding TransparencyOpacity}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<NumericUpDown Grid.Column=\"1\" Value=\"{Binding BlurAmount}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("磨砂算法", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Acrylic（亚克力）", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparencyAlgorithmOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedTransparencyAlgorithm", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBox ItemsSource=\"{Binding TransparencyAlgorithmOptions}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearancePipelineUsesAContinuousZeroToHundredOpacityRange()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");
        var servicePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Configuration", "ConfigurationManagementService.cs");
        var serviceSource = File.ReadAllText(servicePath);

        Assert.Contains("Math.Clamp(opacity, 0, 100)", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyOpacity is < 0 or > 100", serviceSource, StringComparison.Ordinal);

        var tint = MainWindow.CalculateBlurTintFactor(24);
        var alphaAtZero = MainWindow.CalculateBrushAlpha(230, 0, tint);
        var alphaAtOne = MainWindow.CalculateBrushAlpha(230, 1, tint);
        var alphaAtFour = MainWindow.CalculateBrushAlpha(230, 4, tint);
        var alphaAtHundred = MainWindow.CalculateBrushAlpha(230, 100, tint);

        Assert.True(alphaAtZero > 0);
        Assert.True(alphaAtOne > alphaAtZero);
        Assert.True(alphaAtFour > alphaAtOne);
        Assert.True(alphaAtHundred > alphaAtFour);
        Assert.Equal(0.16, MainWindow.CalculateOpacityFactor(0), 3);
        Assert.Equal(1, MainWindow.CalculateOpacityFactor(100), 3);
    }

    [Fact]
    public void BlurTintChangesSmoothlyWithoutChangingTheOpacityScale()
    {
        var lowBlur = MainWindow.CalculateBlurTintFactor(0);
        var highBlur = MainWindow.CalculateBlurTintFactor(64);

        Assert.Equal(0.35, lowBlur, 3);
        Assert.Equal(1, highBlur, 3);
        Assert.True(highBlur > lowBlur);
        Assert.True(
            MainWindow.CalculateBrushAlpha(230, 86, highBlur)
            > MainWindow.CalculateBrushAlpha(230, 86, lowBlur));
    }

    [Fact]
    public void AppearancePipelineKeepsOneMaterialPriorityAcrossTheBlurRange()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");

        Assert.Contains("WindowTransparencyLevel.Transparent", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyLevelHint = BuildTransparencyLevels(algorithm);", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("0.35 + (Math.Clamp(blurAmount, 0, 64) / 64d * 0.65)", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparencyLevelHint = !enabled", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[WindowTransparencyLevel.None]", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TransparencyAlgorithmAlwaysUsesAcrylicMaterial()
    {
        var expected = new[]
        {
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent
        };

        Assert.Equal(expected, MainWindow.BuildTransparencyLevels("acrylic"));
        Assert.Equal(expected, MainWindow.BuildTransparencyLevels("blur"));
        Assert.Equal(expected, MainWindow.BuildTransparencyLevels("mica"));
    }

    [Fact]
    public void AppearanceBrushUpdatesKeepTheSharedBrushAndUseItsBaseColor()
    {
        var brush = new SolidColorBrush(Color.FromArgb(230, 213, 228, 233));
        var resources = new ResourceDictionary { ["WindowBackgroundBrush"] = brush };
        var baseColors = new Dictionary<string, Color>(StringComparer.Ordinal);

        Assert.True(AppearanceBrushUpdater.TryApply(resources, "WindowBackgroundBrush", baseColors, 92));
        Assert.Same(brush, resources["WindowBackgroundBrush"]);
        Assert.Equal(Color.FromArgb(92, 213, 228, 233), brush.Color);

        Assert.True(AppearanceBrushUpdater.TryApply(resources, "WindowBackgroundBrush", baseColors, 184));
        Assert.Same(brush, resources["WindowBackgroundBrush"]);
        Assert.Equal(Color.FromArgb(184, 213, 228, 233), brush.Color);
    }

    [Fact]
    public void TransparencyAlgorithmIsFixedToAcrylic()
    {
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels(" acrylic "));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels("BLUR"));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels("mica"));
    }

    [Fact]
    public void LegacyTransparencyAlgorithmValuesAlwaysUseAcrylicLevel()
    {
        var expected = new[]
        {
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent
        };

        Assert.Equal(
            expected,
            MainWindow.BuildTransparencyLevels("mica"));
        Assert.Equal(
            expected,
            MainWindow.BuildTransparencyLevels("blur"));
    }

    [Fact]
    public void VisualTokenResourcesLoadAsMutableSolidColorBrushes()
    {
        EnsureAvaloniaSetup();

        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(
            new Uri("avares://LoomX/Styles/VisualTokens.axaml")));

        Assert.True(dictionary.TryGetResource("WindowBackgroundBrush", ThemeVariant.Light, out var lightResource));
        Assert.True(dictionary.TryGetResource("WindowBackgroundBrush", ThemeVariant.Dark, out var darkResource));
        var lightBrush = Assert.IsType<SolidColorBrush>(lightResource);
        var darkBrush = Assert.IsType<SolidColorBrush>(darkResource);
        var originalColor = lightBrush.Color;
        lightBrush.Color = Color.FromArgb(12, originalColor.R, originalColor.G, originalColor.B);

        Assert.Equal(12, lightBrush.Color.A);
        Assert.NotEqual(lightBrush.Color.R, darkBrush.Color.R);
    }

    [Fact]
    public void ApplyAppearanceChangesRuntimeBrushesForDifferentOpacityAndBlurValues()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        window.ApplyTheme("light");
        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(
            new Uri("avares://LoomX/Styles/VisualTokens.axaml")));
        window.Resources.MergedDictionaries.Add(dictionary);

        window.ApplyAppearance(true, 0, 64, "acrylic");
        Assert.True(dictionary.TryGetResource("WindowBackgroundBrush", ThemeVariant.Light, out var highBlurResource));
        var highBlurSurfaceAlpha = Assert.IsType<SolidColorBrush>(highBlurResource).Color.A;

        window.ApplyAppearance(true, 100, 0, "mica");
        Assert.True(dictionary.TryGetResource("WindowBackgroundBrush", ThemeVariant.Light, out var lowBlurResource));
        var lowBlurSurfaceAlpha = Assert.IsType<SolidColorBrush>(lowBlurResource).Color.A;

        Assert.Equal(MainWindow.CalculateBrushAlpha(230, 0, MainWindow.CalculateBlurTintFactor(64)), highBlurSurfaceAlpha);
        Assert.Equal(MainWindow.CalculateBrushAlpha(230, 100, MainWindow.CalculateBlurTintFactor(0)), lowBlurSurfaceAlpha);
        Assert.True(lowBlurSurfaceAlpha > highBlurSurfaceAlpha);

        Assert.Equal(Brushes.Transparent, window.Background);
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            window.TransparencyLevelHint);
    }

    [Fact]
    public void ApplyAppearanceResolvesBrushesFromApplicationResources()
    {
        EnsureAvaloniaSetup();
        var app = Assert.IsType<App>(Application.Current);
        app.RequestedThemeVariant = ThemeVariant.Light;
        Assert.True(app.TryGetResource("WindowBackgroundBrush", ThemeVariant.Light, out var resource));
        var brush = Assert.IsType<SolidColorBrush>(resource);
        var originalAlpha = brush.Color.A;

        var window = new MainWindow();
        window.ApplyAppearance(true, 20, 0, "acrylic");

        Assert.NotEqual(originalAlpha, brush.Color.A);
    }

    [Fact]
    public void 更新页保留原设置并提供完整版本历史分栏状态()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");
        var appSource = ReadDesktopFile("App.axaml");

        Assert.Contains("<TabControl SelectedIndex=\"{Binding SelectedTabIndex, Mode=TwoWay}\">", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding VersionLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding AutoCheckUpdates}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding UseProxyForUpdates}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CheckUpdateCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Height=\"430\"", source, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"200,*\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.IsRefreshing}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding ReleaseHistory.RefreshCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ReleaseHistory.Releases}\"", source, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding ReleaseHistory.SelectedRelease, Mode=TwoWay}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ReleaseHistory.LoadMoreCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.CanShowLoadMore}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding ReleaseHistory.LoadMoreCommand}\" IsVisible=\"{Binding ReleaseHistory.HasMore}\"", source, StringComparison.Ordinal);
        Assert.Contains("<views:ReleaseNotesView DataContext=\"{Binding ReleaseHistory.Content}\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"release-history-list selection-rail-list\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.release-history-list ListBoxItem\"><Setter Property=\"Cursor\" Value=\"Hand\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"selection-rail-card\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.selection-rail-list ListBoxItem\"", appSource, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"0,0,0,6\"", appSource, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.selection-rail-card\"", appSource, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.selection-rail-list ListBoxItem:selected\"", appSource, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderThickness\" Value=\"3,0,0,0\"", appSource, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"{Binding VersionText}\" FontWeight=\"SemiBold\"/>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"{Binding Release.HtmlUrl}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ReleaseHistory.SelectedRelease.VersionText}\"", source, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding ReleaseHistory.SelectedRelease.Release.HtmlUrl}\"", source, StringComparison.Ordinal);
        Assert.Contains("Cursor=\"Hand\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource AccentBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("TextDecorations=\"Underline\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{l:Locale settings.update.history.open.release.tip}\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"ReleaseVersion_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"{l:Locale update.dialog.open_release}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedReleasePageButton_OnClick", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.IsInitialLoading}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.IsEmpty}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.HasError}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.HasCachedContent}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ReleaseHistory.IsLoadingMore}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 发布页提示保持简洁()
    {
        var zhCn = ReadDesktopFile("Resources", "Strings.resx");
        var zhTw = ReadDesktopFile("Resources", "Strings.zh-TW.resx");
        var enUs = ReadDesktopFile("Resources", "Strings.en-US.resx");
        var jaJp = ReadDesktopFile("Resources", "Strings.ja-JP.resx");

        Assert.Contains("<data name=\"settings.update.history.open.release.tip\"><value>点击前往此版本的发布页</value></data>", zhCn, StringComparison.Ordinal);
        Assert.Contains("<data name=\"settings.update.history.open.release.tip\"><value>點擊前往此版本的發布頁</value></data>", zhTw, StringComparison.Ordinal);
        Assert.Contains("<data name=\"settings.update.history.open.release.tip\"><value>Open this release page</value></data>", enUs, StringComparison.Ordinal);
        Assert.Contains("<data name=\"settings.update.history.open.release.tip\"><value>このバージョンのリリースページを開きます</value></data>", jaJp, StringComparison.Ordinal);
    }

    [Fact]
    public void 检查更新同时刷新版本历史()
    {
        var settingsViewModel = ReadDesktopFile("ViewModels", "SettingsViewModel.cs");
        var releaseHistoryViewModel = ReadDesktopFile("ViewModels", "ReleaseHistoryViewModel.cs");

        Assert.Contains("await ReleaseHistory.RefreshAsync();", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("public Task RefreshAsync()", releaseHistoryViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新页压缩当前版本与历史标题区域()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");

        Assert.Contains("<Border Classes=\"panel\" Padding=\"16,12\">", source, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Spacing=\"9\">", source, StringComparison.Ordinal);
        Assert.Contains("Margin=\"16,8\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新页当前版本与检查按钮使用紧凑对齐布局()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");
        var zhCn = ReadDesktopFile("Resources", "Strings.resx");
        var zhTw = ReadDesktopFile("Resources", "Strings.zh-TW.resx");
        var enUs = ReadDesktopFile("Resources", "Strings.en-US.resx");
        var jaJp = ReadDesktopFile("Resources", "Strings.ja-JP.resx");

        Assert.Contains("<Grid ColumnDefinitions=\"Auto,*\" ColumnSpacing=\"4\">", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{l:Locale settings.update.version.hint}\"", source, StringComparison.Ordinal);
        Assert.Contains("<Grid RowDefinitions=\"Auto,Auto\" ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"12\" RowSpacing=\"9\">", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\" Text=\"{l:Locale settings.update.use.proxy.hint}\" Classes=\"hint\" Margin=\"24,-7,0,0\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.RowSpan=\"2\" Grid.Column=\"1\" Content=\"{l:Locale settings.update.check.button}\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", source, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsNotBusy}\"", source, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Bottom\"", source, StringComparison.Ordinal);
        Assert.Contains("<value>当前版本：</value>", zhCn, StringComparison.Ordinal);
        Assert.Contains("<value>當前版本：</value>", zhTw, StringComparison.Ordinal);
        Assert.Contains("<value>Current version:</value>", enUs, StringComparison.Ordinal);
        Assert.Contains("<value>現在のバージョン：</value>", jaJp, StringComparison.Ordinal);
    }

    [Fact]
    public void 常规设置提供Windows开机自启动并保存到统一设置模型()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");
        var viewModel = ReadDesktopFile("ViewModels", "SettingsViewModel.cs");
        var configurationService = ReadDesktopFile("Configuration", "ConfigurationManagementService.cs");
        var zhCn = ReadDesktopFile("Resources", "Strings.resx");
        var zhTw = ReadDesktopFile("Resources", "Strings.zh-TW.resx");
        var enUs = ReadDesktopFile("Resources", "Strings.en-US.resx");
        var jaJp = ReadDesktopFile("Resources", "Strings.ja-JP.resx");

        Assert.Contains("settings.startup.windows.label", source, StringComparison.Ordinal);
        Assert.Contains("settings.startup.windows.hint", source, StringComparison.Ordinal);
        Assert.Contains(@"IsChecked=""{Binding StartWithWindows, Mode=TwoWay}""", source, StringComparison.Ordinal);

        var startupIndex = source.IndexOf("settings.startup.windows.label", StringComparison.Ordinal);
        var languageIndex = source.IndexOf("settings.language.label", StringComparison.Ordinal);
        var themeIndex = source.IndexOf("settings.theme.label", StringComparison.Ordinal);
        var transparencyIndex = source.IndexOf("settings.transparency.label", StringComparison.Ordinal);
        Assert.True(startupIndex < languageIndex, "开机自启动应位于常规设置第一排");
        Assert.True(languageIndex < themeIndex && themeIndex < transparencyIndex, "语言、主题与透明设置应连续排列");
        Assert.Contains("public bool StartWithWindows", viewModel, StringComparison.Ordinal);
        Assert.Contains("StartWithWindows = settings.StartWithWindows;", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("GatewayRunning", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("GatewayRunning", configurationService.Split("public sealed record AppSettingsResponse", StringSplitOptions.None)[0], StringComparison.Ordinal);
        Assert.Contains("开机时启动 Loom-X", zhCn, StringComparison.Ordinal);
        Assert.Contains("開機時啟動 Loom-X", zhTw, StringComparison.Ordinal);
        Assert.Contains("Start Loom-X when Windows starts", enUs, StringComparison.Ordinal);
        Assert.Contains("Windows の起動時に Loom-X を起動", jaJp, StringComparison.Ordinal);
    }

    private static void EnsureAvaloniaSetup()
    {
        AvaloniaTestBootstrap.Ensure();
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
