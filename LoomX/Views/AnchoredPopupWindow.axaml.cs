using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoomX.Views;

/// <summary>
/// 锚定浮层：一个独立的无边框顶层窗口，可以浮到主窗口之外（Avalonia 的 Popup 做不到）。
/// 尖角按锚点的<b>屏幕坐标</b>动态偏移，所以永远精准指向锚点中心，
/// 即便面板被屏幕边缘挤开也不会跑偏。
/// </summary>
public partial class AnchoredPopupWindow : Window
{
    private const double ArrowWidth = 14;
    private const double ArrowHeight = 9;
    private const double Gap = 6;

    private Window? ownerWindow;
    private Control? anchor;
    private double cornerRadius = 14;

    public AnchoredPopupWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    /// <summary>关闭时由调用方接回（用于把 ViewModel 的开关状态复位）。</summary>
    public event EventHandler? PopupClosed;

    /// <summary>
    /// 在 <paramref name="anchor"/> 正下方显示浮层。
    /// </summary>
    /// <param name="owner">主窗口（移动/失焦/点击都会关闭浮层）。</param>
    /// <param name="anchor">锚点控件，尖角对准它的水平中心。</param>
    /// <param name="content">面板内容。</param>
    /// <param name="width">面板宽度。</param>
    /// <param name="maxHeight">面板最大高度（超出后内部滚动）。</param>
    public void ShowAnchored(Window owner, Control anchor, Control content, double width, double maxHeight)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(anchor);

        DetachOwner();

        ownerWindow = owner;
        this.anchor = anchor;

        panel.Width = width;
        panel.MaxHeight = maxHeight;
        panel.Child = content;

        ownerWindow.PointerPressed += OnOwnerPointerPressed;
        ownerWindow.PositionChanged += OnOwnerGeometryChanged;
        ownerWindow.Deactivated += OnOwnerDeactivated;
        ownerWindow.Closing += OnOwnerClosing;
        anchor.DetachedFromVisualTree += OnAnchorDetached;

        PlaceOffscreen();
        Opacity = 0;
        Show(ownerWindow);

        // 布局出真实尺寸后再定位，否则算出来的偏移是错的。
        Dispatcher.UIThread.Post(PlaceAndReveal, DispatcherPriority.Loaded);
    }

    private void PlaceOffscreen()
    {
        var screen = Screens.ScreenFromWindow(ownerWindow) ?? Screens.Primary;
        if (screen is null) return;
        Position = new PixelPoint(screen.WorkingArea.X - 100000, screen.WorkingArea.Y);
    }

    private void PlaceAndReveal()
    {
        if (anchor is null || ownerWindow is null) return;

        var scaling = ownerWindow.RenderScaling;
        if (scaling <= 0) scaling = 1;

        // 锚点底部中心（屏幕物理像素）
        var anchorPoint = anchor.PointToScreen(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height));
        var windowWidth = Bounds.Width * scaling;
        var windowHeight = Bounds.Height * scaling;

        var screen = Screens.ScreenFromPoint(anchorPoint) ?? Screens.Primary;
        var area = screen?.WorkingArea;

        var left = anchorPoint.X - (ArrowWidth / 2) * scaling;
        var top = anchorPoint.Y + Gap * scaling;

        // 先按“尖角对准锚点”摆，再夹回屏幕可视区
        if (area is { } bounds)
        {
            if (left + windowWidth > bounds.Right) left = bounds.Right - windowWidth;
            if (left < bounds.X) left = bounds.X;
            if (top + windowHeight > bounds.Bottom) top = anchorPoint.Y - windowHeight - Gap * scaling;
            if (top < bounds.Y) top = bounds.Y;
        }

        Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));

        // 窗口被夹开后，反推尖角应该落在面板的哪个位置
        var arrowCenterInWindow = (anchorPoint.X - left) / scaling;
        var limit = Math.Max(cornerRadius, ArrowWidth);
        var clamped = Math.Clamp(arrowCenterInWindow, limit, Math.Max(limit, panel.Width - limit));
        arrow.Margin = new Thickness(clamped - ArrowWidth / 2, 0, 0, -1);

        Opacity = 1;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            Close();
            args.Handled = true;
        }
    }

    private void OnOwnerPointerPressed(object? sender, PointerPressedEventArgs args) => Close();

    private void OnOwnerGeometryChanged(object? sender, EventArgs args) => Close();

    private void OnOwnerDeactivated(object? sender, EventArgs args) => Close();

    private void OnOwnerClosing(object? sender, EventArgs args) => Close();

    private void OnAnchorDetached(object? sender, VisualTreeAttachmentEventArgs args) => Close();

    private void OnClosed(object? sender, EventArgs args)
    {
        DetachOwner();
        PopupClosed?.Invoke(this, EventArgs.Empty);
    }

    private void DetachOwner()
    {
        if (ownerWindow is not null)
        {
            ownerWindow.PointerPressed -= OnOwnerPointerPressed;
            ownerWindow.PositionChanged -= OnOwnerGeometryChanged;
            ownerWindow.Deactivated -= OnOwnerDeactivated;
            ownerWindow.Closing -= OnOwnerClosing;
            ownerWindow = null;
        }

        if (anchor is not null)
        {
            anchor.DetachedFromVisualTree -= OnAnchorDetached;
            anchor = null;
        }
    }
}
