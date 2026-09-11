using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// 单飞防抖自动保存器：合并短时间内的连续触发；保存过程严格串行，
/// 保存期间的新触发会在当前保存结束后补存一次，保证最后一次编辑一定落库。
/// </summary>
public sealed class DebouncedAutoSaver : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(350);

    private readonly Func<Task> saveAction;
    private readonly TimeSpan delay;
    private readonly ILogger? logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? debounceCancellation;
    private int pendingSave;
    private bool disposed;

    /// <summary>保存动作抛出异常时触发，参数为该异常；保存器本身不中断，后续触发仍会继续尝试。</summary>
    public event EventHandler<Exception>? SaveFaulted;

    public DebouncedAutoSaver(Func<Task> saveAction, TimeSpan? delay = null, ILogger? logger = null)
    {
        this.saveAction = saveAction;
        this.delay = delay ?? DefaultDelay;
        this.logger = logger;
    }

    /// <summary>重新启动防抖计时，delay 到期后执行一次保存。</summary>
    public void Trigger()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelPendingDebounce();
        debounceCancellation = new CancellationTokenSource();
        _ = DelayThenSaveAsync(debounceCancellation.Token);
    }

    /// <summary>取消防抖等待并立即保存；若已有保存在执行则登记补存后返回。</summary>
    public Task FlushAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelPendingDebounce();
        return SaveCoreAsync();
    }

    private async Task DelayThenSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await SaveCoreAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task SaveCoreAsync()
    {
        if (disposed) return;
        if (!gate.Wait(0))
        {
            // 已有保存在执行：只登记一次补存，结束后会用最新状态再保存，绝不丢弃。
            Interlocked.Exchange(ref pendingSave, 1);
            return;
        }
        try
        {
            do
            {
                Interlocked.Exchange(ref pendingSave, 0);
                try
                {
                    await saveAction();
                }
                catch (Exception exception)
                {
                    logger?.LogError(exception, "自动保存执行失败");
                    SaveFaulted?.Invoke(this, exception);
                }
            }
            while (Interlocked.Exchange(ref pendingSave, 0) == 1);
        }
        finally { gate.Release(); }
    }

    private void CancelPendingDebounce()
    {
        debounceCancellation?.Cancel();
        debounceCancellation?.Dispose();
        debounceCancellation = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelPendingDebounce();
    }
}
