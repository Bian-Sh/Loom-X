using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class GlassDialogWindowContractTests
{
    [Fact]
    public void DialogUsesCustomChromeAndDynamicGlassResources()
    {
        var source = ReadDesktopFile("Views", "GlassDialogWindow.axaml");
        var code = ReadDesktopFile("Views", "GlassDialogWindow.axaml.cs");

        Assert.Contains("SystemDecorations=\"None\"", source, StringComparison.Ordinal);
        Assert.Contains("ExtendClientAreaChromeHints=\"NoChrome\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource DialogBackgroundBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"32,*,Auto\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"dialogActionsPresenter\"", source, StringComparison.Ordinal);
        Assert.Contains("Button.dialog-action", source, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"TitleBar_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"10\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window-close", source, StringComparison.Ordinal);
        Assert.Contains("Close(false)", code, StringComparison.Ordinal);
        Assert.Contains("e.Key == Key.Escape", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDeleteFlowUsesGlassDialogAndAppearanceCoordinator()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml.cs");

        Assert.Contains("new GlassDialogWindow", source, StringComparison.Ordinal);
        Assert.Contains("owner.AppearanceCoordinator.ApplyTo(dialog)", source, StringComparison.Ordinal);
        Assert.Contains("dialog.DialogActions = buttons", source, StringComparison.Ordinal);
        Assert.Contains("dialog-action", source, StringComparison.Ordinal);
        Assert.Contains("Title = \"提示\"", source, StringComparison.Ordinal);
        Assert.Contains("ShowDialog<bool>(owner)", source, StringComparison.Ordinal);
        Assert.Contains("dialog-danger", source, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
