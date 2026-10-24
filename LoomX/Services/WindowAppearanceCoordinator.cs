using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

public sealed record WindowAppearanceSnapshot(bool Enabled, int Opacity, int BlurAmount, string Algorithm);

public sealed class WindowAppearanceChangedEventArgs(WindowAppearanceSnapshot snapshot) : EventArgs
{
    public WindowAppearanceSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// 应用级窗口材质协调点：所有顶层窗口（主窗口、启动闪屏、玻璃对话框及后续新增窗口）
/// 在构造时经 <see cref="ApplyTo"/> 注册并立即取得当前快照，此后共享唯一外观状态。
/// 相同状态的重复应用会被跳过，杜绝启动编排、配置刷新等内部流程引发的材质突变；
/// 主题切换由 <see cref="ApplyTheme"/> 统一驱动并在同一次同步遍历内作用于全部注册窗口。
/// </summary>
public sealed class WindowAppearanceCoordinator
{
    private readonly ILogger<WindowAppearanceCoordinator>? logger;
    private readonly Dictionary<string, Color> baseBrushColors = new(StringComparer.Ordinal);
    private readonly List<WeakReference<Window>> registeredWindows = [];
    private WeakReference<Window>? primaryWindowReference;
    private bool brushesApplied;

    public WindowAppearanceSnapshot Current { get; private set; } = new(true, 86, 24, "acrylic");
    public event EventHandler<WindowAppearanceChangedEventArgs>? AppearanceChanged;

    public WindowAppearanceCoordinator(ILogger<WindowAppearanceCoordinator>? logger = null) => this.logger = logger;

    /// <summary>注册窗口并立即应用当前快照（幂等）；主窗口同时成为资源解析的首选作用域。</summary>
    public void ApplyTo(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window is MainWindow) primaryWindowReference = new WeakReference<Window>(window);
        if (!IsRegistered(window))
        {
            registeredWindows.Add(new WeakReference<Window>(window));
            window.Closed += RegisteredWindowOnClosed;
        }

        ApplyWindowProperties(window);
    }

    public void Apply(bool enabled, int opacity, int blurAmount, string algorithm)
    {
        var candidate = new WindowAppearanceSnapshot(
            enabled,
            Math.Clamp(opacity, 0, 100),
            Math.Clamp(blurAmount, 0, 64),
            NormalizeAlgorithm(algorithm));
        if (brushesApplied && candidate == Current)
        {
            logger?.LogDebug(
                "窗口材质状态未变化，跳过重应用 {Enabled} {Opacity} {BlurAmount} {Algorithm}",
                candidate.Enabled, candidate.Opacity, candidate.BlurAmount, candidate.Algorithm);
            return;
        }

        var changedFields = DescribeChanges(Current, candidate);
        Current = candidate;
        ApplyCore();
        logger?.LogInformation(
            "窗口材质应用完成 {Enabled} {Opacity} {BlurAmount} {Algorithm} {WindowCount} {ChangedFields}",
            Current.Enabled, Current.Opacity, Current.BlurAmount, Current.Algorithm, CountLiveWindows(), changedFields);
    }

    public void ApplyTheme(string theme)
    {
        if (Application.Current is not { } application) return;
        var variant = ParseThemeVariant(theme);
        if (brushesApplied && baseBrushColors.Count > 0 && application.RequestedThemeVariant == variant)
        {
            logger?.LogDebug("窗口主题未变化，跳过资源刷新 {Theme}", theme);
            return;
        }

        application.RequestedThemeVariant = variant;
        RefreshThemeResources();
        logger?.LogInformation("窗口主题应用完成 {Theme} {ThemeVariant}", theme, variant.Key);
    }

    /// <summary>主题字典切换会换掉画刷实例，基础颜色需重录后对全部窗口重放当前快照。</summary>
    public void RefreshThemeResources()
    {
        baseBrushColors.Clear();
        ApplyCore();
    }

    private void ApplyCore()
    {
        ApplyBrushes();
        // 同一次同步遍历内完成全部窗口的属性更新，保证切换落在同一 UI 帧。
        foreach (var window in GetLiveWindows()) ApplyWindowProperties(window);
        brushesApplied = true;
        AppearanceChanged?.Invoke(this, new WindowAppearanceChangedEventArgs(Current));
    }

    private void ApplyBrushes()
    {
        var blurFactor = MainWindow.CalculateBlurTintFactor(Current.BlurAmount);
        SetConfiguredBrushAlpha("WindowBackgroundBrush", 230, blurFactor);
        SetConfiguredBrushAlpha("GlassBrush", 184, blurFactor);
        SetConfiguredBrushAlpha("GlassStrongBrush", 208, blurFactor);
        SetConfiguredBrushAlpha("SurfaceBrush", 199, blurFactor);
        SetConfiguredBrushAlpha("SurfaceSubtleBrush", 164, blurFactor);
        SetConfiguredBrushAlpha("SurfaceMutedBrush", 128, blurFactor);
        SetConfiguredBrushAlpha("NavigationHoverBrush", 214, blurFactor);
        SetConfiguredBrushAlpha("AccentSoftBrush", 214, blurFactor);
        SetConfiguredBrushAlpha("SuccessSoftBrush", 214, blurFactor);
        SetConfiguredBrushAlpha("WarningSoftBrush", 214, blurFactor);
        SetConfiguredBrushAlpha("DangerSoftBrush", 214, blurFactor);
        // 错误阅读层保留 85% 的最低不透明度，不能随装饰玻璃一起退化。
        SetBrushAlpha("DangerMessageSurfaceBrush", Current.Enabled
            ? Math.Max((byte)217, MainWindow.CalculateBrushAlpha(235, Current.Opacity, blurFactor))
            : 255);
        SetConfiguredBrushAlpha("SuccessBorderBrush", 160, blurFactor);
        SetConfiguredBrushAlpha("WarningBorderBrush", 160, blurFactor);
        SetConfiguredBrushAlpha("DangerBorderBrush", 160, blurFactor);
        SetConfiguredBrushAlpha("PopupBackgroundBrush", 224, blurFactor);
        SetConfiguredBrushAlpha("DialogBackgroundBrush", 232, blurFactor);
    }

    private void ApplyWindowProperties(Window window)
    {
        var background = ResolveBrush("WindowBackgroundBrush");
        window.TransparencyBackgroundFallback = background;
        window.Background = Current.Enabled
            ? Brushes.Transparent
            : MainWindow.CreateOpaqueCopy(background);
        // 透明关闭时必须清空窗口级透明提示，不残留系统合成层；启用时在窗口显示前设好 Hint。
        window.TransparencyLevelHint = Current.Enabled
            ? MainWindow.BuildTransparencyLevels(Current.Algorithm)
            : [];
    }

    private bool IsRegistered(Window window)
    {
        foreach (var reference in registeredWindows)
            if (reference.TryGetTarget(out var target) && ReferenceEquals(target, window))
                return true;
        return false;
    }

    private List<Window> GetLiveWindows()
    {
        var windows = new List<Window>();
        foreach (var reference in registeredWindows.ToArray())
        {
            if (reference.TryGetTarget(out var window)) windows.Add(window);
            else registeredWindows.Remove(reference);
        }
        return windows;
    }

    private int CountLiveWindows() => GetLiveWindows().Count;

    private void RegisteredWindowOnClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= RegisteredWindowOnClosed;
        foreach (var reference in registeredWindows.ToArray()
                     .Where(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, window)))
            registeredWindows.Remove(reference);
        if (primaryWindowReference?.TryGetTarget(out var primary) == true && ReferenceEquals(primary, window))
            primaryWindowReference = null;
    }

    private bool TryResolveResource(string key, out object? value)
    {
        if (primaryWindowReference?.TryGetTarget(out var primary) == true
            && primary.TryGetResource(key, primary.ActualThemeVariant, out value))
            return true;
        if (Application.Current is { } application
            && application.TryGetResource(key, application.ActualThemeVariant, out value))
            return true;
        value = null;
        return false;
    }

    private IBrush ResolveBrush(string key) =>
        TryResolveResource(key, out var value) && value is IBrush brush ? brush : Brushes.Transparent;

    private void SetBrushAlpha(string key, int alpha)
    {
        if (!TryResolveResource(key, out var value) || value is not SolidColorBrush brush) return;
        AppearanceBrushUpdater.Apply(brush, key, baseBrushColors, alpha);
    }

    private void SetConfiguredBrushAlpha(string key, byte baseAlpha, double blurFactor)
    {
        var alpha = Current.Enabled
            ? MainWindow.CalculateBrushAlpha(baseAlpha, Current.Opacity, blurFactor)
            : (byte)255;
        SetBrushAlpha(key, alpha);
    }

    private static string DescribeChanges(WindowAppearanceSnapshot before, WindowAppearanceSnapshot after)
    {
        var parts = new List<string>();
        if (before.Enabled != after.Enabled) parts.Add("enabled");
        if (before.Opacity != after.Opacity) parts.Add("opacity");
        if (before.BlurAmount != after.BlurAmount) parts.Add("blur");
        return parts.Count == 0 ? "initial" : string.Join(",", parts);
    }

    private static ThemeVariant ParseThemeVariant(string? theme) => (theme ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "dark" => ThemeVariant.Dark,
        "light" => ThemeVariant.Light,
        _ => ThemeVariant.Default
    };

    private static string NormalizeAlgorithm(string? algorithm) => "acrylic";
}
