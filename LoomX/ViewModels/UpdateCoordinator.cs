using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX.Localization;
using LoomX.Services;

namespace LoomX.ViewModels;

public enum UpdateStage
{
    Idle,
    Checking,
    Downloading,
    Verifying,
    Ready,
    Installing,
    Latest,
    Error
}

public enum UpdateErrorKind
{
    None,
    Check,
    Prepare,
    Install
}

/// <summary>统一管理启动、定时、准备与安装确认，并向所有更新入口提供同一份状态。</summary>
public sealed class UpdateCoordinator : NotifyViewModel, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly AppDataStore dataStore;
    private readonly IUpdateService updateService;
    private readonly ILogger<UpdateCoordinator> logger;
    private readonly Action requestApplicationExit;
    private readonly IStringLocalizer<UpdateCoordinator> localizer;
    private readonly Action<Action> dispatch;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object checkSync = new();
    private readonly object prepareSync = new();
    private Task<UpdateCheckResult?>? checkTask;
    private Task? prepareTask;
    private Task? timerTask;
    private UpdateRelease? release;
    private PreparedUpdate? preparedUpdate;
    private UpdateStage stage;
    private UpdateErrorKind errorKind;
    private bool started;
    private bool isDialogVisible;
    private bool disposed;
    private int installStarted;
    private string statusText = string.Empty;
    private string errorMessage = string.Empty;
    private string updateEntryText = string.Empty;
    private int downloadPercent;
    private long downloadedBytes;
    private long totalBytes;
    private long bytesPerSecond;

    public UpdateCoordinator(
        AppDataStore dataStore,
        IUpdateService? updateService = null,
        ILogger<UpdateCoordinator>? logger = null,
        Action? requestApplicationExit = null,
        IStringLocalizer<UpdateCoordinator>? localizer = null,
        Action<Action>? dispatch = null)
    {
        this.dataStore = dataStore;
        this.updateService = updateService ?? new UpdateService(logger: null, currentVersion: CurrentVersion);
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateCoordinator>.Instance;
        this.requestApplicationExit = requestApplicationExit ?? (() => { });
        this.localizer = localizer ?? LocalizerFactory.Create<UpdateCoordinator>();
        this.dispatch = dispatch ?? DispatchToUiThread;

        CheckCommand = new AsyncCommand(() => CheckNowAsync(true), () => !IsBusy, this.logger);
        ToggleDialogCommand = new DelegateCommand(ToggleDialog);
        DismissDialogCommand = new DelegateCommand(() => SetDialogVisible(false));
        LaterCommand = DismissDialogCommand;
        RetryCommand = new AsyncCommand(RetryAsync, () => CanRetry, this.logger);
        InstallAndRestartCommand = new AsyncCommand(InstallAndRestartAsync, () => CanInstall, this.logger);
        RefreshLocalizedText();
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public string CurrentVersion => AppVersion.Current;
    public UpdateRelease? Release => release;
    public PreparedUpdate? PreparedUpdate => preparedUpdate;
    public UpdateStage Stage => stage;
    public UpdateErrorKind ErrorKind => errorKind;
    public bool HasUpdate => release is not null;
    public string LatestVersion => release is null ? string.Empty : $"v{release.Version}";
    public string VersionComparison => release is null ? string.Empty : $"v{CurrentVersion} → v{release.Version}";
    public string ReleaseTitle => release?.Name ?? string.Empty;
    public string ReleaseUrl => release?.HtmlUrl ?? string.Empty;
    public string StatusText => statusText;
    public string ErrorMessage => errorMessage;
    public string UpdateEntryText => updateEntryText;
    public int DownloadPercent => downloadPercent;
    public string DownloadedText => FormatBytes(downloadedBytes);
    public string TotalText => totalBytes > 0 ? FormatBytes(totalBytes) : Loc("update.progress.unknown", "未知");
    public string ProgressText => $"{DownloadedText} / {TotalText}";
    public string SpeedText => bytesPerSecond > 0 ? $"{FormatBytes(bytesPerSecond)}/{Loc("update.progress.second", "秒")}" : Loc("update.progress.calculating", "计算中");
    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Installing;
    public bool IsUpdateEntryVisible => Stage is UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Ready
        || Stage == UpdateStage.Error && Release is not null;
    public bool IsDialogVisible => isDialogVisible;
    public bool IsProgressVisible => Stage is UpdateStage.Downloading or UpdateStage.Verifying;
    public bool IsProgressIndeterminate => Stage == UpdateStage.Verifying;
    public bool CanInstall => Stage == UpdateStage.Ready && PreparedUpdate is not null;
    public bool CanRetry => Stage == UpdateStage.Error && ErrorKind is UpdateErrorKind.Check or UpdateErrorKind.Prepare;

    public ICommand CheckCommand { get; }
    public ICommand ToggleDialogCommand { get; }
    public ICommand DismissDialogCommand { get; }
    public ICommand LaterCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand InstallAndRestartCommand { get; }

    public void Start()
    {
        if (started) return;
        started = true;
        timerTask = RunTimerAsync(lifetime.Token);
    }

    public Task<UpdateCheckResult?> CheckNowAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        lock (checkSync)
        {
            if (checkTask is { IsCompleted: false }) return checkTask;
            if (prepareTask is { IsCompleted: false } && release is not null)
                return Task.FromResult<UpdateCheckResult?>(new UpdateCheckResult(CurrentVersion, release));
            if (Stage == UpdateStage.Installing) return Task.FromResult<UpdateCheckResult?>(null);

            checkTask = CheckCoreAsync(manual, cancellationToken);
            return checkTask;
        }
    }

    public void RefreshLocalizedText() => dispatch(() =>
    {
        var nextStatus = ResolveStatusText();
        SetText(ref statusText, nextStatus, nameof(StatusText));
        SetText(ref updateEntryText, ResolveEntryText(), nameof(UpdateEntryText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SpeedText));
    });

    private async Task<UpdateCheckResult?> CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        try
        {
            TransitionTo(UpdateStage.Checking);
            var settings = await dataStore.GetUpdateProxySettingsAsync(cancellationToken);
            var result = await updateService.CheckAsync(settings, cancellationToken);
            dispatch(() =>
            {
                release = result.Latest;
                preparedUpdate = null;
                OnReleaseChanged();
            });

            if (result.Latest is null)
            {
                SetDialogVisible(false);
                TransitionTo(manual ? UpdateStage.Latest : UpdateStage.Idle);
            }
            else
            {
                SetDialogVisible(true);
                _ = StartPreparation();
            }

            logger.LogInformation("更新检查完成 {Manual} {HasUpdate} {Version}", manual, result.IsAvailable, result.Latest?.Version ?? "无");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Check, Loc("update.error.check", "检查更新失败，请稍后重试。"));
            logger.LogWarning(exception, "更新检查失败 {Manual}", manual);
            return null;
        }
        finally
        {
            lock (checkSync) checkTask = null;
        }
    }

    private Task StartPreparation()
    {
        lock (prepareSync)
        {
            if (prepareTask is { IsCompleted: false }) return prepareTask;
            if (release is null) return Task.CompletedTask;
            prepareTask = PrepareCoreAsync(release, lifetime.Token);
            return prepareTask;
        }
    }

    private async Task PrepareCoreAsync(UpdateRelease targetRelease, CancellationToken cancellationToken)
    {
        try
        {
            ResetProgress();
            TransitionTo(UpdateStage.Downloading);
            var settings = await dataStore.GetUpdateProxySettingsAsync(cancellationToken);
            var progress = new InlineProgress<UpdateDownloadProgress>(ApplyProgress);
            var result = await updateService.PrepareUpdateAsync(targetRelease, settings, progress, cancellationToken);
            dispatch(() =>
            {
                preparedUpdate = result;
                OnPropertyChanged(nameof(PreparedUpdate));
            });
            TransitionTo(UpdateStage.Ready);
            logger.LogInformation("更新包准备完成 {Version}", targetRelease.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!lifetime.IsCancellationRequested)
                TransitionTo(UpdateStage.Error, UpdateErrorKind.Prepare, Loc("update.error.prepare", "更新准备已取消，请重试。"));
        }
        catch (Exception exception)
        {
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Prepare, Loc("update.error.prepare", "更新包准备失败，请重试。"));
            logger.LogWarning(exception, "更新包准备失败 {Version}", targetRelease.Version);
        }
        finally
        {
            lock (prepareSync) prepareTask = null;
        }
    }

    private void ApplyProgress(UpdateDownloadProgress value) => dispatch(() =>
    {
        downloadedBytes = value.Transferred;
        totalBytes = value.Total;
        bytesPerSecond = value.BytesPerSecond;
        downloadPercent = value.Percent;
        OnPropertyChanged(nameof(DownloadPercent));
        OnPropertyChanged(nameof(DownloadedText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SpeedText));
        var progressStage = value.Phase == UpdatePreparationPhase.Verifying ? UpdateStage.Verifying : UpdateStage.Downloading;
        if (Stage != progressStage || ErrorKind != UpdateErrorKind.None) TransitionTo(progressStage);
    });

    private async Task RetryAsync()
    {
        if (!CanRetry) return;
        if (ErrorKind == UpdateErrorKind.Check)
        {
            await CheckNowAsync(true, lifetime.Token);
            return;
        }

        if (ErrorKind == UpdateErrorKind.Prepare) await StartPreparation();
    }

    private Task InstallAndRestartAsync()
    {
        var target = preparedUpdate;
        if (Stage != UpdateStage.Ready || target is null || Interlocked.Exchange(ref installStarted, 1) != 0)
            return Task.CompletedTask;

        try
        {
            TransitionTo(UpdateStage.Installing);
            updateService.LaunchInstaller(target);
            requestApplicationExit();
            logger.LogInformation("更新安装器已启动 {Version}", target.Version);
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref installStarted, 0);
            TransitionTo(UpdateStage.Ready, UpdateErrorKind.Install, Loc("update.error.install", "无法启动更新安装器，请重试。"));
            logger.LogWarning(exception, "更新安装器启动失败 {Version}", target.Version);
        }

        return Task.CompletedTask;
    }

    private void ToggleDialog()
    {
        if (!IsUpdateEntryVisible) return;
        SetDialogVisible(!IsDialogVisible);
    }

    private void SetDialogVisible(bool visible) => dispatch(() =>
    {
        if (SetProperty(ref isDialogVisible, visible, nameof(IsDialogVisible)))
            logger.LogDebug("更新浮窗可见性变化 {Visible} {Stage}", visible, Stage);
    });

    private void TransitionTo(UpdateStage nextStage, UpdateErrorKind nextErrorKind = UpdateErrorKind.None, string? nextError = null) => dispatch(() =>
    {
        var nextErrorMessage = nextError ?? string.Empty;
        if (stage == nextStage && errorKind == nextErrorKind && string.Equals(errorMessage, nextErrorMessage, StringComparison.Ordinal)) return;

        var previous = stage;
        stage = nextStage;
        errorKind = nextErrorKind;
        errorMessage = nextErrorMessage;
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(ErrorKind));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsUpdateEntryVisible));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRetry));
        RefreshLocalizedText();
        RaiseCommandStates();
        logger.LogInformation("更新状态切换 {PreviousStage} {Stage} {ErrorKind} {Version}", previous, nextStage, nextErrorKind, release?.Version ?? "无");
    });

    private string ResolveStatusText()
    {
        if (!string.IsNullOrWhiteSpace(errorMessage)) return errorMessage;
        return Stage switch
        {
            UpdateStage.Checking => Loc("update.status.checking", "正在检查更新…"),
            UpdateStage.Downloading => Loc("update.status.downloading", "正在下载更新…"),
            UpdateStage.Verifying => Loc("update.status.verifying", "正在校验更新包…"),
            UpdateStage.Ready => Loc("update.status.ready", "更新已准备好"),
            UpdateStage.Installing => Loc("update.status.installing", "安装器已启动，应用即将退出…"),
            UpdateStage.Latest => Loc("update.status.latest", "已是最新版本"),
            UpdateStage.Error => Loc("update.status.error", "更新失败"),
            _ => Loc("update.status.idle", "尚未检查更新")
        };
    }

    private string ResolveEntryText() => Stage switch
    {
        UpdateStage.Downloading => Loc("update.entry.downloading", "正在下载更新"),
        UpdateStage.Verifying => Loc("update.entry.verifying", "正在校验更新"),
        UpdateStage.Ready => string.Format(CultureInfo.CurrentCulture, Loc("update.entry.ready", "{0} 已准备好"), LatestVersion),
        UpdateStage.Error when Release is not null => Loc("update.entry.error", "更新需要处理"),
        _ => string.Empty
    };

    private string Loc(string key, string fallback)
    {
        var value = localizer[key];
        return value.ResourceNotFound || string.Equals(value.Value, key, StringComparison.Ordinal) ? fallback : value.Value;
    }

    private void ResetProgress() => dispatch(() =>
    {
        downloadedBytes = 0;
        totalBytes = 0;
        bytesPerSecond = 0;
        downloadPercent = 0;
        OnPropertyChanged(nameof(DownloadPercent));
        OnPropertyChanged(nameof(DownloadedText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SpeedText));
    });

    private void OnReleaseChanged()
    {
        OnPropertyChanged(nameof(Release));
        OnPropertyChanged(nameof(PreparedUpdate));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(VersionComparison));
        OnPropertyChanged(nameof(ReleaseTitle));
        OnPropertyChanged(nameof(ReleaseUrl));
        OnPropertyChanged(nameof(IsUpdateEntryVisible));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRetry));
    }

    private void RaiseCommandStates()
    {
        (CheckCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RetryCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (InstallAndRestartCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dataStore.InitializeAsync(cancellationToken);
            if (dataStore.Settings?.AutoCheckUpdates == true) await CheckNowAsync(false, cancellationToken);

            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (dataStore.Settings?.AutoCheckUpdates == true) await CheckNowAsync(false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogWarning(exception, "更新定时检查循环失败"); }
    }

    private void OnCultureChanged(object? sender, CultureInfo culture) => RefreshLocalizedText();

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
    }

    private void SetText(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        OnPropertyChanged(propertyName);
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
        if (disposed) return;
        disposed = true;
        LocaleService.CultureChanged -= OnCultureChanged;
        lifetime.Cancel();
        try { timerTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        lifetime.Dispose();
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
