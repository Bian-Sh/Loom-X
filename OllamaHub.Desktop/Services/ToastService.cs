namespace OllamaHub.Desktop.Services;

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

    public void Show(string message, ToastLevel level = ToastLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Requested?.Invoke(this, new ToastNotification(message, level));
    }
}
