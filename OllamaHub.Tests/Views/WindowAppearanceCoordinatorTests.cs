using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OllamaHub.Desktop;
using OllamaHub.Desktop.Services;
using OllamaHub.Desktop.Views;
using Xunit;

namespace OllamaHub.Tests.Views;

[Collection("Avalonia UI")]
public sealed class WindowAppearanceCoordinatorTests
{
    [Fact]
    public void PopupAndDialogSurfacesBecomeOpaqueWhenTransparencyIsDisabled()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);

        window.ApplyAppearance(false, 20, 0, "blur");

        Assert.Equal(255, Assert.IsType<SolidColorBrush>(dictionary["DialogBackgroundBrush"]).Color.A);
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(dictionary["PopupBackgroundBrush"]).Color.A);
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(window.Background).Color.A);

        window.ApplyAppearance(true, 20, 0, "blur");

        Assert.Equal(0, Assert.IsType<SolidColorBrush>(dictionary["DialogBackgroundBrush"]).Color.A);
        Assert.Equal(0, Assert.IsType<SolidColorBrush>(dictionary["PopupBackgroundBrush"]).Color.A);
        Assert.Same(Brushes.Transparent, window.Background);

        window.ApplyAppearance(true, 20, 200, "blur");

        Assert.InRange(Assert.IsType<SolidColorBrush>(dictionary["DialogBackgroundBrush"]).Color.A, 1, 254);
        Assert.InRange(Assert.IsType<SolidColorBrush>(dictionary["PopupBackgroundBrush"]).Color.A, 1, 254);
    }

    [Fact]
    public void AppliedSecondaryWindowTracksLaterAppearanceChanges()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        var dialog = new GlassDialogWindow();

        window.ApplyAppearance(true, 86, 200, "mica");
        window.AppearanceCoordinator.ApplyTo(dialog);
        Assert.Same(Brushes.Transparent, dialog.Background);
        Assert.Equal(WindowTransparencyLevel.AcrylicBlur, dialog.TransparencyLevelHint[0]);

        window.ApplyAppearance(false, 86, 200, "acrylic");

        Assert.Equal(255, Assert.IsType<SolidColorBrush>(dialog.Background).Color.A);
        Assert.Equal(WindowTransparencyLevel.AcrylicBlur, dialog.TransparencyLevelHint[0]);
    }

    [Fact]
    public void CoordinatorNormalizesAppearanceSnapshotAndRaisesChange()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        WindowAppearanceSnapshot? changed = null;
        window.AppearanceCoordinator.AppearanceChanged += (_, args) => changed = args.Snapshot;

        window.ApplyAppearance(true, -10, 1000, "unknown");

        Assert.NotNull(changed);
        Assert.Equal(new WindowAppearanceSnapshot(true, 0, 200, "acrylic"), changed);
        Assert.Equal(changed, window.AppearanceCoordinator.Current);
    }

    private static ResourceDictionary LoadVisualTokens() => Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(
        new Uri("avares://OllamaHub.Desktop/Styles/VisualTokens.axaml")));

    private static void EnsureAvaloniaSetup()
    {
        AvaloniaTestBootstrap.Ensure();
    }
}
