namespace LoomX.Services;

public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record ToastNotification(string Message, ToastLevel Level);

public sealed class ToastService
{
    public event EventHandler<ToastNotification>? Requested;

    /// <summary>插件投递隔离故障订阅；健康订阅继续，原异常不离开宿主边界。</summary>
    internal bool TryShowIsolated(string message, ToastLevel level)
    {
        var success = true;
        var notification = new ToastNotification(message, level);
        foreach (var handler in Requested?.GetInvocationList() ?? [])
        {
            try { ((EventHandler<ToastNotification>)handler)(this, notification); }
            catch (Exception) { Requested -= (EventHandler<ToastNotification>)handler; success = false; }
        }
        return success;
    }

    public void Show(string message, ToastLevel level = ToastLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Requested?.Invoke(this, new ToastNotification(message, level));
    }
}
