using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

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
        Assert.Contains("ColumnDefinitions=\"*,Auto,42,42,42\"", source, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0\"", source, StringComparison.Ordinal);
        Assert.Contains("StrokeThickness=\"0.8\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StrokeThickness=\"1.1\"", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"12\" Height=\"12\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"12\" Height=\"1\"", source, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"104,*\"", source, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"26\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新入口位于最小化按钮左侧并支持悬停与焦点展开()
    {
        var source = ReadDesktopFile("MainWindow.axaml");
        var entryStart = source.IndexOf("x:Name=\"updateEntryButton\"", StringComparison.Ordinal);
        var minimizeStart = source.IndexOf("Click=\"MinimizeButton_OnClick\"", StringComparison.Ordinal);

        Assert.Contains("ColumnDefinitions=\"*,Auto,42,42,42\"", source, StringComparison.Ordinal);
        Assert.True(entryStart >= 0 && minimizeStart > entryStart, "更新入口必须位于最小化按钮左侧。");
        Assert.Contains("Grid.Column=\"1\"", source[entryStart..minimizeStart], StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsUpdateEntryVisible}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Update.ToggleDialogCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding Update.UpdateEntryHint}\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Update.UpdateEntryHint}\"", source, StringComparison.Ordinal);
        Assert.Contains("Height=\"32\" MinWidth=\"32\"", source, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Button.update-entry\"", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"32\" />", source, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Button.update-entry:pointerover\"", source, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Button.update-entry:focus\"", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"NaN\" />", source, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Border.update-entry-text\"", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MaxWidth\" Value=\"0\" />", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0\" />", source, StringComparison.Ordinal);
        Assert.Contains("Button.update-entry:pointerover Border.update-entry-text", source, StringComparison.Ordinal);
        Assert.Contains("Button.update-entry:focus Border.update-entry-text", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MaxWidth\" Value=\"200\" />", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"1\" />", source, StringComparison.Ordinal);
        Assert.Contains("<DoubleTransition Property=\"MaxWidth\"", source, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"28\"", source, StringComparison.Ordinal);
        Assert.Contains("<DoubleTransition Property=\"Opacity\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource AccentSoftBrush}\" IsVisible=\"{Binding Update.IsReady}\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource DangerSoftBrush}\" IsVisible=\"{Binding Update.IsError}\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource UpdateEntryActiveBrush}\" IsVisible=\"{Binding Update.IsPreparing}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsPreparing}\">", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsReady}\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"update-entry-arrow\"", source, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Path.update-entry-arrow\"", source, StringComparison.Ordinal);
        Assert.Contains("IterationCount=\"Infinite\"", source, StringComparison.Ordinal);
        Assert.Contains("TranslateTransform.Y", source, StringComparison.Ordinal);
        Assert.Contains("M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 侧栏底部仅显示实际版本与实时网关状态点()
    {
        var source = ReadDesktopFile("MainWindow.axaml");
        var footerStart = source.IndexOf("x:Name=\"sidebarRuntimeStatus\"", StringComparison.Ordinal);
        var footerEnd = footerStart >= 0 ? source.IndexOf("</Border>", footerStart, StringComparison.Ordinal) : -1;

        Assert.True(footerStart >= 0 && footerEnd > footerStart, "找不到侧栏运行状态区域。");
        var footer = source[footerStart..footerEnd];

        Assert.Contains("Text=\"{Binding VersionLabel}\"", footer, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding GatewayStatusText}\"", footer, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding GatewayStatusText}\"", footer, StringComparison.Ordinal);
        Assert.Contains("Fill=\"{DynamicResource SuccessBrush}\" IsVisible=\"{Binding IsGatewayRunning}\"", footer, StringComparison.Ordinal);
        Assert.Contains("Fill=\"{DynamicResource WarningBrush}\" IsVisible=\"{Binding IsGatewayTransitioning}\"", footer, StringComparison.Ordinal);
        Assert.Contains("Fill=\"{DynamicResource DangerBrush}\" IsVisible=\"{Binding IsGatewayFailed}\"", footer, StringComparison.Ordinal);
        Assert.Contains("Fill=\"{DynamicResource TextTertiaryBrush}\" IsVisible=\"{Binding IsGatewayStopped}\"", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("sidebar.service", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("sidebar.version", footer, StringComparison.Ordinal);

        var viewModelSource = ReadDesktopFile("ViewModels", "MainWindowViewModel.cs");
        Assert.Contains("public string VersionLabel => AppVersion.Label;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("gatewayService.StateChanged += OnGatewayStateChanged", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("gatewayService.StateChanged -= OnGatewayStateChanged", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(GatewayStatusText))", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsGatewayTransitioning))", viewModelSource, StringComparison.Ordinal);
    }
    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
