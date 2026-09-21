namespace LoomX.Models;

/// <summary>
/// 应用内模态的视觉语义。
/// </summary>
public enum AppModalKind
{
    Information,
    Warning
}

/// <summary>
/// 通用确认模态所需的安全展示信息。
/// </summary>
public sealed record AppModalOptions(
    string Title,
    string Message,
    AppModalKind Kind = AppModalKind.Information,
    string ConfirmButtonText = "确定",
    string CancelButtonText = "取消");

/// <summary>
/// 当前正由宿主显示的模态请求。调用方通过服务返回的 Task 获取结果。
/// </summary>
public sealed class AppModalRequest
{
    private readonly TaskCompletionSource<bool> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal AppModalRequest(AppModalOptions options)
    {
        Options = options;
    }

    public AppModalOptions Options { get; }

    internal Task<bool> Result => completion.Task;

    internal bool TryComplete(bool result) => completion.TrySetResult(result);
}
