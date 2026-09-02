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
        var chromeStyleStart = source.IndexOf("<Style Selector=\"Border.window-chrome\">", StringComparison.Ordinal);
        var chromeStyleEnd = source.IndexOf("</Style>", chromeStyleStart, StringComparison.Ordinal);

        Assert.True(styleStart >= 0 && styleEnd > styleStart, "找不到系统按钮基础样式。");
        Assert.True(chromeStyleStart >= 0 && chromeStyleEnd > chromeStyleStart, "找不到标题栏容器样式。");
        var baseStyle = source[styleStart..styleEnd];
        var chromeStyle = source[chromeStyleStart..chromeStyleEnd];

        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", chromeStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"42\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"32\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"Transparent\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Top\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", baseStyle, StringComparison.Ordinal);
        Assert.Contains("Height=\"32\" VerticalAlignment=\"Top\"", source, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"*,42,42,42\"", source, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0\"", source, StringComparison.Ordinal);
        Assert.Contains("StrokeThickness=\"1.1\"", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"12\" Height=\"12\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"12\" Height=\"1\"", source, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
