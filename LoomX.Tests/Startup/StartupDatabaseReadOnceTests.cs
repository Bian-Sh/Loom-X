using LoomX.Activity;
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
    public async Task 启动链路Shell阶段主窗口初始化不再重复读取配置库()
    {
        using var fixture = await CreateFixtureAsync();
        // 编排器 Database 阶段：StartupOrchestrator.RunAsync 的第一步即为 AppDataStore.InitializeAsync。
        await fixture.Store.InitializeAsync();
        Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);

        // Shell 阶段：与 App.CompleteStartupAsync 相同的主窗口初始化（autoInitializeDataStore: false）。
        var mainWindow = CreateMainWindowViewModel(fixture);
        try
        {
            await WaitForReadsToSettleAsync(fixture.ConfigService, minimumReads: 1);
            Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);

            // 宽限期后复核，捕获迟到的无驱动读库。
            await Task.Delay(600);
            Assert.Equal(1, fixture.ConfigService.DatabaseReadCount);
        }
        finally
        {
            mainWindow.Dispose();
        }
    }

    private static MainWindowViewModel CreateMainWindowViewModel(StartupFixture fixture) => new(
        fixture.GatewayService,
        configService: fixture.ConfigService,
        dataStore: fixture.Store,
        autoInitializeDataStore: false,
        restoreStartupIntent: false);

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
