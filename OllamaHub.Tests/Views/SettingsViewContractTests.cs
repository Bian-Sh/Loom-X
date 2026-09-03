using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OllamaHub.Desktop;
using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

[Collection("Avalonia UI")]
public sealed class SettingsViewContractTests
{
    [Fact]
    public void AppearanceValuesUseIntegerSlidersWithStableReadouts()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");

        Assert.Contains("<Slider Value=\"{Binding TransparencyOpacity, Mode=TwoWay}\" Minimum=\"0\" Maximum=\"100\" TickFrequency=\"1\" IsSnapToTickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("<Slider Value=\"{Binding BlurAmount, Mode=TwoWay}\" Minimum=\"0\" Maximum=\"200\" TickFrequency=\"1\" IsSnapToTickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"透明度\"", source, StringComparison.Ordinal);
        Assert.Contains("数值越低越透明，数值越高越不透明。", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"颗粒晕开度（Blur）\"", source, StringComparison.Ordinal);
        Assert.Contains("数值越低越通透，数值越高越模糊（0 到 200）。", source, StringComparison.Ordinal);
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
        var servicePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub", "Configuration", "ConfigurationManagementService.cs");
        var serviceSource = File.ReadAllText(servicePath);

        Assert.Contains("Math.Clamp(opacity, 0, 100)", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyOpacity is < 0 or > 100", serviceSource, StringComparison.Ordinal);

        var alphaAtZero = MainWindow.CalculateBrushAlpha(230, 0);
        var alphaAtOne = MainWindow.CalculateBrushAlpha(230, 1);
        var alphaAtFour = MainWindow.CalculateBrushAlpha(230, 4);
        var alphaAtTen = MainWindow.CalculateBrushAlpha(230, 10);
        var alphaAtHundred = MainWindow.CalculateBrushAlpha(230, 100);

        Assert.Equal(0, alphaAtZero);
        Assert.True(alphaAtOne > alphaAtZero);
        Assert.True(alphaAtFour > alphaAtOne);
        Assert.True(alphaAtTen > alphaAtFour);
        Assert.True(alphaAtHundred > alphaAtTen);
        Assert.Equal(0, MainWindow.CalculateOpacityFactor(0), 3);
        Assert.Equal(1, MainWindow.CalculateOpacityFactor(100), 3);
    }

    [Fact]
    public void TransparencyOpacityControlsSurfaceAlphaWithoutDependingOnBlur()
    {
        var lowBlur = MainWindow.CalculateBlurTintFactor(0);
        var highBlur = MainWindow.CalculateBlurTintFactor(200);

        Assert.Equal(0, MainWindow.CalculateBrushAlpha(230, 0, lowBlur));
        Assert.Equal(230, MainWindow.CalculateBrushAlpha(230, 100, lowBlur));
        Assert.Equal(
            MainWindow.CalculateBrushAlpha(230, 50, lowBlur),
            MainWindow.CalculateBrushAlpha(230, 50, highBlur));
    }

    [Fact]
    public void AppearancePipelineKeepsOneMaterialPriorityAcrossTheBlurRange()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");

        Assert.Contains("WindowTransparencyLevel.Transparent", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyLevelHint = BuildTransparencyLevels(blurAmount);", windowSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(blurAmount, MinimumBlurAmount, MaximumBlurAmount)", windowSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(blurAmount, MinimumBlurAmount, MaximumBlurAmount) / (double)MaximumBlurAmount", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparentBlurThreshold", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[WindowTransparencyLevel.Transparent, WindowTransparencyLevel.AcrylicBlur]", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowTransparencyLevel.Mica", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowTransparencyLevel.Blur", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparencyLevelHint = !enabled", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[WindowTransparencyLevel.None]", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BlurOverlayChangesContinuouslyWithoutChangingMaterialPriority()
    {
        var lowBlur = MainWindow.CalculateBlurTintFactor(0);
        var baselineBlur = MainWindow.CalculateBlurTintFactor(100);
        var highBlur = MainWindow.CalculateBlurTintFactor(200);

        Assert.Equal(0, lowBlur, 3);
        Assert.Equal(0.5, baselineBlur, 3);
        Assert.Equal(1, highBlur, 3);
        Assert.True(baselineBlur > lowBlur);
        Assert.True(highBlur > baselineBlur);
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels(0));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels(200));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels(99));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels(100));
        Assert.Equal(0, MainWindow.CalculateBlurOverlayAlpha(86, 0));
        Assert.True(MainWindow.CalculateBlurOverlayAlpha(86, 100) > MainWindow.CalculateBlurOverlayAlpha(86, 0));
        Assert.True(MainWindow.CalculateBlurOverlayAlpha(86, 200) > MainWindow.CalculateBlurOverlayAlpha(86, 100));
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
            new Uri("avares://OllamaHub.Desktop/Styles/VisualTokens.axaml")));

        var brush = Assert.IsType<SolidColorBrush>(dictionary["WindowBackgroundBrush"]);
        Assert.IsType<SolidColorBrush>(dictionary["BlurOverlayBrush"]);
        var originalColor = brush.Color;
        brush.Color = Color.FromArgb(12, originalColor.R, originalColor.G, originalColor.B);

        Assert.Equal(12, brush.Color.A);
    }

    [Fact]
    public void ApplyAppearanceChangesRuntimeBrushesForDifferentOpacityAndBlurValues()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(
            new Uri("avares://OllamaHub.Desktop/Styles/VisualTokens.axaml")));
        window.Resources.MergedDictionaries.Add(dictionary);

        window.ApplyAppearance(true, 0, 200, "acrylic");
        var transparentSurfaceAlpha = Assert.IsType<SolidColorBrush>(dictionary["WindowBackgroundBrush"]).Color.A;
        var transparentBlurAlpha = Assert.IsType<SolidColorBrush>(dictionary["BlurOverlayBrush"]).Color.A;

        window.ApplyAppearance(true, 100, 0, "mica");
        var opaqueSurfaceAlpha = Assert.IsType<SolidColorBrush>(dictionary["WindowBackgroundBrush"]).Color.A;
        var clearBlurAlpha = Assert.IsType<SolidColorBrush>(dictionary["BlurOverlayBrush"]).Color.A;

        Assert.Equal(0, transparentSurfaceAlpha);
        Assert.Equal(0, transparentBlurAlpha);
        Assert.Equal(230, opaqueSurfaceAlpha);
        Assert.Equal(0, clearBlurAlpha);

        window.ApplyAppearance(true, 100, 200, "acrylic");

        Assert.True(Assert.IsType<SolidColorBrush>(dictionary["BlurOverlayBrush"]).Color.A > clearBlurAlpha);
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
        Assert.True(app.TryGetResource("WindowBackgroundBrush", null, out var resource));
        var brush = Assert.IsType<SolidColorBrush>(resource);
        var originalAlpha = brush.Color.A;

        var window = new MainWindow();
        window.ApplyAppearance(true, 20, 0, "acrylic");

        Assert.NotEqual(originalAlpha, brush.Color.A);
    }

    private static void EnsureAvaloniaSetup()
    {
        AvaloniaTestBootstrap.Ensure();
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
