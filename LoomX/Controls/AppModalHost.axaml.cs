using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LoomX.Models;
using LoomX.Services;

namespace LoomX.Controls;

/// <summary>
/// 主窗口内部的模态覆盖层。宿主只负责呈现和输入，排队与结果由 AppModalService 管理。
/// </summary>
public partial class AppModalHost : UserControl
{
    private AppModalService? service;
    private bool feedbackRunning;

    public AppModalHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 可由主窗口在代码后置或绑定中注入的模态服务。
    /// </summary>
    public AppModalService? Service
    {
        get => service;
        set
        {
            if (ReferenceEquals(service, value))
                return;

            if (service is not null)
                service.CurrentRequestChanged -= Service_OnCurrentRequestChanged;

            service = value;

            if (service is not null)
                service.CurrentRequestChanged += Service_OnCurrentRequestChanged;

            RefreshRequest();
        }
    }

    public void Attach(AppModalService modalService)
    {
        ArgumentNullException.ThrowIfNull(modalService);
        Service = modalService;
    }

    private void Service_OnCurrentRequestChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshRequest();
            return;
        }

        Dispatcher.UIThread.Post(RefreshRequest);
    }

    private void RefreshRequest()
    {
        var request = service?.CurrentRequest;
        IsVisible = request is not null;
        if (request is null)
            return;

        var options = request.Options;
        titleText.Text = options.Title;
        messageText.Text = options.Message;
        confirmButton.Content = options.ConfirmButtonText;
        cancelButton.Content = options.CancelButtonText;
        warningBadge.IsVisible = options.Kind == AppModalKind.Warning;

        // 等布局完成后把键盘焦点放到主操作，避免焦点留在被遮罩的页面。
        Dispatcher.UIThread.Post(() => confirmButton.Focus(), DispatcherPriority.Input);
    }

    private void Overlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, overlay))
            return;

        // 默认不允许点击遮罩关闭；仅用轻微闪动提示用户必须明确选择。
        e.Handled = true;
        _ = RunMaskFeedbackAsync();
    }

    private async Task RunMaskFeedbackAsync()
    {
        if (feedbackRunning)
            return;

        feedbackRunning = true;
        try
        {
            if (Resources["MaskFeedbackAnimation"] is Animation animation)
                await animation.RunAsync(modalCard);
        }
        finally
        {
            feedbackRunning = false;
        }
    }

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs e)
    {
        service?.TryCompleteCurrent(true);
        e.Handled = true;
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        service?.TryCompleteCurrent(false);
        e.Handled = true;
    }
}
