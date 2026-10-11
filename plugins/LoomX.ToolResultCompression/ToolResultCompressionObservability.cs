namespace LoomX.ToolResultCompression;

/// <summary>线程安全的进程内观测状态，只接收计数，不保存业务数据。</summary>
public sealed class ToolResultCompressionObservability
{
    /// <summary>落盘节流间隔：统计写入不必逐请求执行，避免为计数器引入 IO 抖动。</summary>
    private const long FlushIntervalMilliseconds = 10_000;

    private readonly object gate = new();
    private readonly List<CompressionStatsEntry> recent = [];

    private CompressionStatsStore? store;

    // 初始即视为「已到期」：本次运行第一次有压缩结果就落盘一次，之后按节流间隔写入。
    private long lastFlushTick = Environment.TickCount64 - FlushIntervalMilliseconds;

    private long totalRequests;
    private long compressedRequests;
    private long compressedToolResults;
    private long originalChars;
    private long compressedChars;
    private long estimatedOriginalTokens;
    private long estimatedCompressedTokens;
    private long errors;

    public event EventHandler? Changed;

    /// <summary>
    /// 绑定统计存储并载入历史累计值。设置页展示的是跨会话的长期收益，
    /// 不绑定存储时退化为纯进程内计数。
    /// </summary>
    public void AttachStore(CompressionStatsStore? statsStore)
    {
        lock (gate)
        {
            store = statsStore;
            if (store is null) return;

            var persisted = store.Load();
            totalRequests = persisted.TotalRequests;
            compressedRequests = persisted.CompressedRequests;
            compressedToolResults = persisted.CompressedToolResults;
            originalChars = persisted.OriginalChars;
            compressedChars = persisted.CompressedChars;
            estimatedOriginalTokens = persisted.EstimatedOriginalTokens;
            estimatedCompressedTokens = persisted.EstimatedCompressedTokens;
            errors = persisted.Errors;
            recent.Clear();
            recent.AddRange(persisted.RecentEntries);
        }
    }

    public CompressionStats Snapshot()
    {
        lock (gate)
        {
            return new CompressionStats(
                Interlocked.Read(ref totalRequests),
                Interlocked.Read(ref compressedRequests),
                Interlocked.Read(ref compressedToolResults),
                Interlocked.Read(ref originalChars),
                Interlocked.Read(ref compressedChars),
                Interlocked.Read(ref estimatedOriginalTokens),
                Interlocked.Read(ref estimatedCompressedTokens),
                Interlocked.Read(ref errors),
                recent.ToArray());
        }
    }

    /// <summary>通知宿主快照失效；配置变化后由插件主动调用。</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void RecordRequest(ToolResultRewriteReport? report)
    {
        lock (gate)
        {
            Interlocked.Increment(ref totalRequests);
            if (report is null) return;

            Interlocked.Add(ref originalChars, report.OriginalChars);
            Interlocked.Add(ref compressedChars, report.CompressedChars);
            Interlocked.Add(ref estimatedOriginalTokens, report.EstimatedOriginalTokens);
            Interlocked.Add(ref estimatedCompressedTokens, report.EstimatedCompressedTokens);
            if (report.CompressedToolResults <= 0) return;

            Interlocked.Increment(ref compressedRequests);
            Interlocked.Add(ref compressedToolResults, report.CompressedToolResults);
            AddRecent(CreateEntry(report));
            FlushIfDue();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RecordRequest()
    {
        lock (gate)
            Interlocked.Increment(ref totalRequests);
    }

    public void RecordError()
    {
        lock (gate)
        {
            Interlocked.Increment(ref errors);
            Flush(force: false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>清零累计统计与最近记录，并立即落盘。</summary>
    public void Reset()
    {
        lock (gate)
        {
            totalRequests = 0;
            compressedRequests = 0;
            compressedToolResults = 0;
            originalChars = 0;
            compressedChars = 0;
            estimatedOriginalTokens = 0;
            estimatedCompressedTokens = 0;
            errors = 0;
            recent.Clear();
            Flush(force: true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static CompressionStatsEntry CreateEntry(ToolResultRewriteReport report) => new(
        DateTimeOffset.Now,
        report.CompressedToolResults,
        report.SavedChars,
        report.SavedTokens,
        report.OriginalChars <= 0
            ? 0
            : Math.Clamp((double)report.SavedChars / report.OriginalChars, 0, 1));

    private void AddRecent(CompressionStatsEntry entry)
    {
        recent.Insert(0, entry);
        if (recent.Count > 12) recent.RemoveRange(12, recent.Count - 12);
    }

    private CompressionStats CurrentUnsafe() => new(
        Interlocked.Read(ref totalRequests),
        Interlocked.Read(ref compressedRequests),
        Interlocked.Read(ref compressedToolResults),
        Interlocked.Read(ref originalChars),
        Interlocked.Read(ref compressedChars),
        Interlocked.Read(ref estimatedOriginalTokens),
        Interlocked.Read(ref estimatedCompressedTokens),
        Interlocked.Read(ref errors),
        recent.ToArray());

    private void FlushIfDue() => Flush(force: false);

    private void Flush(bool force)
    {
        var now = Environment.TickCount64;
        if (!force && now - lastFlushTick < FlushIntervalMilliseconds) return;

        lastFlushTick = now;
        store?.Save(CurrentUnsafe());
    }
}
