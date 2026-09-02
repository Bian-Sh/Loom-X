using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class MainWindowChromeContractTests
{
    [Fact]
    public void SystemButtonsUseCompactTransparentChromeWithThinGlyphs()
    {
        var source = ReadDesktopFile("MainWindow.axaml");
        var styleStart = source.IndexOf("<Style Selector=\"Button.window-control\">", StringComparison.Ordinal);
        var styleEnd = source.IndexOf("</Style>", styleStart, StringComparison.Ordinal);

        Assert.True(styleStart >= 0 && styleEnd > styleStart, "找不到系统按钮基础样式。");
        var baseStyle = source[styleStart..styleEnd];

        Assert.Contains("<Setter Property=\"Width\" Value=\"46\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"32\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"Transparent\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("StrokeThickness=\"1.2\"", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"14\" Height=\"14\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"14\" Height=\"1\"", source, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
