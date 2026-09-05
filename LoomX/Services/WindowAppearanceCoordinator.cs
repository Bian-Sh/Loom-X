using Avalonia.Controls;
using Avalonia.Media;

namespace LoomX.Services;

public sealed record WindowAppearanceSnapshot(bool Enabled, int Opacity, int BlurAmount, string Algorithm);

public sealed class WindowAppearanceChangedEventArgs(WindowAppearanceSnapshot snapshot) : EventArgs
{
    public WindowAppearanceSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// 保存当前窗口外观并把同一套透明材质应用到主窗口和独立弹窗。
/// </summary>
public sealed class WindowAppearanceCoordinator
{
    private readonly MainWindow owner;
    private readonly Dictionary<string, Color> baseBrushColors = new(StringComparer.Ordinal);
    private readonly HashSet<Window> attachedWindows = [];

    public WindowAppearanceSnapshot Current { get; private set; } = new(true, 86, 24, "acrylic");
    public event EventHandler<WindowAppearanceChangedEventArgs>? AppearanceChanged;

    public WindowAppearanceCoordinator(MainWindow owner)
    {
        this.owner = owner;
    }

    public void Apply(bool enabled, int opacity, int blurAmount, string algorithm)
    {
        Current = new(
            enabled,
            Math.Clamp(opacity, 0, 100),
            Math.Clamp(blurAmount, 0, 64),
            NormalizeAlgorithm(algorithm));

        var blurFactor = MainWindow.CalculateBlurTintFactor(Current.BlurAmount);
        SetConfiguredBrushAlpha("WindowBackgroundBrush", 230, blurFactor);
        SetConfiguredBrushAlpha("GlassBrush", 184, blurFactor);
        SetConfiguredBrushAlpha("GlassStrongBrush", 208, blurFactor);
        SetConfiguredBrushAlpha("SurfaceBrush", 199, blurFactor);
        SetConfiguredBrushAlpha("SurfaceSubtleBrush", 164, blurFactor);
        SetConfiguredBrushAlpha("SurfaceMutedBrush", 128, blurFactor);
        SetConfiguredBrushAlpha("NavigationHoverBrush", 214, blurFactor);
        SetConfiguredBrushAlpha("AccentSoftBrush", 214, blurFactor);
        SetConfiguredBrushAlpha("PopupBackgroundBrush", 224, blurFactor);
        SetConfiguredBrushAlpha("DialogBackgroundBrush", 232, blurFactor);

        ApplyWindow(owner);
        foreach (var window in attachedWindows.ToArray()) ApplyWindow(window);
        AppearanceChanged?.Invoke(this, new WindowAppearanceChangedEventArgs(Current));
    }

    public void ApplyTo(Window window)
    {
        ApplyWindow(window);
        if (ReferenceEquals(window, owner) || !attachedWindows.Add(window)) return;
        window.Closed += AttachedWindowOnClosed;
    }

    private void ApplyWindow(Window window)
    {
        window.TransparencyBackgroundFallback = owner.ResolveAppearanceBrush("WindowBackgroundBrush");
        window.Background = Current.Enabled
            ? Brushes.Transparent
            : MainWindow.CreateOpaqueCopy(owner.ResolveAppearanceBrush("WindowBackgroundBrush"));
        window.TransparencyLevelHint = MainWindow.BuildTransparencyLevels(Current.Algorithm);
    }

    private void AttachedWindowOnClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= AttachedWindowOnClosed;
        attachedWindows.Remove(window);
    }

    private void SetBrushAlpha(string key, int alpha)
    {
        if (!owner.TryResolveAppearanceResource(key, out var value) || value is not SolidColorBrush brush) return;
        AppearanceBrushUpdater.Apply(brush, key, baseBrushColors, alpha);
    }

    private void SetConfiguredBrushAlpha(string key, byte baseAlpha, double blurFactor)
    {
        var alpha = Current.Enabled
            ? MainWindow.CalculateBrushAlpha(baseAlpha, Current.Opacity, blurFactor)
            : (byte)255;
        SetBrushAlpha(key, alpha);
    }

    private static string NormalizeAlgorithm(string? algorithm) => "acrylic";
}
