using LoomX.Models;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// 为主窗口内部的模态宿主排队确认请求，并异步返回用户选择。
/// </summary>
public sealed class AppModalService
{
    private readonly object syncRoot = new();
    private readonly Queue<AppModalRequest> pendingRequests = new();
    private readonly ILogger<AppModalService>? logger;
    private AppModalRequest? currentRequest;

    public AppModalService(ILogger<AppModalService>? logger = null)
    {
        this.logger = logger;
    }

    /// <summary>
    /// 活动请求发生变化时通知界面刷新；事件不会携带正文副本。
    /// </summary>
    public event EventHandler? CurrentRequestChanged;

    public AppModalRequest? CurrentRequest
    {
        get
        {
            lock (syncRoot)
                return currentRequest;
        }
    }

    /// <summary>
    /// 显示通用确认模态；多个调用按先进先出顺序依次展示。
    /// </summary>
    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        AppModalKind kind = AppModalKind.Information,
        string confirmButtonText = "确定",
        string cancelButtonText = "取消") =>
        ShowConfirmationAsync(new AppModalOptions(
            title,
            message,
            kind,
            confirmButtonText,
            cancelButtonText));

    public Task<bool> ShowConfirmationAsync(AppModalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var request = new AppModalRequest(options);
        var currentChanged = false;
        var pendingCount = 0;
        lock (syncRoot)
        {
            if (currentRequest is null)
            {
                currentRequest = request;
                currentChanged = true;
            }
            else
            {
                pendingRequests.Enqueue(request);
            }

            pendingCount = pendingRequests.Count;
        }

        if (currentChanged)
        {
            logger?.LogInformation(
                "应用内模态请求已激活 {Kind} {PendingCount}",
                options.Kind,
                pendingCount);
            CurrentRequestChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            logger?.LogInformation(
                "应用内模态请求已排队 {Kind} {PendingCount}",
                options.Kind,
                pendingCount);
        }

        return request.Result;
    }

    /// <summary>
    /// 完成当前请求并自动推进到队列中的下一个请求。
    /// </summary>
    public bool TryCompleteCurrent(bool result)
    {
        AppModalRequest? completedRequest;
        var pendingCount = 0;
        var hasNext = false;
        lock (syncRoot)
        {
            completedRequest = currentRequest;
            if (completedRequest is null)
                return false;

            currentRequest = pendingRequests.Count > 0
                ? pendingRequests.Dequeue()
                : null;
            pendingCount = pendingRequests.Count;
            hasNext = currentRequest is not null;
        }

        completedRequest.TryComplete(result);
        logger?.LogInformation(
            "应用内模态请求已完成 {Kind} {Result} {PendingCount} {HasNext}",
            completedRequest.Options.Kind,
            result,
            pendingCount,
            hasNext);
        CurrentRequestChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
