using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using LoomX.Localization;
using LoomX.Models;
using LoomX.Services;
using LoomX.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace LoomX;
public partial class MainWindow : Window
{
    // 透明度为 0 时保留轻微基底，避免系统材质在 alpha=0 时退化为仅边框。
    private const double MinimumOpacityFactor = 0.16;
    private readonly ToastService toastService;
    private readonly ILogger<MainWindow> logger;
    private readonly DispatcherTimer toastTimer;
    private readonly WindowAppearanceCoordinator appearanceCoordinator;
    private readonly AppModalService appModalService;
    private MainWindowViewModel? navigationViewModel;
    private UpdateCoordinator? observedUpdateCoordinator;
    private readonly UpdateWindowPresentation updatePresentation = new();
    private readonly DispatcherTimer navigationSelectionAnimationTimer;
    private readonly DispatcherTimer updateReleasePreviewHideTimer;
    private readonly TranslateTransform navigationSelectionIndicatorTransform = new();
    private readonly TranslateTransform navigationSelectionOutlineTransform = new();
    private Stopwatch? navigationSelectionAnimationStopwatch;
    private double navigationSelectionOffset;
    private double navigationSelectionAnimationFrom;
    private double navigationSelectionAnimationTarget;
    private const double NavigationSelectionAnimationDurationMs = 200;
    private static readonly CubicEaseOut NavigationSelectionEasing = new();
    // 窗口宽度收窄到该阈值以下时自动折叠侧栏；放宽时不会自动展开，需用户手动展开。
    private const double SidebarAutoCollapseWidth = 1000;
    // 同方向拖拽位移累计超过该值才认定用户在压缩/拉宽窗口；±死区内的抖动不响应。
    private const double SidebarAutoCollapseDeadzone = 5;
    private bool isSidebarCollapsed;
    // 上一次处理自动折叠时记录的窗口宽度；<= 0 表示尚未记录（首次布局）。
    private double lastClientWidthForSidebar = -1;
    // 当前方向上累计的拖拽位移（带符号，负数为收窄）；方向反转或确认意图后清零重算。
    private double sidebarResizeAccumulatedDelta;

    public ToastService ToastService => toastService;
    public UpdateWindowPresentation UpdatePresentation => updatePresentation;
    internal WindowAppearanceCoordinator AppearanceCoordinator => appearanceCoordinator;

    public MainWindow() : this(new ToastService(), null, null) { }

    public MainWindow(
        ToastService toastService,
        ILogger<MainWindow>? logger = null,
        ILogger<AppModalService>? appModalLogger = null)
    {
        this.toastService = toastService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance;
        appModalService = new AppModalService(appModalLogger);
        InitializeComponent();
        appModalHost.Attach(appModalService);
        navigationSelectionIndicator.RenderTransform = navigationSelectionIndicatorTransform;
        navigationSelectionOutline.RenderTransform = navigationSelectionOutlineTransform;
        navigationSelectionAnimationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(8),
            DispatcherPriority.Render,
            NavigationSelectionAnimationTimer_OnTick);
        updateReleasePreviewHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        updateReleasePreviewHideTimer.Tick += (_, _) => CloseUpdateReleasePreview();
        UpdateSidebarCollapseToolTip();
        LocaleService.CultureChanged += MainWindow_OnCultureChanged;
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
            LocaleService.CultureChanged -= MainWindow_OnCultureChanged;
            updateReleasePreviewHideTimer.Stop();
            DetachUpdateCoordinator();
            DetachNavigationViewModel();
            updatePresentation.Dispose();
        };
    }

    private void MainWindow_OnDataContextChanged(object? sender, EventArgs e)
    {
        var viewModel = DataContext as MainWindowViewModel;
        AttachNavigationViewModel(viewModel);
        AttachUpdateCoordinator(viewModel?.Update);
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

    private void AttachUpdateCoordinator(UpdateCoordinator? coordinator)
    {
        if (ReferenceEquals(observedUpdateCoordinator, coordinator)) return;

        DetachUpdateCoordinator();
        observedUpdateCoordinator = coordinator;
        updatePresentation.Attach(coordinator);
        if (observedUpdateCoordinator is null) return;

        observedUpdateCoordinator.PropertyChanged += UpdateCoordinator_OnPropertyChanged;
    }

    private void DetachUpdateCoordinator()
    {
        if (observedUpdateCoordinator is not null)
            observedUpdateCoordinator.PropertyChanged -= UpdateCoordinator_OnPropertyChanged;
        observedUpdateCoordinator = null;
        CloseUpdateReleasePreview();
        updatePresentation.Attach(null);
    }

    private void UpdateCoordinator_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateCoordinator.IsUpdateEntryVisible)
            && sender is UpdateCoordinator { IsUpdateEntryVisible: false })
            CloseUpdateReleasePreview();
    }

    private void NavigationViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNavigationOffset) && sender is MainWindowViewModel viewModel)
        {
            var targetOffset = viewModel.SelectedNavigationOffset;
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

    public Task<bool> ConfirmUpdateInstallAsync() =>
        appModalService.ShowConfirmationAsync(new AppModalOptions(
            ResourceLookup.Resolve("update.install.confirm.title", LocaleService.CurrentCulture),
            ResourceLookup.Resolve("update.install.confirm.message", LocaleService.CurrentCulture),
            AppModalKind.Warning,
            ResourceLookup.Resolve("update.install.confirm.accept", LocaleService.CurrentCulture),
            ResourceLookup.Resolve("update.install.confirm.cancel", LocaleService.CurrentCulture)));

    private void UpdateEntryButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        updateReleasePreviewHideTimer.Stop();
        if (observedUpdateCoordinator?.Release is not null)
            updateReleasePreviewPopup.IsOpen = true;
    }

    private void UpdateEntryButton_OnPointerExited(object? sender, PointerEventArgs e) =>
        ScheduleUpdateReleasePreviewClose();

    private void UpdateReleasePreview_OnPointerEntered(object? sender, PointerEventArgs e) =>
        updateReleasePreviewHideTimer.Stop();

    private void UpdateReleasePreview_OnPointerExited(object? sender, PointerEventArgs e) =>
        ScheduleUpdateReleasePreviewClose();

    private void ScheduleUpdateReleasePreviewClose()
    {
        updateReleasePreviewHideTimer.Stop();
        updateReleasePreviewHideTimer.Start();
    }

    private void CloseUpdateReleasePreview()
    {
        updateReleasePreviewHideTimer.Stop();
        updateReleasePreviewPopup.IsOpen = false;
    }

    private void SidebarCollapseButton_OnClick(object? sender, RoutedEventArgs e) =>
        SetSidebarCollapsed(!isSidebarCollapsed);

    private void SetSidebarCollapsed(bool collapsed)
    {
        if (isSidebarCollapsed == collapsed) return;
        isSidebarCollapsed = collapsed;
        if (collapsed) sidebar.Classes.Add("collapsed");
        else sidebar.Classes.Remove("collapsed");
        UpdateSidebarCollapseToolTip();
    }

    private void MainWindow_OnCultureChanged(object? sender, CultureInfo culture)
    {
        if (Dispatcher.UIThread.CheckAccess()) UpdateSidebarCollapseToolTip();
        else Dispatcher.UIThread.Post(UpdateSidebarCollapseToolTip);
    }

    private void UpdateSidebarCollapseToolTip()
    {
        var key = isSidebarCollapsed ? "sidebar.expand" : "sidebar.collapse";
        ToolTip.SetTip(sidebarCollapseButton, ResourceLookup.Resolve(key));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty) AutoCollapseSidebar();
    }

    private void AutoCollapseSidebar()
    {
        var width = ClientSize.Width;
        if (width <= 0) return;
        var shouldCollapse = ShouldAutoCollapseSidebar(
            lastClientWidthForSidebar,
            width,
            sidebarResizeAccumulatedDelta,
            isSidebarCollapsed,
            out var accumulatedDelta);
        lastClientWidthForSidebar = width;
        sidebarResizeAccumulatedDelta = accumulatedDelta;
        if (shouldCollapse) SetSidebarCollapsed(true);
    }

    // 折叠判定：delta 仅用于判断拖拽方向，不按单次大小裁决；同方向位移累计超过 ±死区 才确认用户意图。
    // 首次布局即低于阈值时直接折叠；确认"在压缩"且宽度低于阈值时折叠；确认"在拉宽"时不响应并清零累计。
    internal static bool ShouldAutoCollapseSidebar(
        double previousWidth,
        double width,
        double accumulatedDelta,
        bool isCollapsed,
        out double nextAccumulatedDelta)
    {
        nextAccumulatedDelta = accumulatedDelta;
        if (previousWidth <= 0)
        {
            nextAccumulatedDelta = 0;
            return !isCollapsed && width < SidebarAutoCollapseWidth;
        }

        var delta = width - previousWidth;
        if (delta == 0) return false;

        nextAccumulatedDelta = Math.Sign(delta) == Math.Sign(accumulatedDelta)
            ? accumulatedDelta + delta
            : delta;

        if (nextAccumulatedDelta < -SidebarAutoCollapseDeadzone)
        {
            nextAccumulatedDelta = 0;
            return !isCollapsed && width < SidebarAutoCollapseWidth;
        }

        if (nextAccumulatedDelta > SidebarAutoCollapseDeadzone) nextAccumulatedDelta = 0;
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

    public void ApplyTheme(string theme)
    {
        if (Application.Current is not { } application) return;
        application.RequestedThemeVariant = theme.Trim().ToLowerInvariant() switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
        appearanceCoordinator.RefreshThemeResources();
    }

    public void ApplyAppearance(bool enabled, int opacity, int blurAmount, string algorithm)
    {
        // 算法选择已固定为 Acrylic；保留参数仅兼容旧版调用方和配置数据。
        algorithm = "acrylic";
        logger.LogDebug("透明外观应用开始 {Enabled} {Opacity} {BlurAmount} {Algorithm}", enabled, opacity, blurAmount, algorithm);
        opacity = Math.Clamp(opacity, 0, 100);
        blurAmount = Math.Clamp(blurAmount, 0, 64);
        appearanceCoordinator.Apply(enabled, opacity, blurAmount, algorithm);
        // 保留主窗口入口的显式材质赋值，兼容现有外观契约和运行时诊断。
        TransparencyLevelHint = BuildTransparencyLevels(algorithm);
        logger.LogDebug(
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
        var theme = ActualThemeVariant;
        if (TryGetResource(key, theme, out value)) return true;
        if (Application.Current is { } application && application.TryGetResource(key, theme, out value)) return true;
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

public sealed class UpdateWindowPresentation : IDisposable
{
    public UpdateWindowPresentationAdapter Update { get; } = new();

    internal void Attach(UpdateCoordinator? coordinator) => Update.Attach(coordinator);

    public void Dispose() => Update.Dispose();
}

public sealed class UpdateWindowPresentationAdapter : INotifyPropertyChanged, IDisposable
{
    private static readonly string[] SourceReplacementProperties =
    [
        nameof(ReleaseNotesContent),
        nameof(LatestVersion),
        nameof(StatusText),
        nameof(ErrorMessage),
        nameof(UpdateEntryText),
        nameof(UpdateEntryHint),
        nameof(ProgressText),
        nameof(SpeedText),
        nameof(DownloadPercent),
        nameof(IsUpdateEntryVisible),
        nameof(IsDownloading),
        nameof(IsVerifying),
        nameof(IsPreparing),
        nameof(IsReady),
        nameof(IsError),
        nameof(CanInstall),
        nameof(CanRetry),
        nameof(ActivateUpdateEntryCommand),
        nameof(RetryCommand),
        nameof(InstallAndRestartCommand)
    ];
    private static readonly string[] StageDerivedProperties =
    [
        nameof(IsDownloading),
        nameof(IsVerifying),
        nameof(IsPreparing),
        nameof(IsReady),
        nameof(IsError)
    ];

    private UpdateCoordinator? coordinator;
    private ReleaseNotesContentViewModel? releaseNotesContent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReleaseNotesContentViewModel? ReleaseNotesContent => releaseNotesContent;
    public string LatestVersion => coordinator?.LatestVersion ?? string.Empty;
    public string StatusText => coordinator?.StatusText ?? string.Empty;
    public string ErrorMessage => coordinator?.ErrorMessage ?? string.Empty;
    public string UpdateEntryText => coordinator?.UpdateEntryText ?? string.Empty;
    public string UpdateEntryHint => coordinator?.UpdateEntryHint ?? string.Empty;
    public string ProgressText => coordinator?.ProgressText ?? string.Empty;
    public string SpeedText => coordinator?.SpeedText ?? string.Empty;
    public int DownloadPercent => coordinator?.DownloadPercent ?? 0;
    public bool IsUpdateEntryVisible => coordinator?.IsUpdateEntryVisible == true;
    public bool IsDownloading => coordinator?.Stage == UpdateStage.Downloading;
    public bool IsVerifying => coordinator?.Stage == UpdateStage.Verifying;
    public bool IsPreparing => coordinator?.Stage is UpdateStage.Downloading or UpdateStage.Verifying;
    public bool IsReady => coordinator?.Stage == UpdateStage.Ready;
    public bool IsError => coordinator?.Stage == UpdateStage.Error;
    public bool CanInstall => coordinator?.CanInstall == true;
    public bool CanRetry => coordinator?.CanRetry == true;
    public ICommand? ActivateUpdateEntryCommand => coordinator?.ActivateUpdateEntryCommand;
    public ICommand? RetryCommand => coordinator?.RetryCommand;
    public ICommand? InstallAndRestartCommand => coordinator?.InstallAndRestartCommand;

    internal void Attach(UpdateCoordinator? value)
    {
        if (ReferenceEquals(coordinator, value)) return;

        if (coordinator is not null) coordinator.PropertyChanged -= Coordinator_OnPropertyChanged;
        coordinator = value;
        if (coordinator is not null) coordinator.PropertyChanged += Coordinator_OnPropertyChanged;

        if (coordinator is null)
        {
            releaseNotesContent?.Dispose();
            releaseNotesContent = null;
        }
        else
        {
            releaseNotesContent ??= new ReleaseNotesContentViewModel();
            releaseNotesContent.SetRelease(coordinator.Release);
        }
        RaisePropertiesChanged(SourceReplacementProperties);
    }

    private void Coordinator_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UpdateCoordinator.Release):
                releaseNotesContent?.SetRelease(coordinator?.Release);
                break;
            case nameof(UpdateCoordinator.Stage):
                RaisePropertiesChanged(StageDerivedProperties);
                break;
            case nameof(UpdateCoordinator.DownloadPercent):
                RaisePropertyChanged(nameof(DownloadPercent));
                break;
            case nameof(UpdateCoordinator.ProgressText):
                RaisePropertyChanged(nameof(ProgressText));
                break;
            case nameof(UpdateCoordinator.SpeedText):
                RaisePropertyChanged(nameof(SpeedText));
                break;
            case nameof(UpdateCoordinator.LatestVersion):
                RaisePropertyChanged(nameof(LatestVersion));
                break;
            case nameof(UpdateCoordinator.StatusText):
                RaisePropertyChanged(nameof(StatusText));
                break;
            case nameof(UpdateCoordinator.ErrorMessage):
                RaisePropertyChanged(nameof(ErrorMessage));
                break;
            case nameof(UpdateCoordinator.UpdateEntryText):
                RaisePropertyChanged(nameof(UpdateEntryText));
                break;
            case nameof(UpdateCoordinator.UpdateEntryHint):
                RaisePropertyChanged(nameof(UpdateEntryHint));
                break;
            case nameof(UpdateCoordinator.IsUpdateEntryVisible):
                RaisePropertyChanged(nameof(IsUpdateEntryVisible));
                break;

            case nameof(UpdateCoordinator.CanInstall):
                RaisePropertyChanged(nameof(CanInstall));
                break;
            case nameof(UpdateCoordinator.CanRetry):
                RaisePropertyChanged(nameof(CanRetry));
                break;
        }
    }

    private void RaisePropertiesChanged(IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames) RaisePropertyChanged(propertyName);
    }

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (coordinator is not null) coordinator.PropertyChanged -= Coordinator_OnPropertyChanged;
        coordinator = null;
        releaseNotesContent?.Dispose();
        releaseNotesContent = null;
    }
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
