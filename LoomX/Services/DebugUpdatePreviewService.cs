#if DEBUG
namespace LoomX.Services;

/// <summary>仅供 Debug 构建的更新体验可视化预览，不访问网络或启动安装器。</summary>
internal sealed class DebugUpdatePreviewService : IUpdateService
{
    private const string PreviewVariable = "LOOMX_UPDATE_PREVIEW";
    private static readonly string[] HistoryVersions =
    [
        "9.9.0",
        AppVersion.Current,
        "9.8.0",
        "9.7.0",
        "9.6.0",
        "9.5.0",
        "9.4.0",
        "9.3.0",
        "9.2.0",
        "9.1.0",
        "9.0.0",
        "8.9.0",
        "8.8.0",
        "8.7.0",
        "8.6.0"
    ];

    private readonly string mode;
    private int historyRequestCount;

    private DebugUpdatePreviewService(string mode) => this.mode = mode;

    public static IUpdateService CreateFromEnvironment(IUpdateService fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var value = Environment.GetEnvironmentVariable(PreviewVariable)?.Trim().ToLowerInvariant();
        return value is "downloading" or "verifying" or "ready" or "error" or "history-empty"
            ? new DebugUpdatePreviewService(value)
            : fallback;
    }

    public Task<UpdateCheckResult> CheckAsync(
        UpdateProxySettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latest = mode == "history-empty" ? null : CreateRelease(HistoryVersions[0], 0);
        return Task.FromResult(new UpdateCheckResult(AppVersion.Current, latest));
    }

    public Task<UpdateReleasePage> GetStableReleasesAsync(
        UpdateProxySettings settings,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == "history-empty")
            return Task.FromResult(new UpdateReleasePage([], page, pageSize, false));

        if (mode == "error" && Interlocked.Increment(ref historyRequestCount) % 2 == 1)
            throw new InvalidOperationException("Debug 更新历史预览请求失败。");

        var start = Math.Max(0, (page - 1) * pageSize);
        var items = HistoryVersions
            .Skip(start)
            .Take(pageSize)
            .Select((version, index) => CreateRelease(version, start + index))
            .ToArray();
        var hasMore = start + items.Length < HistoryVersions.Length;
        return Task.FromResult(new UpdateReleasePage(items, page, pageSize, hasMore));
    }

    public async Task<PreparedUpdate> PrepareUpdateAsync(
        UpdateRelease release,
        UpdateProxySettings settings,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const long total = 128L * 1024 * 1024;
        if (mode == "downloading")
        {
            progress?.Report(new UpdateDownloadProgress(54L * 1024 * 1024, total, 42, 4L * 1024 * 1024));
            return await WaitForCancellationAsync(cancellationToken);
        }

        if (mode == "verifying")
        {
            progress?.Report(new UpdateDownloadProgress(total, total, 100, 0));
            progress?.Report(new UpdateDownloadProgress(total, total, 100, 0, UpdatePreparationPhase.Verifying));
            return await WaitForCancellationAsync(cancellationToken);
        }

        if (mode == "error")
        {
            progress?.Report(new UpdateDownloadProgress(72L * 1024 * 1024, total, 56, 3L * 1024 * 1024));
            throw new InvalidOperationException("Debug 更新包预览准备失败。");
        }

        progress?.Report(new UpdateDownloadProgress(total, total, 100, 0));
        progress?.Report(new UpdateDownloadProgress(total, total, 100, 0, UpdatePreparationPhase.Verifying));
        return new PreparedUpdate(
            release.Version,
            Path.Combine(Path.GetTempPath(), "LoomXPreview", "LoomXSetup.exe"),
            DateTimeOffset.Now,
            new string('0', 64),
            total);
    }

    public void LaunchInstaller(PreparedUpdate preparedUpdate) =>
        throw new InvalidOperationException("Debug 更新预览不会启动安装器。");

    private static async Task<PreparedUpdate> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new OperationCanceledException(cancellationToken);
    }

    private static UpdateRelease CreateRelease(string version, int index)
    {
        var installer = new UpdateAsset("LoomX-win-x64-setup.exe", "https://example.invalid/LoomX-win-x64-setup.exe", 128L * 1024 * 1024, "application/octet-stream");
        var checksum = new UpdateAsset("LoomX-win-x64-setup.exe.sha256", "https://example.invalid/LoomX-win-x64-setup.exe.sha256", 64, "text/plain");
        return new UpdateRelease(
            $"v{version}",
            version,
            index == 0 ? "Loom-X 更新体验预览" : $"Loom-X v{version}",
            $"## 🐞 修复问题\n\n- 修复透明模式下更新说明可读性\n\n## ✨ 新增功能\n\n- 支持三个模块默认展开并独立折叠\n\n## 🚀 优化改进\n\n- 优化安装确认流程\n- 安全链接：[项目主页](https://github.com/Bian-Sh/Loom-X)",
            "https://github.com/Bian-Sh/Loom-X/releases",
            DateTimeOffset.Now.AddDays(-index),
            [installer, checksum],
            installer,
            checksum);
    }
}
#endif
