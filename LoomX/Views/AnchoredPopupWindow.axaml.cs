using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoomX.Views;

/// <summary>
/// 锚定浮层：一个独立的无边框顶层窗口，可以浮到主窗口之外（Avalonia 的 Popup 做不到）。
/// 默认把面板<b>水平居中</b>在锚点正下方（尖角落在面板中间），再整体夹回屏幕可视区；
/// 尖角按锚点的<b>屏幕坐标</b>反推偏移，所以永远精准指向锚点中心，
/// 即便面板被屏幕边缘挤开也不会跑偏。
/// </summary>
public partial class AnchoredPopupWindow : Window
{
    private const double ArrowWidth = 14;
    private const double ArrowHeight = 9;
    private const double Gap = 6;

    /// <summary>面板圆角半径，必须与 <c>AnchoredPopupWindow.axaml</c> 里 panel 的 CornerRadius 保持一致。</summary>
    private const double PanelCornerRadius = 14;

    /// <summary>
    /// 尖角中心距离面板左右边缘的最小距离。尖角底边半宽是 <see cref="ArrowWidth"/> / 2，
    /// 底边若落进圆角段就会悬空，所以最小内缩 = 圆角半径 + 半个尖角宽。
    /// </summary>
    private const double ArrowMinInset = PanelCornerRadius + ArrowWidth / 2;

    private Window? ownerWindow;
    private Control? anchor;

    /// <summary>
    /// 最近一次"在浮窗内部按下指针"的时间。
    /// 用来区分「主窗口失活」到底是"用户点了浮窗"还是"用户点到了外面"。
    /// </summary>
    private DateTimeOffset lastInsidePressAt = DateTimeOffset.MinValue;

    /// <summary>浮窗内按下的宽限窗口：点击浮窗时 <c>IsActive</c> 的更新可能晚于主窗口失活。</summary>
    private static readonly TimeSpan InsidePressGrace = TimeSpan.FromMilliseconds(500);

    public AnchoredPopupWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
        KeyDown += OnKeyDown;
        Closed += OnClosed;

        // 隧道阶段先于按钮的 Click 记录，作为主窗口失活原因判定的兜底依据。
        AddHandler(PointerPressedEvent, OnInsidePointerPressed, RoutingStrategies.Tunnel);
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
        if (ownerWindow is null) return;

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

        // 先按“面板居中在锚点下方”摆：尖角落在面板正中，正好指向锚点。
        var left = anchorPoint.X - windowWidth / 2;
        var top = anchorPoint.Y + Gap * scaling;

        // 再整体夹回屏幕可视区：宁可让尖角偏离面板中心，也不能让面板出屏。
        if (area is { } bounds)
        {
            if (left + windowWidth > bounds.Right) left = bounds.Right - windowWidth;
            if (left < bounds.X) left = bounds.X;
            if (top + windowHeight > bounds.Bottom) top = anchorPoint.Y - windowHeight - Gap * scaling;
            if (top < bounds.Y) top = bounds.Y;
        }

        Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));

        // 面板被屏幕挤开后，反推尖角应该落在面板的哪个位置：对准锚点，
        // 只在会压到面板圆角时向内收 —— 注意不能再动窗口位置，
        // 否则会像修这个问题之前那样，尖角被推离锚点却又没人补偿。
        var arrowCenterInWindow = (anchorPoint.X - left) / scaling;
        var maxInset = Math.Max(ArrowMinInset, panel.Width - ArrowMinInset);
        var clamped = Math.Clamp(arrowCenterInWindow, ArrowMinInset, maxInset);
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

    /// <summary>
    /// 主窗口失活：绝大多数情况下这就是"用户点了浮窗本身"——点击会先把浮窗激活，
    /// 主窗口随之失活。此时<b>绝不能关</b>，否则浮窗里的按钮永远点不到：
    /// <c>WM_ACTIVATE</c> 早于 <c>WM_LBUTTONDOWN</c>，浮窗在按钮的 Click 派发之前就没了宿主，
    /// 表现就是"点了没反应，浮窗还消失"。
    /// 真正要关的是"点到了浮窗外面"（另一个应用、桌面、主窗口），
    /// 那种情况下浮窗自己会收到 Deactivated，这里的延迟判定只是兜底。
    /// </summary>
    private void OnOwnerDeactivated(object? sender, EventArgs args) =>
        // 延后到本轮输入处理完成再看：IsActive 此时已经稳定。
        Dispatcher.UIThread.Post(CloseUnlessEngaged, DispatcherPriority.Input);

    private void CloseUnlessEngaged()
    {
        // 浮窗自己成了活动窗口 -> 用户点在浮窗里。
        if (IsActive) return;
        // 兜底：浮窗内刚有指针按下（IsActive 的更新可能慢半拍）-> 也算点在浮窗里。
        if (DateTimeOffset.UtcNow - lastInsidePressAt < InsidePressGrace) return;
        Close();
    }

    private void OnInsidePointerPressed(object? sender, PointerPressedEventArgs args) =>
        lastInsidePressAt = DateTimeOffset.UtcNow;

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
