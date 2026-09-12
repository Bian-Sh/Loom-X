using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using LoomX.Services;
using LoomX.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoomX;
public partial class MainWindow : Window
{
    // 透明度为 0 时保留轻微基底，避免系统材质在 alpha=0 时退化为仅边框。
    private const double MinimumOpacityFactor = 0.16;
    private readonly ToastService toastService;
    private readonly ILogger<MainWindow> logger;
    private readonly DispatcherTimer toastTimer;
    private readonly WindowAppearanceCoordinator appearanceCoordinator;
    private MainWindowViewModel? navigationViewModel;
    private readonly DispatcherTimer navigationSelectionAnimationTimer;
    private readonly TranslateTransform navigationSelectionIndicatorTransform = new();
    private readonly TranslateTransform navigationSelectionOutlineTransform = new();
    private Stopwatch? navigationSelectionAnimationStopwatch;
    private double navigationSelectionOffset;
    private double navigationSelectionAnimationFrom;
    private double navigationSelectionAnimationTarget;
    private const double NavigationSelectionAnimationDurationMs = 200;
    private static readonly CubicEaseOut NavigationSelectionEasing = new();

    public ToastService ToastService => toastService;
    internal WindowAppearanceCoordinator AppearanceCoordinator => appearanceCoordinator;

    public MainWindow() : this(new ToastService(), null) { }

    public MainWindow(ToastService toastService, ILogger<MainWindow>? logger = null)
    {
        this.toastService = toastService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance;
        InitializeComponent();
        navigationSelectionIndicator.RenderTransform = navigationSelectionIndicatorTransform;
        navigationSelectionOutline.RenderTransform = navigationSelectionOutlineTransform;
        navigationSelectionAnimationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(8),
            DispatcherPriority.Render,
            NavigationSelectionAnimationTimer_OnTick);
        appearanceCoordinator = new WindowAppearanceCoordinator(this);
        TransparencyLevelHint = BuildTransparencyLevels("acrylic");
        AddHandler(InputElement.PointerPressedEvent, Window_OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, Window_OnPointerMoved, RoutingStrategies.Tunnel);
        DataContextChanged += MainWindow_OnDataContextChanged;
        toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            toastBorder.IsVisible = false;
        };
        toastService.Requested += ToastServiceOnRequested;
        Closed += (_, _) =>
        {
            toastService.Requested -= ToastServiceOnRequested;
            DetachNavigationViewModel();
        };
    }

    private void MainWindow_OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachNavigationViewModel(DataContext as MainWindowViewModel);
    }

    private void AttachNavigationViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(navigationViewModel, viewModel))
        {
            if (viewModel is not null) SetNavigationSelectionOffset(viewModel.SelectedNavigationOffset);
            return;
        }

        DetachNavigationViewModel();
        navigationViewModel = viewModel;
        if (navigationViewModel is null) return;

        navigationViewModel.PropertyChanged += NavigationViewModel_OnPropertyChanged;
        // 首次绑定时直接定位默认概览，后续切换才进入动画，避免首帧从未布局状态硬切。
        SetNavigationSelectionOffset(navigationViewModel.SelectedNavigationOffset);
    }

    private void DetachNavigationViewModel()
    {
        if (navigationViewModel is not null)
            navigationViewModel.PropertyChanged -= NavigationViewModel_OnPropertyChanged;
        navigationViewModel = null;
        navigationSelectionAnimationTimer.Stop();
        navigationSelectionAnimationStopwatch = null;
    }

    private void NavigationViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNavigationOffset) && sender is MainWindowViewModel viewModel)
        {
            var targetOffset = viewModel.SelectedNavigationOffset;
            logger.LogInformation("左侧导航选中框切换请求 {TargetOffset}", targetOffset);
            // 等待当前导航命令完成页面通知，避免首次创建页面阻塞动画的首帧。
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(navigationViewModel, viewModel)) AnimateNavigationSelection(targetOffset);
            }, DispatcherPriority.Background);
        }
    }

    private void SetNavigationSelectionOffset(double offset)
    {
        navigationSelectionOffset = offset;
        // 复用变换对象，只更新 Y 属性，减少动画过程中的分配和渲染树重建。
        navigationSelectionIndicatorTransform.Y = offset;
        navigationSelectionOutlineTransform.Y = offset;
    }

    private void AnimateNavigationSelection(double targetOffset)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AnimateNavigationSelection(targetOffset));
            return;
        }

        navigationSelectionAnimationTimer.Stop();
        navigationSelectionAnimationStopwatch = null;
        var fromOffset = navigationSelectionOffset;
        if (Math.Abs(fromOffset - targetOffset) < 0.01)
        {
            SetNavigationSelectionOffset(targetOffset);
            return;
        }

        navigationSelectionAnimationFrom = fromOffset;
        navigationSelectionAnimationTarget = targetOffset;
        navigationSelectionAnimationStopwatch = Stopwatch.StartNew();
        logger.LogInformation("左侧导航选中框动画开始 {FromOffset} -> {TargetOffset}", fromOffset, targetOffset);
        navigationSelectionAnimationTimer.Start();
    }

    private void NavigationSelectionAnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (navigationSelectionAnimationStopwatch is null)
        {
            navigationSelectionAnimationTimer.Stop();
            return;
        }

        var progress = navigationSelectionAnimationStopwatch.Elapsed.TotalMilliseconds / NavigationSelectionAnimationDurationMs;
        if (progress >= 1)
        {
            SetNavigationSelectionOffset(navigationSelectionAnimationTarget);
            navigationSelectionAnimationTimer.Stop();
            navigationSelectionAnimationStopwatch = null;
            logger.LogInformation("左侧导航选中框动画完成 {TargetOffset}", navigationSelectionAnimationTarget);
            return;
        }

        // Cubic ease-out 让选中框立即跟手移动，并在目标卡片前自然减速。
        var eased = NavigationSelectionEasing.Ease(progress);
        SetNavigationSelectionOffset(navigationSelectionAnimationFrom + ((navigationSelectionAnimationTarget - navigationSelectionAnimationFrom) * eased));
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

    internal void ActivateFromSecondaryLaunch()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ActivateFromSecondaryLaunch);
            return;
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        var nativeActivated = !OperatingSystem.IsWindows() || NativeWindowActivation.TryBringToFront(this);
        logger.LogInformation("重复启动请求已激活主窗口 {NativeActivated} {ProcessId}", nativeActivated, Environment.ProcessId);
    }

    public void ApplyAppearance(bool enabled, int opacity, int blurAmount, string algorithm)
    {
        // 算法选择已固定为 Acrylic；保留参数仅兼容旧版调用方和配置数据。
        algorithm = "acrylic";
        logger.LogInformation("透明外观应用开始 {Enabled} {Opacity} {BlurAmount} {Algorithm}", enabled, opacity, blurAmount, algorithm);
        opacity = Math.Clamp(opacity, 0, 100);
        blurAmount = Math.Clamp(blurAmount, 0, 64);
        appearanceCoordinator.Apply(enabled, opacity, blurAmount, algorithm);
        // 保留主窗口入口的显式材质赋值，兼容现有外观契约和运行时诊断。
        TransparencyLevelHint = BuildTransparencyLevels(algorithm);
        logger.LogInformation(
            "透明外观应用完成 {Enabled} {Opacity} {BlurAmount} {Algorithm} {WindowBackgroundType} {GlassType} {ActualTransparencyLevel}",
            enabled,
            appearanceCoordinator.Current.Opacity,
            appearanceCoordinator.Current.BlurAmount,
            algorithm,
            DescribeResource("WindowBackgroundBrush"),
            DescribeResource("GlassBrush"),
            ActualTransparencyLevel);
    }

    private string DescribeResource(string key) =>
        TryResolveAppearanceResource(key, out var value) && value is not null
            ? value.GetType().FullName ?? value.GetType().Name
            : "missing";

    internal bool TryResolveAppearanceResource(string key, out object? value)
    {
        if (TryGetResource(key, null, out value)) return true;
        if (Application.Current is { } application && application.TryGetResource(key, null, out value)) return true;
        value = null;
        return false;
    }

    internal static IReadOnlyList<WindowTransparencyLevel> BuildTransparencyLevels(string algorithm) =>
        [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent];

    internal IBrush ResolveAppearanceBrush(string key) => TryResolveAppearanceResource(key, out var value) && value is IBrush brush
        ? brush
        : Brushes.Transparent;

    internal static double CalculateBlurTintFactor(int blurAmount) =>
        0.35 + (Math.Clamp(blurAmount, 0, 64) / 64d * 0.65);

    internal static double CalculateOpacityFactor(int opacity) =>
        MinimumOpacityFactor + ((1 - MinimumOpacityFactor) * (Math.Clamp(opacity, 0, 100) / 100d));

    internal static byte CalculateBrushAlpha(byte baseAlpha, int opacity, double blurTintFactor) =>
        (byte)Math.Clamp(Math.Round(baseAlpha * CalculateOpacityFactor(opacity) * Math.Clamp(blurTintFactor, 0, 1)), 0, 255);

    internal static SolidColorBrush CreateOpaqueCopy(IBrush brush) => brush is SolidColorBrush solid
        ? new SolidColorBrush(Color.FromArgb(255, solid.Color.R, solid.Color.G, solid.Color.B))
        : new SolidColorBrush(Color.FromArgb(255, 230, 240, 243));
}

internal static class NativeWindowActivation
{
    internal static bool TryBringToFront(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return false;

        ShowWindow(handle, ShowWindowRestore);
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
        SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
        BringWindowToTop(handle);
        var activated = SetForegroundWindow(handle) && GetForegroundWindow() == handle;
        if (!activated)
            FlashWindow(handle, true);
        return activated;
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const int ShowWindowRestore = 9;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosShowWindow = 0x0040;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindow(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] bool invert);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
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
