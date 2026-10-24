using LoomX.Activity;
using LoomX.Assistant;
using LoomX.Configuration;
using LoomX.Services;
using LoomX.Tests.Logging;
using LoomX.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Startup;

/// <summary>
/// 启动链路读库次数回归（desktop-startup-splash：启动初始化必须避免重复读库）。
/// 观测点为 <see cref="ConfigSnapshotService.DatabaseReadCount"/>：断言「编排器 Database 阶段 →
/// Shell 阶段主窗口初始化 → 助手首次模型摘要」整个流程内配置库只被读取一次，
/// 同时验证显式刷新与配置变更事件仍可按需读库。
/// </summary>
[Collection("Avalonia UI")]
public sealed class StartupDatabaseReadOnceTests
{
    [Fact]
    public void 启动链路Shell阶段主窗口初始化不再重复读取配置库()
    {
        // Shell 阶段主窗口初始化 = MainWindowViewModel 构造的全部页面 VM（与 ctor 同一构造顺序与参数形态）。
        // MainWindowViewModel 外壳本身无法在无头测试里构造：导航图标 Geometry 依赖 Avalonia 平台，
        // 且部分 VM 构造路径含 Dispatcher 阻塞等待（无泵环境必死锁）；其配置库相关面（页面 VM 构造
        // 与 memoized InitializeAsync）在此完整覆盖。测试体在无同步上下文的工作线程执行，
        // NullDispatcher 下所有 UI 调度内联，无死锁风险。
        Task.Run(async () =>
        {
            using var fixture = await CreateFixtureAsync();
            // 编排器 Database 阶段：StartupOrchestrator.RunAsync 的第一步即为 AppDataStore.InitializeAsync。
            await fixture.Store.InitializeAsync();
            Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);

            // Shell 阶段：与 App.CompleteStartupAsync 相同的主窗口初始化（autoInitializeDataStore: false）。
            var toast = new ToastService();
            var console = new ConsoleViewModel(toastService: toast, logger: NullLogger<ConsoleViewModel>.Instance);
            var overview = new OverviewViewModel(fixture.GatewayService, fixture.Store, NullLogger<MainWindowViewModel>.Instance);
            var plugins = new PluginsViewModel(fixture.GatewayService, toast, NullLogger<PluginsViewModel>.Instance);
            var providers = new ProvidersViewModel(fixture.Store, toast, NullLogger<ProvidersViewModel>.Instance);
            var gateway = new GatewayViewModel(fixture.Store, toast);
            var activity = new ActivityViewModel(fixture.Store, NullLogger<ActivityViewModel>.Instance);
            var assistant = new AssistantViewModel(fixture.GatewayService, NullLoggerFactory.Instance, toast, dataStore: fixture.Store);
            IUpdateService updateService = new UpdateService(logger: NullLogger<UpdateService>.Instance, currentVersion: AppVersion.Current);
            var updateCoordinator = new UpdateCoordinator(fixture.Store, updateService, NullLogger<UpdateCoordinator>.Instance, dispatch: action => action());
            var releaseHistory = new ReleaseHistoryViewModel(updateService, fixture.Store.GetUpdateProxySettingsAsync, NullLogger<ReleaseHistoryViewModel>.Instance, dispatch: action => action());
            var settings = new SettingsViewModel(
                dataStore: fixture.Store,
                logger: NullLogger<SettingsViewModel>.Instance,
                toastService: toast,
                updateCoordinator: updateCoordinator,
                releaseHistory: releaseHistory,
                localizer: LoomX.Localization.LocalizerFactory.Create<SettingsViewModel>());
            try
            {
                // 等页面首刷全部落地后，配置库仍只被读取过一次；宽限期后复核捕获迟到的无驱动读库。
                await WaitForReadsToSettleAsync(fixture.ConfigService, minimumReads: 1);
                Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);
                await Task.Delay(600);
                Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);
            }
            finally
            {
                settings.Dispose();
                releaseHistory.Dispose();
                updateCoordinator.Dispose();
                assistant.Dispose();
                activity.Dispose();
                gateway.Dispose();
                providers.Dispose();
                plugins.Dispose();
                overview.Dispose();
                console.Dispose();
            }
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public void 启动阶段页面与助手展示的数据与启动快照一致()
    {
        Task.Run(async () =>
        {
            using var fixture = await CreateFixtureAsync();
            await fixture.Store.InitializeAsync();
            Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);

            // Shell 阶段：与主窗口构造相同的页面 VM 首刷（构造器直调路径，不读库）。
            // 启动首刷任务经内部观测点可等待，页面数据断言因此是确定性的。
            var overview = new OverviewViewModel(fixture.GatewayService, fixture.Store, new RecordingLogger<MainWindowViewModel>());
            var providers = new ProvidersViewModel(fixture.Store, logger: new RecordingLogger<ProvidersViewModel>());
            try
            {
                Assert.NotNull(overview.StartupSnapshotTask);
                Assert.NotNull(providers.StartupRefreshTask);
                await overview.StartupSnapshotTask!;
                await providers.StartupRefreshTask!;

                Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);
                Assert.Equal(fixture.Store.CurrentConfig.Providers.Count, overview.ProviderCount);
                Assert.Equal(fixture.Store.CurrentConfig.Models.Count, overview.ModelCount);
                var provider = Assert.Single(providers.Providers);
                Assert.Equal("fixture-provider", provider.BusinessId);

                // 助手首刷数据源：数据中心内存快照折叠出的分组与快照一致
                var group = Assert.Single(AssistantModelClientFactory.GroupAvailableModels(fixture.Store.Providers));
                Assert.Equal("fixture-provider", group.ProviderBusinessId);
                Assert.Equal("fixture-model", Assert.Single(group.Models).ModelId);
            }
            finally
            {
                providers.Dispose();
                overview.Dispose();
            }
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public void 启动完成后显式刷新仍按需读库并更新内存状态()
    {
        Task.Run(async () =>
        {
            using var fixture = await CreateFixtureAsync();
            await fixture.Store.InitializeAsync();
            var before = fixture.ConfigService.DatabaseReadCount;

            // 模拟非本机写入：绕过数据中心直接写配置库（另一进程/另一实例视角）。
            using (var externalService = new ConfigSnapshotService(Path.Combine(fixture.DirectoryPath, "LoomX.db")))
            {
                await externalService.CreateProviderAsync(new ProviderInput(
                    "external-provider", "外部 Provider", "https://example.com", "openai", true, null, false, null));
            }
            Assert.DoesNotContain(fixture.Store.Providers, item => item.BusinessId == "external-provider");

            // 显式刷新（用户操作 / 非本机配置变更事件的同一读库入口）按需重新读取并更新内存状态。
            await fixture.Store.RefreshAsync();

            Assert.True(fixture.ConfigService.DatabaseReadCount > before, "显式刷新必须按需重新读库。");
            Assert.Contains(fixture.Store.Providers, item => item.BusinessId == "external-provider");
        }).GetAwaiter().GetResult();
    }

    private static async Task<StartupFixture> CreateFixtureAsync(Func<ConfigSnapshotService, Task>? seed = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");

        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite($"Data Source={configPath}")
            .Options;
        await using (var db = new ConfigurationDbContext(options))
            await ConfigurationDatabase.InitializeAsync(db);

        // 关闭自动更新检查：更新协调器在启动后会按需读取代理设置，该读取不属于启动快照组装。
        await using (var connection = new SqliteConnection($"Data Source={configPath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AppSettings SET AutoCheckUpdates = 0;";
            await command.ExecuteNonQueryAsync();
        }

        // 种子数据走独立实例，观测实例的读库计数从 0 开始。
        using (var seedService = new ConfigSnapshotService(configPath))
        {
            var provider = await seedService.CreateProviderAsync(new ProviderInput(
                "fixture-provider", "夹具 Provider", "https://example.com", "openai", true, null, false, null));
            await seedService.CreateModelAsync(provider.Id, new ModelInput(
                "fixture-model", "夹具模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));
            if (seed is not null) await seed(seedService);
        }

        var logger = new RecordingLogger<ConfigSnapshotService>();
        var configService = new ConfigSnapshotService(configPath, logger);
        var gatewayService = new GatewayProcessService();
        var store = new AppDataStore(
            configService,
            gatewayService,
            NullLogger<AppDataStore>.Instance,
            new ActivityQueryService(activityPath));
        return new StartupFixture(directory, configService, logger, gatewayService, store);
    }

    /// <summary>等待启动期的 fire-and-forget 刷新全部落地：读库计数进入稳定段（空闲 400ms）后返回。</summary>
    private static async Task WaitForReadsToSettleAsync(ConfigSnapshotService configService, int minimumReads)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var last = -1;
        var lastChangeAt = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            var current = configService.DatabaseReadCount;
            if (current != last)
            {
                last = current;
                lastChangeAt = DateTime.UtcNow;
            }
            else if (current >= minimumReads && DateTime.UtcNow - lastChangeAt >= TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private sealed record StartupFixture(
        string DirectoryPath,
        ConfigSnapshotService ConfigService,
        RecordingLogger<ConfigSnapshotService> Logger,
        GatewayProcessService GatewayService,
        AppDataStore Store) : IDisposable
    {
        public void Dispose()
        {
            Store.Dispose();
            GatewayService.Dispose();
            ConfigService.Dispose();
            SqliteConnection.ClearAllPools();
            for (var attempt = 0; attempt < 20 && Directory.Exists(DirectoryPath); attempt++)
            {
                try
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
                catch (UnauthorizedAccessException) when (attempt < 19) { Thread.Sleep(50); }
            }
        }
    }
}
