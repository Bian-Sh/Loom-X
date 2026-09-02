using Avalonia.Controls;
using Avalonia.Media;
using OllamaHub.Desktop;
using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class SettingsViewContractTests
{
    [Fact]
    public void AppearanceValuesUseIntegerSlidersWithStableReadouts()
    {
        var source = ReadDesktopFile("Views", "SettingsView.axaml");

        Assert.Contains("<Slider Value=\"{Binding TransparencyOpacity, Mode=TwoWay}\" Minimum=\"0\" Maximum=\"100\" TickFrequency=\"1\" IsSnapToTickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("<Slider Value=\"{Binding BlurAmount, Mode=TwoWay}\" Minimum=\"0\" Maximum=\"64\" TickFrequency=\"1\" IsSnapToTickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TransparencyOpacity, StringFormat='{}{0}%'}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BlurAmount}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<NumericUpDown Grid.Column=\"1\" Value=\"{Binding TransparencyOpacity}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<NumericUpDown Grid.Column=\"1\" Value=\"{Binding BlurAmount}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearancePipelineUsesAContinuousZeroToHundredOpacityRange()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");
        var servicePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub", "Configuration", "ConfigurationManagementService.cs");
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
    public void TransparencyOpacityZeroKeepsAVisibleBaselineWithoutAJumpAtLowValues()
    {
        var tint = MainWindow.CalculateBlurTintFactor(24);
        var alphaAtZero = MainWindow.CalculateBrushAlpha(230, 0, tint);
        var alphaAtFour = MainWindow.CalculateBrushAlpha(230, 4, tint);

        Assert.InRange(alphaAtZero, 1, 255);
        Assert.InRange(alphaAtFour - alphaAtZero, 0, 12);
    }

    [Fact]
    public void AppearancePipelineKeepsMaterialBeforeTransparentFallback()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");

        Assert.Contains("WindowTransparencyLevel.Transparent", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyLevelHint = BuildTransparencyLevels(algorithm);", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.Blur, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("0.35 + (Math.Clamp(blurAmount, 0, 64) / 64d * 0.65)", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparencyLevelHint = !enabled", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[WindowTransparencyLevel.None]", windowSource, StringComparison.Ordinal);
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

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
