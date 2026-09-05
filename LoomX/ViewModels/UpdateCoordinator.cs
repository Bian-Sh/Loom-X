using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using LoomX.Services;

namespace LoomX.ViewModels;

public enum UpdateStage
{
    Idle,
    Checking,
    Available,
    Downloading,
    Verifying,
    Installing,
    Latest,
    Error
}

/// <summary>
/// 统一管理启动、定时和手动更新检查，并向主窗口与设置页提供同一份状态。
/// </summary>
public sealed class UpdateCoordinator : NotifyViewModel, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly AppDataStore dataStore;
    private readonly UpdateService updateService;
    private readonly ILogger<UpdateCoordinator> logger;
    private readonly SemaphoreSlim checkGate = new(1, 1);
    private readonly SemaphoreSlim downloadGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private UpdateRelease? release;
    private Task? timerTask;
    private bool started;
    private bool cardVisible;
    private bool releaseNotesVisible;
    private UpdateStage stage;
    private string statusText = "尚未检查更新";
    private string errorMessage = string.Empty;
    private int downloadPercent;
    private long downloadedBytes;
    private long totalBytes;
    private long bytesPerSecond;

    public string CurrentVersion => AppVersion.Current;
    public UpdateRelease? Release => release;
    public UpdateStage Stage
    {
        get => stage;
        private set
        {
            if (!SetProperty(ref stage, value)) return;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanRetry));
            RaiseCommandStates();
        }
    }
    public bool CardVisible { get => cardVisible; private set => SetProperty(ref cardVisible, value); }
    public bool ReleaseNotesVisible { get => releaseNotesVisible; private set => SetProperty(ref releaseNotesVisible, value); }
    public bool HasUpdate => release is not null;
    public string LatestVersion => release is null ? string.Empty : $"v{release.Version}";
    public string VersionComparison => release is null ? string.Empty : $"v{CurrentVersion} → v{release.Version}";
    public string ReleaseTitle => release?.Name ?? string.Empty;
    public string ReleaseUrl => release?.HtmlUrl ?? string.Empty;
    public string ReleaseNotes => SanitizeMarkdown(release?.Body);
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string ErrorMessage { get => errorMessage; private set => SetProperty(ref errorMessage, value); }
    public int DownloadPercent { get => downloadPercent; private set => SetProperty(ref downloadPercent, value); }
    public string DownloadedText => FormatBytes(downloadedBytes);
    public string TotalText => totalBytes > 0 ? FormatBytes(totalBytes) : "未知";
    public string ProgressText => $"{DownloadedText} / {TotalText}";
    public string SpeedText => bytesPerSecond > 0 ? $"{FormatBytes(bytesPerSecond)}/秒" : "计算中";
    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Installing;
    public bool CanDownload => HasUpdate && (Stage is UpdateStage.Available or UpdateStage.Error);
    public bool CanRetry => HasUpdate && Stage == UpdateStage.Error;

    public ICommand CheckCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand LaterCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }
    public ICommand CloseReleaseNotesCommand { get; }

    public UpdateCoordinator(AppDataStore dataStore, UpdateService? updateService = null, ILogger<UpdateCoordinator>? logger = null)
    {
        this.dataStore = dataStore;
        this.updateService = updateService ?? new UpdateService(logger: null, currentVersion: CurrentVersion);
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateCoordinator>.Instance;
        CheckCommand = new AsyncCommand(() => CheckNowAsync(true), () => !IsBusy);
        DownloadCommand = new AsyncCommand(DownloadAndInstallAsync, () => CanDownload);
        LaterCommand = new DelegateCommand(() => CardVisible = false);
        OpenReleaseNotesCommand = new DelegateCommand(() => ReleaseNotesVisible = true);
        CloseReleaseNotesCommand = new DelegateCommand(() => ReleaseNotesVisible = false);
    }

    public void Start()
    {
        if (started) return;
        started = true;
        timerTask = RunTimerAsync(lifetime.Token);
    }

    public async Task<UpdateCheckResult?> CheckNowAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        if (Stage is UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Installing) return null;
        if (!await checkGate.WaitAsync(0, cancellationToken)) return null;
        try
        {
            Stage = UpdateStage.Checking;
            StatusText = "正在检查更新…";
            ErrorMessage = string.Empty;
            var settings = await dataStore.GetUpdateProxySettingsAsync(cancellationToken);
            var result = await updateService.CheckAsync(settings, cancellationToken);
            release = result.Latest;
            OnReleaseChanged();
            if (release is null)
            {
                Stage = UpdateStage.Latest;
                StatusText = "已是最新版本";
                CardVisible = false;
            }
            else
            {
                Stage = UpdateStage.Available;
                StatusText = $"发现新版本 {LatestVersion}";
                CardVisible = true;
            }
            logger.LogInformation("更新状态刷新完成 {Manual} {Stage} {LatestVersion}", manual, Stage, release?.Version ?? "无");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Stage = UpdateStage.Error;
            ErrorMessage = "检查更新失败，请稍后重试。";
            StatusText = manual ? ErrorMessage : "更新检查失败";
            logger.LogWarning(exception, "更新状态刷新失败 {Manual}", manual);
            return null;
        }
        finally
        {
            checkGate.Release();
            RaiseCommandStates();
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        if (release is null || !await downloadGate.WaitAsync(0)) return;
        try
        {
            Stage = UpdateStage.Downloading;
            StatusText = "正在下载更新…";
            ErrorMessage = string.Empty;
            downloadedBytes = totalBytes = bytesPerSecond = 0;
            OnPropertyChanged(nameof(DownloadedText));
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(SpeedText));
            var settings = await dataStore.GetUpdateProxySettingsAsync(lifetime.Token);
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                downloadedBytes = value.Transferred;
                totalBytes = value.Total;
                bytesPerSecond = value.BytesPerSecond;
                DownloadPercent = value.Percent;
                OnPropertyChanged(nameof(DownloadedText));
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(SpeedText));
                if (value.Percent >= 100 && Stage == UpdateStage.Downloading)
                {
                    Stage = UpdateStage.Verifying;
                    StatusText = "正在校验更新包…";
                }
            });
            await updateService.DownloadAndInstallAsync(release, settings, progress, lifetime.Token);
            Stage = UpdateStage.Installing;
            StatusText = "安装器已启动，应用即将重启…";
            CardVisible = true;
            logger.LogInformation("更新安装流程已启动 {Version}", release.Version);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            Stage = UpdateStage.Error;
            ErrorMessage = "更新已取消。";
            StatusText = ErrorMessage;
        }
        catch (Exception exception)
        {
            Stage = UpdateStage.Error;
            ErrorMessage = "下载或安装失败，请重试。";
            StatusText = ErrorMessage;
            logger.LogWarning(exception, "更新下载或安装失败 {Version}", release?.Version ?? "未知");
        }
        finally
        {
            downloadGate.Release();
            RaiseCommandStates();
        }
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dataStore.InitializeAsync(cancellationToken);
            if (dataStore.Settings?.AutoCheckUpdates == true)
                await CheckNowAsync(false, cancellationToken);

            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (dataStore.Settings?.AutoCheckUpdates == true)
                    await CheckNowAsync(false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogWarning(exception, "更新定时检查循环失败"); }
    }

    private void OnReleaseChanged()
    {
        OnPropertyChanged(nameof(Release));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(VersionComparison));
        OnPropertyChanged(nameof(ReleaseTitle));
        OnPropertyChanged(nameof(ReleaseUrl));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanRetry));
    }

    private void RaiseCommandStates()
    {
        (CheckCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (DownloadCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private static string SanitizeMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "暂无更新说明。";
        var text = Regex.Replace(markdown, @"<[^>]*>", string.Empty);
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        text = Regex.Replace(text, @"^\s*#{1,6}\s*", string.Empty, RegexOptions.Multiline);
        return text.Trim();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var value = bytes / 1024d;
        if (value < 1024) return $"{value:0.0} KB";
        value /= 1024d;
        if (value < 1024) return $"{value:0.0} MB";
        return $"{value / 1024d:0.00} GB";
    }

    public void Dispose()
    {
        lifetime.Cancel();
        try { timerTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        lifetime.Dispose();
        checkGate.Dispose();
        downloadGate.Dispose();
    }
}
