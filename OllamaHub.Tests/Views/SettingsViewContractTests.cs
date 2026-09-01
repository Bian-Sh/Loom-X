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

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
