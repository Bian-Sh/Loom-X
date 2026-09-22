using Microsoft.Extensions.Logging;

namespace LoomX.Assistant.Browser;

/// <summary>Browser Bridge 可重启生命周期边界。</summary>
public interface IBrowserBridgeLifecycle
{
    bool IsListening { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 以 Assistant Session ID 管理进程内单例 Browser Bridge 的租约。
/// 正常释放只由 AI 主动调用；删除 Session 仅使用同一入口清理意外残留。
/// </summary>
public sealed class BrowserBridgeLeaseManager
{
    private readonly IBrowserBridgeLifecycle lifecycle;
    private readonly ILogger<BrowserBridgeLeaseManager> logger;
    private readonly HashSet<string> activeSessionIds = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);

    public BrowserBridgeLeaseManager(
        IBrowserBridgeLifecycle lifecycle,
        ILogger<BrowserBridgeLeaseManager> logger)
    {
        this.lifecycle = lifecycle;
        this.logger = logger;
    }

    public IReadOnlyCollection<string> ActiveSessionIds
    {
        get
        {
            lock (activeSessionIds)
            {
                return activeSessionIds.ToArray();
            }
        }
    }

    public async Task AcquireAsync(string assistantSessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantSessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            lock (activeSessionIds)
            {
                if (activeSessionIds.Contains(assistantSessionId)) return;
            }

            var shouldStart = false;
            lock (activeSessionIds)
            {
                shouldStart = activeSessionIds.Count == 0;
            }

            if (shouldStart)
            {
                await lifecycle.StartAsync(cancellationToken);
            }

            lock (activeSessionIds)
            {
                activeSessionIds.Add(assistantSessionId);
            }

            logger.LogInformation(
                "Assistant Session 已启用 Browser Bridge {AssistantSessionId} {LeaseCount}",
                assistantSessionId,
                ActiveSessionIds.Count);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseAsync(string assistantSessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantSessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            bool removed;
            bool shouldStop;
            lock (activeSessionIds)
            {
                removed = activeSessionIds.Remove(assistantSessionId);
                shouldStop = removed && activeSessionIds.Count == 0;
            }

            if (!removed) return;
            if (shouldStop)
            {
                await lifecycle.StopAsync(cancellationToken);
            }

            logger.LogInformation(
                "Assistant Session 已释放 Browser Bridge {AssistantSessionId} {LeaseCount}",
                assistantSessionId,
                ActiveSessionIds.Count);
        }
        finally
        {
            gate.Release();
        }
    }
}
