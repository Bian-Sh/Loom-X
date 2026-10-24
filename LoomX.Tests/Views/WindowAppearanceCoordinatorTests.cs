using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using LoomX;
using LoomX.Services;
using LoomX.Views;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class WindowAppearanceCoordinatorTests
{
    [Fact]
    public void PopupAndDialogSurfacesBecomeOpaqueWhenTransparencyIsDisabled()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);

        window.ApplyAppearance(false, 20, 0, "blur");

        Assert.Equal(255, GetBrush(dictionary, "DialogBackgroundBrush").Color.A);
        Assert.Equal(255, GetBrush(dictionary, "PopupBackgroundBrush").Color.A);
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(window.Background).Color.A);

        window.ApplyAppearance(true, 20, 0, "blur");

        var lowBlurFactor = MainWindow.CalculateBlurTintFactor(0);
        Assert.Equal(MainWindow.CalculateBrushAlpha(232, 20, lowBlurFactor), GetBrush(dictionary, "DialogBackgroundBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(224, 20, lowBlurFactor), GetBrush(dictionary, "PopupBackgroundBrush").Color.A);
        Assert.Same(Brushes.Transparent, window.Background);

        window.ApplyAppearance(true, 20, 64, "blur");

        var highBlurFactor = MainWindow.CalculateBlurTintFactor(64);
        Assert.Equal(MainWindow.CalculateBrushAlpha(232, 20, highBlurFactor), GetBrush(dictionary, "DialogBackgroundBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(224, 20, highBlurFactor), GetBrush(dictionary, "PopupBackgroundBrush").Color.A);
        Assert.True(
            GetBrush(dictionary, "DialogBackgroundBrush").Color.A
            > MainWindow.CalculateBrushAlpha(232, 20, lowBlurFactor));
    }

    [Fact]
    public void SharedAccentBrushFollowsTransparencyAndOpaqueFallback()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);

        window.ApplyAppearance(true, 20, 0, "acrylic");
        var lowBlurAlpha = GetBrush(dictionary, "AccentSoftBrush").Color.A;
        Assert.Equal(MainWindow.CalculateBrushAlpha(214, 20, MainWindow.CalculateBlurTintFactor(0)), lowBlurAlpha);

        window.ApplyAppearance(true, 20, 64, "acrylic");
        var highBlurAlpha = GetBrush(dictionary, "AccentSoftBrush").Color.A;
        Assert.Equal(MainWindow.CalculateBrushAlpha(214, 20, MainWindow.CalculateBlurTintFactor(64)), highBlurAlpha);
        Assert.True(highBlurAlpha > lowBlurAlpha);

        window.ApplyAppearance(false, 20, 64, "acrylic");
        Assert.Equal(255, GetBrush(dictionary, "AccentSoftBrush").Color.A);
    }

    [Fact]
    public void SemanticSurfaceBrushesFollowTransparencyAndOpaqueFallback()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);

        window.ApplyAppearance(true, 20, 0, "acrylic");
        var lowBlurFactor = MainWindow.CalculateBlurTintFactor(0);
        Assert.Equal(MainWindow.CalculateBrushAlpha(214, 20, lowBlurFactor), GetBrush(dictionary, "SuccessSoftBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(214, 20, lowBlurFactor), GetBrush(dictionary, "WarningSoftBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(214, 20, lowBlurFactor), GetBrush(dictionary, "DangerSoftBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(160, 20, lowBlurFactor), GetBrush(dictionary, "SuccessBorderBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(160, 20, lowBlurFactor), GetBrush(dictionary, "WarningBorderBrush").Color.A);
        Assert.Equal(MainWindow.CalculateBrushAlpha(160, 20, lowBlurFactor), GetBrush(dictionary, "DangerBorderBrush").Color.A);

        window.ApplyAppearance(true, 20, 64, "acrylic");
        var highBlurFactor = MainWindow.CalculateBlurTintFactor(64);
        Assert.True(
            GetBrush(dictionary, "SuccessSoftBrush").Color.A
            > MainWindow.CalculateBrushAlpha(214, 20, lowBlurFactor));
        Assert.Equal(MainWindow.CalculateBrushAlpha(160, 20, highBlurFactor), GetBrush(dictionary, "DangerBorderBrush").Color.A);

        window.ApplyAppearance(false, 20, 64, "acrylic");
        Assert.Equal(255, GetBrush(dictionary, "SuccessSoftBrush").Color.A);
        Assert.Equal(255, GetBrush(dictionary, "WarningSoftBrush").Color.A);
        Assert.Equal(255, GetBrush(dictionary, "DangerSoftBrush").Color.A);
        Assert.Equal(255, GetBrush(dictionary, "SuccessBorderBrush").Color.A);
        Assert.Equal(255, GetBrush(dictionary, "WarningBorderBrush").Color.A);
        Assert.Equal(255, GetBrush(dictionary, "DangerBorderBrush").Color.A);
    }

    [Fact]
    public void AppliedSecondaryWindowTracksLaterAppearanceChanges()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        var dialog = new GlassDialogWindow();

        window.ApplyAppearance(true, 86, 24, "mica");
        window.AppearanceCoordinator.ApplyTo(dialog);
        Assert.Same(Brushes.Transparent, dialog.Background);
        Assert.Equal(WindowTransparencyLevel.AcrylicBlur, dialog.TransparencyLevelHint[0]);

        window.ApplyAppearance(false, 86, 24, "acrylic");

        Assert.Equal(255, Assert.IsType<SolidColorBrush>(dialog.Background).Color.A);
        // 透明关闭后不得残留窗口级透明提示。
        Assert.Empty(dialog.TransparencyLevelHint);
    }

    [Fact]
    public void CoordinatorNormalizesAppearanceSnapshotAndRaisesChange()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        WindowAppearanceSnapshot? changed = null;
        window.AppearanceCoordinator.AppearanceChanged += (_, args) => changed = args.Snapshot;

        window.ApplyAppearance(true, -10, 1000, "unknown");

        Assert.NotNull(changed);
        Assert.Equal(new WindowAppearanceSnapshot(true, 0, 64, "acrylic"), changed);
        Assert.Equal(changed, window.AppearanceCoordinator.Current);
    }

    [Fact]
    public void 异常气泡最低透明度仍有稳定阅读背景()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        window.ApplyAppearance(true, 0, 0, "acrylic");
        var brush = GetBrush(dictionary, "DangerMessageSurfaceBrush");
        Assert.InRange(brush.Color.A, (byte)217, (byte)254);
        window.ApplyAppearance(false, 0, 0, "acrylic");
        Assert.Equal(255, brush.Color.A);
    }

    [Fact]
    public void 相同外观与主题请求被幂等跳过不重复应用()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        var applications = 0;
        window.AppearanceCoordinator.AppearanceChanged += (_, _) => applications++;

        window.ApplyTheme("dark");
        window.ApplyTheme("light");
        var baseline = applications;
        window.ApplyAppearance(true, 40, 12, "acrylic");
        var appearanceBaseline = applications;

        // 重复的相同请求：主题不刷新资源、外观不重换画刷，事件不再触发。
        window.ApplyTheme("light");
        window.ApplyAppearance(true, 40, 12, "acrylic");

        Assert.Equal(baseline, applications - 1);
        Assert.Equal(appearanceBaseline, applications);
    }

    [Fact]
    public void 透明开关时全部注册窗口同帧一致切换()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window, enabled: false, opacity: 1, blur: 1);
        window.ApplyTheme("light");
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        var existingDialog = new GlassDialogWindow();
        var lateDialog = new GlassDialogWindow();
        Window[] windows = [window, existingDialog, lateDialog];

        // 注册即应用：禁用态下先存窗口与后建窗口都自第一帧起不透明且无透明提示。
        Assert.All(windows, item => Assert.Equal(255, Assert.IsType<SolidColorBrush>(item.Background).Color.A));
        Assert.All(windows, item => Assert.Empty(item.TransparencyLevelHint));

        window.ApplyAppearance(true, 60, 20, "acrylic");

        Assert.All(windows, item => Assert.Same(Brushes.Transparent, item.Background));
        Assert.All(windows, item => Assert.Equal(WindowTransparencyLevel.AcrylicBlur, item.TransparencyLevelHint[0]));

        window.ApplyAppearance(false, 60, 20, "acrylic");

        Assert.All(windows, item => Assert.Equal(255, Assert.IsType<SolidColorBrush>(item.Background).Color.A));
        Assert.All(windows, item => Assert.Empty(item.TransparencyLevelHint));
    }

    [Fact]
    public void 主题切换刷新全部注册窗口的画刷引用()
    {
        EnsureAvaloniaSetup();
        var window = new MainWindow();
        ResetToKnownAppearance(window, enabled: false, opacity: 50, blur: 10);
        var dictionary = LoadVisualTokens();
        window.Resources.MergedDictionaries.Add(dictionary);
        var dialog = new GlassDialogWindow();
        window.ApplyTheme("light");

        // 主题字典切换会替换画刷实例：禁用态下窗口背景必须改用新变体的不透明底色。
        window.ApplyTheme("dark");
        Assert.True(dictionary.TryGetResource("WindowBackgroundBrush", ThemeVariant.Dark, out var darkResource));
        var expectedDark = Assert.IsType<SolidColorBrush>(darkResource).Color;
        foreach (Window item in new Window[] { window, dialog })
        {
            var background = Assert.IsType<SolidColorBrush>(item.Background).Color;
            Assert.Equal(255, background.A);
            Assert.Equal((expectedDark.R, expectedDark.G, expectedDark.B), (background.R, background.G, background.B));
        }
    }

    /// <summary>把共享协调器收敛到已知状态，规避前序测试残留状态触发幂等跳过。</summary>
    private static void ResetToKnownAppearance(MainWindow window, bool enabled = false, int opacity = 1, int blur = 1) =>
        window.ApplyAppearance(enabled, opacity, blur, "acrylic");

    private static ResourceDictionary LoadVisualTokens() => Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(
        new Uri("avares://LoomX/Styles/VisualTokens.axaml")));

    private static SolidColorBrush GetBrush(ResourceDictionary dictionary, string key)
    {
        Assert.True(dictionary.TryGetResource(key, ThemeVariant.Light, out var resource));
        return Assert.IsType<SolidColorBrush>(resource);
    }

    private static void EnsureAvaloniaSetup()
    {
        AvaloniaTestBootstrap.Ensure();
    }
}
