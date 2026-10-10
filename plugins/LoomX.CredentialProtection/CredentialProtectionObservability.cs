namespace LoomX.CredentialProtection;

/// <summary>凭据保护当前进程内的安全计数快照，不包含任何请求或凭据内容。</summary>
public sealed record CredentialProtectionSnapshot(
    long TotalRequests,
    long SanitizedRequests,
    long SanitizedTerms,
    long RestoredResponses,
    long Errors);

/// <summary>线程安全的进程内观测状态，只接收计数，不保存业务数据。</summary>
public sealed class CredentialProtectionObservability
{
    private long totalRequests;
    private long sanitizedRequests;
    private long sanitizedTerms;
    private long restoredResponses;
    private long errors;
    private long protectionEnabled = 1;

    public event EventHandler? Changed;

    public CredentialProtectionSnapshot Snapshot() => new(
        Interlocked.Read(ref totalRequests),
        Interlocked.Read(ref sanitizedRequests),
        Interlocked.Read(ref sanitizedTerms),
        Interlocked.Read(ref restoredResponses),
        Interlocked.Read(ref errors));

    public void RecordRequest(int sanitizedTermCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sanitizedTermCount);
        Interlocked.Increment(ref totalRequests);
        if (sanitizedTermCount > 0)
        {
            Interlocked.Increment(ref sanitizedRequests);
            Interlocked.Add(ref sanitizedTerms, sanitizedTermCount);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordRestoredResponse()
    {
        Interlocked.Increment(ref restoredResponses);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordError()
    {
        Interlocked.Increment(ref errors);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>记录主动保护启停切换（只记录状态，不含任何业务内容）。</summary>
    public void RecordProtectionToggled(bool enabled)
    {
        Interlocked.Exchange(ref protectionEnabled, enabled ? 1 : 0);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>触发一次快照失效通知（规则变更时使用）。</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}