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
    public void AppearancePipelineSupportsZeroOpacity()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");
        var servicePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub", "Configuration", "ConfigurationManagementService.cs");
        var serviceSource = File.ReadAllText(servicePath);

        Assert.Contains("Math.Clamp(opacity, 0, 100)", windowSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(Math.Round(baseAlpha * (opacity / 86d) * blurFactor), 0, 255)", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyOpacity is < 0 or > 100", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearancePipelineKeepsTransparentFallbackAndDoesNotDisableItAtRuntime()
    {
        var windowSource = ReadDesktopFile("MainWindow.axaml.cs");

        Assert.Contains("WindowTransparencyLevel.Transparent", windowSource, StringComparison.Ordinal);
        Assert.Contains("TransparencyLevelHint = BuildTransparencyLevels(algorithm);", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.Blur, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("[WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]", windowSource, StringComparison.Ordinal);
        Assert.Contains("0.35 + (blurAmount / 64d * 0.65)", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparencyLevelHint = !enabled", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[WindowTransparencyLevel.None]", windowSource, StringComparison.Ordinal);
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
    public void TransparencyAlgorithmPrefersTheSelectedMaterialBeforeFallbacks()
    {
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels(" acrylic "));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels("BLUR"));
        Assert.Equal(
            new[]
            {
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.Transparent
            },
            MainWindow.BuildTransparencyLevels("mica"));
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
