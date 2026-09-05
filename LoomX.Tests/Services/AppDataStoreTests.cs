using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using LoomX.Activity;
using LoomX.Configuration;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class AppDataStoreTests
{
    [Fact]
    public async Task InitializeAsyncReturnsTheSameTaskAndLoadsConfigurationOnce()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(configService, gatewayService, NullLogger<AppDataStore>.Instance, new ActivityQueryService(activityPath));

            var first = store.InitializeAsync();
            var second = store.InitializeAsync();
            await Task.WhenAll(first, second);

            Assert.Same(first, second);
            Assert.True(store.IsInitialized);
            Assert.NotNull(store.Settings);
            store.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task SuccessfulConfigurationWriteReplacesSnapshotAndPublishesChange()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(configService, gatewayService, NullLogger<AppDataStore>.Instance, new ActivityQueryService(activityPath));
            await store.InitializeAsync();
            var before = store.CurrentConfig;
            var changes = 0;
            store.ConfigurationChanged += (_, _) => changes++;

            await store.CreateProviderAsync(new ProviderInput("test-provider", "测试 Provider", "https://example.com", "openai", true, null, false, null));

            Assert.NotSame(before, store.CurrentConfig);
            Assert.Contains(store.Providers, item => item.BusinessId == "test-provider");
            Assert.True(changes >= 1);
            store.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task FailedConfigurationWriteKeepsExistingSnapshot()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(configService, gatewayService, NullLogger<AppDataStore>.Instance, new ActivityQueryService(activityPath));
            await store.InitializeAsync();
            var before = store.CurrentConfig;

            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateProviderAsync(new ProviderInput("bad", "坏配置", "not-a-url", "openai", true, null, false, null)));

            Assert.Same(before, store.CurrentConfig);
            store.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task ActivityWindowEvictsOldestRowsAndQueuesFilteredOutHistoryEvents()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            await SeedActivitiesAsync(activityPath, 501);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(configService, gatewayService, NullLogger<AppDataStore>.Instance, new ActivityQueryService(activityPath));

            var query = new ActivityQuery(Protocol: "OpenAI", Limit: AppDataStore.ActivityWindowLimit);
            var page = await store.LoadActivityPageAsync(query);
            Assert.Equal(AppDataStore.ActivityWindowLimit, page.Items.Count);
            Assert.Equal("request-500", page.Items[0].RequestId);
            Assert.Equal("request-1", page.Items[^1].RequestId);

            store.SetActivityHistoryMode(true);
            await store.HandleActivityEnqueuedAsync(CreateInput("request-anthropic", "Anthropic", 503));

            Assert.Equal(1, store.PendingActivityCount);
            Assert.Equal(AppDataStore.ActivityWindowLimit, store.ActivityWindow.Count);
            var latest = await store.ReturnToLatestAsync(query);
            Assert.Equal(0, store.PendingActivityCount);
            Assert.DoesNotContain(latest.Items, item => item.RequestId == "request-anthropic");

            await store.HandleActivityEnqueuedAsync(CreateInput("request-new", "OpenAI", 200));
            Assert.Equal(AppDataStore.ActivityWindowLimit, store.ActivityWindow.Count);
            Assert.Equal("request-new", store.ActivityWindow[0].RequestId);
            Assert.DoesNotContain(store.ActivityWindow, item => item.RequestId == "request-1");
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task ActivityHistoryDeduplicatesRealtimeAndPagedRecords()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            await SeedActivitiesAsync(activityPath, 2);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(configService, gatewayService, NullLogger<AppDataStore>.Instance, new ActivityQueryService(activityPath));

            var query = new ActivityQuery(Limit: AppDataStore.ActivityWindowLimit);
            await store.LoadActivityPageAsync(query);
            store.SetActivityHistoryMode(true);
            var duplicate = CreateInput("request-1", "OpenAI", 200) with { CreatedAt = DateTimeOffset.Parse("2026-09-02T09:01:00+08:00") };
            await store.HandleActivityEnqueuedAsync(duplicate);
            await store.HandleActivityEnqueuedAsync(duplicate);

            var page = await store.ReturnToLatestAsync(query);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(0, store.PendingActivityCount);
        }
        finally { DeleteDirectory(directory); }
    }

    private static async Task InitializeConfigurationAsync(string path)
    {
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={path}").Options;
        await using var db = new ConfigurationDbContext(options);
        await ConfigurationDatabase.InitializeAsync(db);
    }

    private static async Task SeedActivitiesAsync(string path, int count)
    {
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={path}").Options;
        await using var db = new ActivityDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var start = DateTimeOffset.Parse("2026-09-02T09:00:00+08:00");
        for (var index = 0; index < count; index++)
            db.Events.Add(new ActivityEventEntity
            {
                CreatedAt = start.AddMinutes(index),
                RequestId = $"request-{index}",
                Method = "POST",
                IncomingPath = "/v1/chat/completions",
                Protocol = "OpenAI",
                Route = "OpenAI 直通",
                ProviderId = "provider-a",
                ModelId = "model-a",
                StatusCode = 200,
                ElapsedMs = 100
            });
        await db.SaveChangesAsync();
    }

    private static ActivityEventInput CreateInput(string requestId, string protocol, int statusCode) => new(
        DateTimeOffset.UtcNow,
        requestId,
        "POST",
        "/v1/chat/completions",
        protocol,
        protocol == "OpenAI" ? "OpenAI 直通" : "Anthropic 直通",
        "provider-a",
        "model-a",
        statusCode,
        100,
        0,
        false,
        null);

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(directory); attempt++)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { if (attempt < 19) Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { if (attempt < 19) Thread.Sleep(50); }
        }
    }
}
