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
    Install,
    Exit
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
    public string StatusText => ResolveStatusText();
    public string ErrorMessage => ResolveErrorText();
    public string UpdateEntryText => ResolveEntryText();
    public string UpdateEntryHint => ResolveEntryHint();
    public int DownloadPercent => downloadPercent;
    public string DownloadedText => FormatBytes(downloadedBytes);
    public string TotalText => totalBytes > 0 ? FormatBytes(totalBytes) : Loc("update.progress.unknown");
    public string ProgressText => $"{DownloadedText} / {TotalText}";
    public string SpeedText => bytesPerSecond > 0 ? $"{FormatBytes(bytesPerSecond)}/{Loc("update.progress.second")}" : Loc("update.progress.calculating");
    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Installing;
    public bool IsUpdateEntryVisible => Stage is UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Ready
        || Stage == UpdateStage.Error && Release is not null;
    public bool IsDialogVisible => isDialogVisible;
    public bool IsProgressVisible => Stage is UpdateStage.Downloading or UpdateStage.Verifying;
    public bool IsProgressIndeterminate => Stage == UpdateStage.Verifying;
    public bool CanInstall => Stage == UpdateStage.Ready && PreparedUpdate is not null && Volatile.Read(ref installStarted) == 0;
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
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(UpdateEntryText));
        OnPropertyChanged(nameof(UpdateEntryHint));
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
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Check);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "check");
            logger.LogWarning(diagnostic, "更新检查失败 {Manual} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                manual,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
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
                TransitionTo(UpdateStage.Error, UpdateErrorKind.Prepare);
        }
        catch (Exception exception)
        {
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Prepare);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "prepare");
            logger.LogWarning(diagnostic, "更新包准备失败 {Version} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                targetRelease.Version,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
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
        OnPropertyChanged(nameof(UpdateEntryHint));
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
        var targetRelease = release;
        if (Stage != UpdateStage.Ready || target is null) return Task.CompletedTask;
        if (targetRelease is null || !string.Equals(target.Version, targetRelease.Version, StringComparison.OrdinalIgnoreCase))
        {
            dispatch(() =>
            {
                preparedUpdate = null;
                OnPropertyChanged(nameof(PreparedUpdate));
            });
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Prepare);
            logger.LogWarning("准备完成的更新版本与当前发布版本不一致 {PreparedVersion} {ReleaseVersion}",
                target.Version, targetRelease?.Version ?? "none");
            return Task.CompletedTask;
        }

        if (Interlocked.Exchange(ref installStarted, 1) != 0) return Task.CompletedTask;
        TransitionTo(UpdateStage.Installing);

        try
        {
            updateService.LaunchInstaller(target);
        }
        catch (InvalidPreparedUpdateException exception)
        {
            Interlocked.Exchange(ref installStarted, 0);
            dispatch(() =>
            {
                preparedUpdate = null;
                OnPropertyChanged(nameof(PreparedUpdate));
            });
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Prepare);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "install-validate");
            logger.LogWarning(diagnostic, "更新安装器启动前验证失败 {Version} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                target.Version,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref installStarted, 0);
            TransitionTo(UpdateStage.Ready, UpdateErrorKind.Install);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "install");
            logger.LogWarning(diagnostic, "更新安装器启动失败 {Version} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                target.Version,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
            return Task.CompletedTask;
        }

        try
        {
            logger.LogInformation("更新安装器已启动 {Version}", target.Version);
        }
        catch
        {
            // 安装器已成功启动，非关键日志失败不能释放一次性闩锁。
        }

        try
        {
            requestApplicationExit();
        }
        catch (Exception exception)
        {
            TransitionTo(UpdateStage.Error, UpdateErrorKind.Exit);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "exit-after-install");
            logger.LogWarning(diagnostic, "更新安装器已启动但应用退出失败 {Version} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                target.Version,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
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

    private void TransitionTo(UpdateStage nextStage, UpdateErrorKind nextErrorKind = UpdateErrorKind.None) => dispatch(() =>
    {
        if (stage == nextStage && errorKind == nextErrorKind) return;

        var previous = stage;
        stage = nextStage;
        errorKind = nextErrorKind;
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
        var error = ResolveErrorText();
        if (!string.IsNullOrWhiteSpace(error)) return error;
        return Stage switch
        {
            UpdateStage.Checking => Loc("update.status.checking"),
            UpdateStage.Downloading => Loc("update.status.downloading"),
            UpdateStage.Verifying => Loc("update.status.verifying"),
            UpdateStage.Ready => Loc("update.status.ready"),
            UpdateStage.Installing => Loc("update.status.installing"),
            UpdateStage.Latest => Loc("update.status.latest"),
            UpdateStage.Error => Loc("update.status.error"),
            _ => Loc("update.status.idle")
        };
    }

    private string ResolveEntryText() => Stage switch
    {
        UpdateStage.Downloading => Loc("update.entry.downloading"),
        UpdateStage.Verifying => Loc("update.entry.verifying"),
        UpdateStage.Ready => string.Format(CultureInfo.CurrentCulture, Loc("update.entry.ready"), LatestVersion),
        UpdateStage.Error when Release is not null => Loc("update.entry.error"),
        _ => string.Empty
    };

    private string ResolveEntryHint() => Stage switch
    {
        UpdateStage.Downloading or UpdateStage.Verifying => $"{downloadPercent}%",
        UpdateStage.Ready => Loc("update.entry.install"),
        UpdateStage.Error when Release is not null => Loc("update.entry.retry"),
        _ => string.Empty
    };

    private string ResolveErrorText() => ErrorKind switch
    {
        UpdateErrorKind.Check => Loc("update.error.check"),
        UpdateErrorKind.Prepare => Loc("update.error.prepare"),
        UpdateErrorKind.Install => Loc("update.error.install"),
        UpdateErrorKind.Exit => Loc("update.error.exit"),
        _ => string.Empty
    };

    private string Loc(string key)
    {
        var value = localizer[key];
        return value.ResourceNotFound || string.Equals(value.Value, key, StringComparison.Ordinal) ? key : value.Value;
    }

    private void ResetProgress() => dispatch(() =>
    {
        downloadedBytes = 0;
        totalBytes = 0;
        bytesPerSecond = 0;
        downloadPercent = 0;
        OnPropertyChanged(nameof(DownloadPercent));
        OnPropertyChanged(nameof(UpdateEntryHint));
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
        catch (Exception exception)
        {
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "scheduled-check");
            logger.LogWarning(diagnostic, "更新定时检查循环失败 {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
        }
    }

    private void OnCultureChanged(object? sender, CultureInfo culture) => RefreshLocalizedText();

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
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
