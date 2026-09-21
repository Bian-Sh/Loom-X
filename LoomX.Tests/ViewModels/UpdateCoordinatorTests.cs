using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
using LoomX.Localization;
using LoomX.Services;
using LoomX.Tests.Logging;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.ViewModels;

public sealed class UpdateCoordinatorTests
{
    [Fact]
    public async Task Release正文与异常响应不会进入协调器日志()
    {
        const string releaseBody = "release-body-secret";
        const string apiKey = "test-api-key-secret";
        const string responseBody = "response-body-secret";
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService
        {
            CheckResult = new UpdateCheckResult(AppVersion.Current, new UpdateRelease(
                "v9.9.9", "9.9.9", "Loom-X 9.9.9", $"{releaseBody} {apiKey}",
                "https://example.com/releases/9.9.9", DateTimeOffset.UtcNow, [],
                new UpdateAsset("LoomXSetup.exe", "https://example.com/LoomXSetup.exe", 100, "application/octet-stream"),
                new UpdateAsset("LoomXSetup.exe.sha256", "https://example.com/LoomXSetup.exe.sha256", 64, "text/plain")))
        };
        var failure = new HttpRequestException(
            $"{responseBody} {apiKey}",
            new InvalidOperationException("Authorization bearer-secret"),
            System.Net.HttpStatusCode.BadGateway);
        service.EnqueuePreparation().SetException(failure);
        var logger = new RecordingLogger<UpdateCoordinator>();
        using var coordinator = fixture.CreateCoordinator(service, logger: logger);

        await coordinator.CheckNowAsync(false);
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Error);

        var logs = string.Join("\n", logger.Messages);
        var warning = Assert.Single(logger.Entries, entry => entry.Exception is not null);
        Assert.Contains("9.9.9", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(releaseBody, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(responseBody, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", logs, StringComparison.Ordinal);
        var diagnostic = Assert.IsType<SafeUpdateDiagnosticException>(warning.Exception);
        Assert.Null(diagnostic.InnerException);
        Assert.Equal(failure.HResult, diagnostic.HResult);
        Assert.Contains("PrepareCoreAsync", warning.Exception.StackTrace, StringComparison.Ordinal);
        Assert.Equal(typeof(HttpRequestException).FullName, warning.Properties["ExceptionType"]);
        Assert.Equal(failure.HResult, warning.Properties["HResult"]);
        Assert.Equal((int)System.Net.HttpStatusCode.BadGateway, warning.Properties["HttpStatusCode"]);
        Assert.Equal("prepare", warning.Properties["Stage"]);
    }

    [Fact]
    public async Task 文化切换重新计算状态进度和错误摘要()
    {
        var originalCulture = LocaleService.CurrentCulture.Name;
        try
        {
            LocaleService.SetCulture("en-US");
            await using var fixture = await CoordinatorFixture.CreateAsync();
            var service = new FakeUpdateService { CheckException = new InvalidOperationException("private-response") };
            using var coordinator = fixture.CreateCoordinator(service);

            await coordinator.CheckNowAsync(true);
            var englishStatus = coordinator.StatusText;
            var englishError = coordinator.ErrorMessage;
            var englishTotal = coordinator.TotalText;

            LocaleService.SetCulture("zh-CN");

            Assert.NotEqual(englishStatus, coordinator.StatusText);
            Assert.NotEqual(englishError, coordinator.ErrorMessage);
            Assert.NotEqual(englishTotal, coordinator.TotalText);
            Assert.Equal(ResourceLookup.Resolve("update.error.check", LocaleService.CurrentCulture), coordinator.ErrorMessage);
        }
        finally
        {
            LocaleService.SetCulture(originalCulture);
        }
    }

    [Fact]
    public void 更新服务程序集不再暴露旧下载并安装兼容名称()
    {
        var legacyMethods = typeof(UpdateService).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
            .Where(method => string.Equals(method.Name, "DownloadAndInstallAsync", StringComparison.Ordinal));

        Assert.Empty(legacyMethods);
    }

    [Fact]
    public async Task 自动检查发现版本后依次进入下载校验和就绪()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var preparation = service.EnqueuePreparation();
        using var coordinator = fixture.CreateCoordinator(service);
        var stages = new List<UpdateStage>();
        coordinator.PropertyChanged += (_, args) => RecordStage(args, coordinator, stages);

        var result = await coordinator.CheckNowAsync(false);
        await service.PrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(result?.Latest);
        Assert.True(coordinator.IsDialogVisible);
        Assert.Equal(UpdateStage.Downloading, coordinator.Stage);
        service.ReportVerifying();
        Assert.Equal(UpdateStage.Verifying, coordinator.Stage);
        preparation.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        Assert.Contains(UpdateStage.Downloading, stages);
        Assert.Contains(UpdateStage.Verifying, stages);
        Assert.Equal(UpdateStage.Ready, coordinator.Stage);
        Assert.True(coordinator.IsDialogVisible);
        Assert.True(coordinator.CanInstall);
        Assert.False(coordinator.IsProgressVisible);
    }

    [Fact]
    public async Task 重复下载进度不会重复通知相同阶段()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var preparation = service.EnqueuePreparation();
        using var coordinator = fixture.CreateCoordinator(service);

        await coordinator.CheckNowAsync(false);
        await service.PrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stageNotifications = 0;
        coordinator.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateCoordinator.Stage)) stageNotifications++;
        };

        service.ReportDownloading(40);
        service.ReportDownloading(65);

        Assert.Equal(UpdateStage.Downloading, coordinator.Stage);
        Assert.Equal(0, stageNotifications);
        preparation.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);
    }

    [Fact]
    public async Task 并发检查复用同一服务任务()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService { BlockCheck = true };
        service.EnqueuePreparation();
        using var coordinator = fixture.CreateCoordinator(service);

        var first = coordinator.CheckNowAsync(true);
        await service.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = coordinator.CheckNowAsync(true);
        service.CompleteCheck();

        var results = await Task.WhenAll(first, second);
        await service.PrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.CheckCalls);
        Assert.Equal(1, service.PrepareCalls);
        Assert.All(results, result => Assert.NotNull(result?.Latest));
    }

    [Fact]
    public async Task 稍后只隐藏浮窗且不会取消准备任务()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        service.EnqueuePreparation();
        using var coordinator = fixture.CreateCoordinator(service);

        await coordinator.CheckNowAsync(false);
        await service.PrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.DismissDialogCommand.Execute(null);

        Assert.False(coordinator.IsDialogVisible);
        Assert.False(service.PrepareCancellationToken.IsCancellationRequested);
        Assert.Equal(UpdateStage.Downloading, coordinator.Stage);
    }

    [Fact]
    public async Task 准备失败保留版本且重试不重新检查()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var failed = service.EnqueuePreparation();
        var retried = service.EnqueuePreparation();
        using var coordinator = fixture.CreateCoordinator(service);

        await coordinator.CheckNowAsync(false);
        failed.SetException(new InvalidOperationException("准备失败"));
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Error);

        Assert.NotNull(coordinator.Release);
        Assert.Equal(UpdateErrorKind.Prepare, coordinator.ErrorKind);
        Assert.True(coordinator.CanRetry);
        coordinator.RetryCommand.Execute(null);
        await WaitForAsync(() => service.PrepareCalls == 2);
        retried.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        Assert.Equal(1, service.CheckCalls);
        Assert.Equal(2, service.PrepareCalls);
    }

    [Fact]
    public async Task 就绪后重复安装只启动一次且只请求一次退出()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        var exits = 0;
        using var coordinator = fixture.CreateCoordinator(service, () => exits++);
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        coordinator.InstallAndRestartCommand.Execute(null);
        await service.InstallerLaunched.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.LaunchCalls);
        Assert.Equal(1, exits);
        Assert.Equal(UpdateStage.Installing, coordinator.Stage);
    }

    [Fact]
    public async Task 退出回调失败后不会再次启动安装器且安装命令保持关闭()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        var exits = 0;
        using var coordinator = fixture.CreateCoordinator(service, () =>
        {
            exits++;
            throw new InvalidOperationException("退出失败");
        });
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        await WaitForAsync(() => service.LaunchCalls == 1 && coordinator.Stage == UpdateStage.Error);
        coordinator.InstallAndRestartCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(1, service.LaunchCalls);
        Assert.Equal(1, exits);
        Assert.False(coordinator.CanInstall);
        Assert.False(coordinator.InstallAndRestartCommand.CanExecute(null));
        Assert.False(coordinator.CanRetry);
        Assert.Equal(UpdateErrorKind.Exit, coordinator.ErrorKind);
        Assert.Equal(ResourceLookup.Resolve("update.error.exit", LocaleService.CurrentCulture), coordinator.ErrorMessage);
    }

    [Fact]
    public async Task 安装器启动后的成功日志失败仍请求退出且不会重新开放安装命令()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        var exits = 0;
        var logger = new ThrowingLogger<UpdateCoordinator>("更新安装器已启动");
        using var coordinator = fixture.CreateCoordinator(service, () => exits++, logger);
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        await service.InstallerLaunched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        coordinator.InstallAndRestartCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(1, service.LaunchCalls);
        Assert.Equal(1, exits);
        Assert.Equal(UpdateStage.Installing, coordinator.Stage);
        Assert.False(coordinator.CanInstall);
        Assert.False(coordinator.InstallAndRestartCommand.CanExecute(null));
        Assert.False(coordinator.CanRetry);
    }

    [Fact]
    public async Task 准备版本与当前发布版本不一致时拒绝启动并要求重新准备()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        using var coordinator = fixture.CreateCoordinator(service);
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate("9.9.8"));
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Error);

        Assert.Equal(0, service.LaunchCalls);
        Assert.Null(coordinator.PreparedUpdate);
        Assert.False(coordinator.CanInstall);
        Assert.True(coordinator.CanRetry);
        Assert.Equal(UpdateErrorKind.Prepare, coordinator.ErrorKind);
    }

    [Fact]
    public async Task 安装器启动失败不会退出并恢复就绪()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService { LaunchException = new InvalidOperationException("启动失败") };
        var prepared = service.EnqueuePreparation();
        var exits = 0;
        using var coordinator = fixture.CreateCoordinator(service, () => exits++);
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        await WaitForAsync(() => service.LaunchCalls == 1 && coordinator.Stage == UpdateStage.Ready);

        Assert.Equal(0, exits);
        Assert.Equal(UpdateErrorKind.Install, coordinator.ErrorKind);
        Assert.True(coordinator.CanInstall);

        service.LaunchException = null;
        coordinator.InstallAndRestartCommand.Execute(null);
        await WaitForAsync(() => service.LaunchCalls == 2);

        Assert.Equal(1, exits);
        Assert.Equal(UpdateStage.Installing, coordinator.Stage);
    }

    [Fact]
    public async Task 就绪入口直接请求安装确认而不重新打开更新说明()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        var confirmations = 0;
        using var coordinator = fixture.CreateCoordinator(
            service,
            confirmInstall: () =>
            {
                confirmations++;
                return Task.FromResult(false);
            });
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);
        coordinator.DismissDialogCommand.Execute(null);

        coordinator.ToggleDialogCommand.Execute(null);
        await WaitForAsync(() => confirmations == 1);

        Assert.False(coordinator.IsDialogVisible);
        Assert.Equal(0, service.LaunchCalls);
        Assert.Equal(UpdateStage.Ready, coordinator.Stage);
    }

    [Fact]
    public async Task 取消安装确认保持就绪且不启动安装器()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        var exits = 0;
        using var coordinator = fixture.CreateCoordinator(service, () => exits++, confirmInstall: () => Task.FromResult(false));
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(0, service.LaunchCalls);
        Assert.Equal(0, exits);
        Assert.Equal(UpdateStage.Ready, coordinator.Stage);
        Assert.False(coordinator.IsDialogVisible);
        Assert.True(coordinator.CanInstall);
    }

    [Fact]
    public async Task 安装确认进行中重复点击只显示一个确认请求()
    {
        await using var fixture = await CoordinatorFixture.CreateAsync();
        var service = new FakeUpdateService();
        var prepared = service.EnqueuePreparation();
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmations = 0;
        using var coordinator = fixture.CreateCoordinator(
            service,
            confirmInstall: () =>
            {
                confirmations++;
                return confirmation.Task;
            });
        await coordinator.CheckNowAsync(false);
        prepared.SetResult(CreatePreparedUpdate());
        await WaitForAsync(() => coordinator.Stage == UpdateStage.Ready);

        coordinator.InstallAndRestartCommand.Execute(null);
        coordinator.ToggleDialogCommand.Execute(null);
        await WaitForAsync(() => confirmations == 1);
        Assert.False(coordinator.CanInstall);

        confirmation.SetResult(false);
        await WaitForAsync(() => coordinator.CanInstall);

        Assert.Equal(1, confirmations);
        Assert.Equal(0, service.LaunchCalls);
    }

    [Fact]
    public async Task 手动无更新显示最新而自动无更新回到空闲并隐藏入口()
    {
        await using var manualFixture = await CoordinatorFixture.CreateAsync();
        var manualService = new FakeUpdateService { CheckResult = new UpdateCheckResult(AppVersion.Current, null) };
        using var manual = manualFixture.CreateCoordinator(manualService);

        await manual.CheckNowAsync(true);

        Assert.Equal(UpdateStage.Latest, manual.Stage);
        Assert.False(manual.IsUpdateEntryVisible);

        await using var automaticFixture = await CoordinatorFixture.CreateAsync();
        var automaticService = new FakeUpdateService { CheckResult = new UpdateCheckResult(AppVersion.Current, null) };
        using var automatic = automaticFixture.CreateCoordinator(automaticService);

        await automatic.CheckNowAsync(false);

        Assert.Equal(UpdateStage.Idle, automatic.Stage);
        Assert.False(automatic.IsUpdateEntryVisible);
        Assert.False(automatic.IsDialogVisible);
    }

    private static void RecordStage(PropertyChangedEventArgs args, UpdateCoordinator coordinator, ICollection<UpdateStage> stages)
    {
        if (args.PropertyName == nameof(UpdateCoordinator.Stage)) stages.Add(coordinator.Stage);
    }

    private static PreparedUpdate CreatePreparedUpdate(string version = "9.9.9") =>
        new(version, Path.Combine(Path.GetTempPath(), "LoomXSetup.exe"), DateTimeOffset.UtcNow, new string('0', 64), 0);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition(), "等待协调器状态更新超时。");
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly Queue<TaskCompletionSource<PreparedUpdate>> preparations = new();
        private IProgress<UpdateDownloadProgress>? currentProgress;
        private readonly TaskCompletionSource<UpdateCheckResult> blockedCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UpdateCheckResult CheckResult { get; set; } = new(AppVersion.Current, CreateRelease());
        public bool BlockCheck { get; set; }
        public Exception? CheckException { get; set; }
        public Exception? LaunchException { get; set; }
        public int CheckCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public int LaunchCalls { get; private set; }
        public CancellationToken PrepareCancellationToken { get; private set; }
        public TaskCompletionSource<bool> CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> PrepareStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> InstallerLaunched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PreparedUpdate> EnqueuePreparation()
        {
            var source = new TaskCompletionSource<PreparedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            preparations.Enqueue(source);
            return source;
        }

        public async Task<UpdateCheckResult> CheckAsync(UpdateProxySettings settings, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            CheckStarted.TrySetResult(true);
            if (BlockCheck) return await blockedCheck.Task.WaitAsync(cancellationToken);
            if (CheckException is not null) throw CheckException;
            return CheckResult;
        }

        public void CompleteCheck() => blockedCheck.TrySetResult(CheckResult);

        public Task<UpdateReleasePage> GetStableReleasesAsync(UpdateProxySettings settings, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateReleasePage([], page, pageSize, false));

        public async Task<PreparedUpdate> PrepareUpdateAsync(UpdateRelease release, UpdateProxySettings settings, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            PrepareCancellationToken = cancellationToken;
            currentProgress = progress;
            progress?.Report(new UpdateDownloadProgress(25, 100, 25, 10, UpdatePreparationPhase.Downloading));
            PrepareStarted.TrySetResult(true);
            return await preparations.Dequeue().Task.WaitAsync(cancellationToken);
        }

        public void ReportDownloading(int percent) =>
            currentProgress?.Report(new UpdateDownloadProgress(percent, 100, percent, 10, UpdatePreparationPhase.Downloading));

        public void ReportVerifying() =>
            currentProgress?.Report(new UpdateDownloadProgress(100, 100, 100, 10, UpdatePreparationPhase.Verifying));

        public void LaunchInstaller(PreparedUpdate preparedUpdate)
        {
            LaunchCalls++;
            InstallerLaunched.TrySetResult(true);
            if (LaunchException is not null) throw LaunchException;
        }

        private static UpdateRelease CreateRelease() => new(
            "v9.9.9",
            "9.9.9",
            "Loom-X 9.9.9",
            "# 更新说明",
            "https://example.com/releases/9.9.9",
            DateTimeOffset.UtcNow,
            [],
            new UpdateAsset("LoomXSetup.exe", "https://example.com/LoomXSetup.exe", 100, "application/octet-stream"),
            new UpdateAsset("LoomXSetup.exe.sha256", "https://example.com/LoomXSetup.exe.sha256", 64, "text/plain"));
    }

    private sealed class ThrowingLogger<T>(string messagePrefix) : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith(messagePrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("日志写入失败");
        }
    }

    private sealed class CoordinatorFixture : IAsyncDisposable
    {
        private readonly string directory;
        private readonly ConfigSnapshotService configService;
        private readonly GatewayProcessService gatewayService;
        private readonly AppDataStore dataStore;

        private CoordinatorFixture(string directory, ConfigSnapshotService configService, GatewayProcessService gatewayService, AppDataStore dataStore)
        {
            this.directory = directory;
            this.configService = configService;
            this.gatewayService = gatewayService;
            this.dataStore = dataStore;
        }

        public static async Task<CoordinatorFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "LoomX.db");
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using (var db = new ConfigurationDbContext(options)) await ConfigurationDatabase.InitializeAsync(db);
            var configService = new ConfigSnapshotService(databasePath);
            var gatewayService = new GatewayProcessService();
            var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            return new CoordinatorFixture(directory, configService, gatewayService, dataStore);
        }

        public UpdateCoordinator CreateCoordinator(
            FakeUpdateService service,
            Action? requestExit = null,
            Microsoft.Extensions.Logging.ILogger<UpdateCoordinator>? logger = null,
            Func<Task<bool>>? confirmInstall = null) =>
            new(dataStore, service, logger, requestExit, confirmInstall: confirmInstall ?? (() => Task.FromResult(true)), dispatch: action => action());

        public ValueTask DisposeAsync()
        {
            dataStore.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
            try { Directory.Delete(directory, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
