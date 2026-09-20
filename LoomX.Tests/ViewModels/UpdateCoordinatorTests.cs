using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.ViewModels;

public sealed class UpdateCoordinatorTests
{
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

    private static PreparedUpdate CreatePreparedUpdate() =>
        new("9.9.9", Path.Combine(Path.GetTempPath(), "LoomXSetup.exe"), DateTimeOffset.UtcNow);

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

        public UpdateCoordinator CreateCoordinator(FakeUpdateService service, Action? requestExit = null) =>
            new(dataStore, service, requestApplicationExit: requestExit, dispatch: action => action());

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
