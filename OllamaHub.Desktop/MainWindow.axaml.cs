using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop;
public partial class MainWindow : Window
{
    private readonly ToastService toastService;
    private readonly ILogger<MainWindow> logger;
    private readonly DispatcherTimer toastTimer;
    private readonly Dictionary<string, Color> baseBrushColors = new(StringComparer.Ordinal);

    public ToastService ToastService => toastService;

    public MainWindow() : this(new ToastService(), null) { }

    public MainWindow(ToastService toastService, ILogger<MainWindow>? logger = null)
    {
        this.toastService = toastService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance;
        InitializeComponent();
        TransparencyLevelHint = BuildTransparencyLevels("acrylic");
        AddHandler(InputElement.PointerPressedEvent, Window_OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, Window_OnPointerMoved, RoutingStrategies.Tunnel);
        toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            toastBorder.IsVisible = false;
        };
        toastService.Requested += ToastServiceOnRequested;
        Closed += (_, _) => toastService.Requested -= ToastServiceOnRequested;
    }

    private void ToastServiceOnRequested(object? sender, ToastNotification notification)
    {
        void ShowToast()
        {
            toastText.Text = notification.Message;
            toastBorder.Background = new SolidColorBrush(notification.Level switch
            {
                ToastLevel.Success => Color.Parse("#176B5B"),
                ToastLevel.Warning => Color.Parse("#8A5A12"),
                ToastLevel.Error => Color.Parse("#9E3544"),
                _ => Color.Parse("#17212B")
            });
            toastBorder.IsVisible = true;
            toastTimer.Stop();
            toastTimer.Start();
        }

        if (Dispatcher.UIThread.CheckAccess()) ShowToast();
        else Dispatcher.UIThread.Post(ShowToast);
    }

    private void WindowChrome_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInsideButton(e.Source)) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        BeginMoveDrag(e);
    }

    private void Window_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || IsInsideButton(e.Source)) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        var edge = GetResizeEdge(point.Position);
        if (edge is null) return;

        BeginResizeDrag(edge.Value, e);
        e.Handled = true;
    }

    private void Window_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (WindowState == WindowState.Maximized || IsInsideButton(e.Source))
        {
            Cursor = null;
            return;
        }

        Cursor = GetResizeCursor(GetResizeEdge(e.GetPosition(this)));
    }

    private static Cursor? GetResizeCursor(WindowEdge? edge) => edge switch
    {
        WindowEdge.West or WindowEdge.East => new Cursor(StandardCursorType.SizeWestEast),
        WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
        WindowEdge.NorthWest => new Cursor(StandardCursorType.TopLeftCorner),
        WindowEdge.NorthEast => new Cursor(StandardCursorType.TopRightCorner),
        WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
        WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
        _ => null
    };

    private WindowEdge? GetResizeEdge(Point position)
    {
        const double grip = 8;
        var left = position.X <= grip;
        var right = position.X >= Bounds.Width - grip;
        var top = position.Y <= grip;
        var bottom = position.Y >= Bounds.Height - grip;
        return (left, top, right, bottom) switch
        {
            (true, true, _, _) => WindowEdge.NorthWest,
            (true, false, _, true) => WindowEdge.SouthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (_, false, true, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, _, true, _) => WindowEdge.East,
            (_, true, _, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null
        };
    }

    private static bool IsInsideButton(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button) return true;
        }
        return false;
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object? sender, RoutedEventArgs e) => ToggleWindowState();

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void ApplyAppearance(bool enabled, int opacity, int blurAmount, string algorithm)
    {
        logger.LogInformation("透明外观应用开始 {Enabled} {Opacity} {BlurAmount} {Algorithm}", enabled, opacity, blurAmount, algorithm);
        opacity = Math.Clamp(opacity, 0, 100);
        blurAmount = Math.Clamp(blurAmount, 0, 64);
        var appearance = AppearanceProfile.Create(opacity, blurAmount);
        SetBrushAlpha("WindowBackgroundBrush", appearance.ScaleAlpha(230));
        SetBrushAlpha("GlassBrush", appearance.ScaleAlpha(184));
        SetBrushAlpha("GlassStrongBrush", appearance.ScaleAlpha(208));
        SetBrushAlpha("SurfaceBrush", appearance.ScaleAlpha(199));
        SetBrushAlpha("SurfaceSubtleBrush", appearance.ScaleAlpha(164));
        SetBrushAlpha("SurfaceMutedBrush", appearance.ScaleAlpha(128));
        SetBrushAlpha("NavigationHoverBrush", appearance.ScaleAlpha(214));

        TransparencyBackgroundFallback = ResolveBrush("WindowBackgroundBrush");
        var opaqueBackground = OpaqueCopy(ResolveBrush("WindowBackgroundBrush"));
        Background = enabled ? Brushes.Transparent : opaqueBackground;
        TransparencyLevelHint = BuildTransparencyLevels(algorithm, opacity);
        logger.LogInformation(
            "透明外观应用完成 {Enabled} {Opacity} {BlurAmount} {Algorithm} {WindowBackgroundType} {GlassType} {ActualTransparencyLevel}",
            enabled,
            opacity,
            blurAmount,
            algorithm,
            DescribeResource("WindowBackgroundBrush"),
            DescribeResource("GlassBrush"),
            ActualTransparencyLevel);
    }

    private string DescribeResource(string key) =>
        TryResolveResource(key, out var value) && value is not null
            ? value.GetType().FullName ?? value.GetType().Name
            : "missing";

    private bool TryResolveResource(string key, out object? value)
    {
        if (TryGetResource(key, null, out value)) return true;
        if (Application.Current is { } application && application.TryGetResource(key, null, out value)) return true;
        value = null;
        return false;
    }

    internal static IReadOnlyList<WindowTransparencyLevel> BuildTransparencyLevels(string algorithm, int opacity = 100)
    {
        if (opacity <= 0)
            return [WindowTransparencyLevel.Transparent];

        return algorithm.Trim().ToLowerInvariant() switch
        {
            "mica" => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent],
            "blur" => [WindowTransparencyLevel.Blur, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent],
            _ => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]
        };
    }

    private IBrush ResolveBrush(string key) => TryResolveResource(key, out var value) && value is IBrush brush
        ? brush
        : Brushes.Transparent;

    private void SetBrushAlpha(string key, int alpha)
    {
        if (!TryResolveResource(key, out var value) || value is not SolidColorBrush brush)
            return;

        AppearanceBrushUpdater.Apply(brush, key, baseBrushColors, alpha);
    }

    private static SolidColorBrush OpaqueCopy(IBrush brush) => brush is SolidColorBrush solid
        ? new SolidColorBrush(Color.FromArgb(255, solid.Color.R, solid.Color.G, solid.Color.B))
        : new SolidColorBrush(Color.FromArgb(255, 230, 240, 243));
}

internal readonly record struct AppearanceProfile(byte SurfaceAlpha, double MaterialOpacity, double TintOpacity)
{
    public static AppearanceProfile Create(int opacity, int blurAmount)
    {
        var opacityFactor = Math.Clamp(opacity, 0, 100) / 100d;
        var blurFactor = Math.Clamp(blurAmount, 0, 64) / 64d;
        var materialOpacity = opacityFactor * blurFactor;
        return new(
            (byte)Math.Clamp(Math.Round(255 * materialOpacity), 0, 255),
            materialOpacity,
            Math.Clamp(blurFactor, 0, 1));
    }

    public byte ScaleAlpha(byte baseAlpha) =>
        (byte)Math.Clamp(Math.Round(255 * MaterialOpacity), 0, 255);
}

internal static class AppearanceBrushUpdater
{
    public static bool TryApply(
        ResourceDictionary resources,
        string key,
        IDictionary<string, Color> baseColors,
        int alpha)
    {
        if (!resources.TryGetValue(key, out var value) || value is not SolidColorBrush brush)
            return false;

        Apply(brush, key, baseColors, alpha);
        return true;
    }

    public static void Apply(
        SolidColorBrush brush,
        string key,
        IDictionary<string, Color> baseColors,
        int alpha)
    {
        if (!baseColors.TryGetValue(key, out var baseColor))
        {
            baseColor = brush.Color;
            baseColors[key] = baseColor;
        }

        brush.Color = Color.FromArgb(
            (byte)Math.Clamp(alpha, 0, 255),
            baseColor.R,
            baseColor.G,
            baseColor.B);
    }
}
