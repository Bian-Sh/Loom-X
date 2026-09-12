using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    public async Task InitializePublishesInitializationSnapshotEvent()
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
            ConfigurationChangedEventArgs? received = null;
            store.ConfigurationChanged += (_, args) => received = args;

            await store.InitializeAsync();

            Assert.NotNull(received);
            Assert.Equal(ConfigurationChangeSource.Initialization, received!.Source);
            Assert.Equal(ConfigurationChangeKind.Snapshot, received.Kind);
            store.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task LocalWritesPublishLocalSaveEventsWithEntityId()
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
            var events = new List<ConfigurationChangedEventArgs>();
            store.ConfigurationChanged += (_, args) => events.Add(args);

            var created = await store.CreateProviderAsync(new ProviderInput("test-provider", "测试 Provider", "https://example.com", "openai", true, null, false, null));

            var createEvent = events.Last();
            Assert.Equal(ConfigurationChangeSource.LocalSave, createEvent.Source);
            Assert.Equal(ConfigurationChangeKind.Provider, createEvent.Kind);
            Assert.Equal(created.Id, createEvent.EntityId);

            events.Clear();
            await store.UpdateSettingsAsync(new AppSettingsInput("zh-CN", "dark", "direct", "http://127.0.0.1", 7890, null, null, false, true, "stable", false, 30, false, true, 86, 24, "acrylic", true));

            var settingsEvent = events.Last();
            Assert.Equal(ConfigurationChangeSource.LocalSave, settingsEvent.Source);
            Assert.Equal(ConfigurationChangeKind.Settings, settingsEvent.Kind);
            store.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task ModelEnabledUpdatePreservesUnrelatedDesktopSnapshots()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            var configLogger = new RecordingLogger<ConfigSnapshotService>();
            using var configService = new ConfigSnapshotService(configPath, configLogger);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(configService, gatewayService, NullLogger<AppDataStore>.Instance, new ActivityQueryService(activityPath));
            await store.InitializeAsync();
            var provider = await store.CreateProviderAsync(new ProviderInput("toggle-provider", "Toggle Provider", "https://example.com", "openai", true, null, false, null));
            var model = await store.CreateModelAsync(provider.Id, new ModelInput("toggle-model", "Toggle Model", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));
            var settingsBefore = store.Settings;
            var serverBefore = store.CurrentConfig.Server;
            var events = new List<ConfigurationChangedEventArgs>();
            store.ConfigurationChanged += (_, args) => events.Add(args);
            configLogger.Messages.Clear();

            var updated = await store.UpdateModelEnabledAsync(model.Id, false);

            Assert.False(updated.Enabled);
            Assert.Same(settingsBefore, store.Settings);
            Assert.Same(serverBefore, store.CurrentConfig.Server);
            Assert.DoesNotContain(store.CurrentConfig.Models, item => item.ModelId == model.ModelId && item.ProviderId == provider.BusinessId);
            Assert.DoesNotContain(store.EnabledGatewayModels, item => item.Id == model.Id);
            var change = Assert.Single(events);
            Assert.Equal(ConfigurationChangeKind.Model, change.Kind);
            Assert.Equal(model.Id, change.EntityId);
            Assert.DoesNotContain(configLogger.Messages, message => message.Contains("数据库配置重载", StringComparison.Ordinal));
            Assert.DoesNotContain(configLogger.Messages, message => message.Contains("配置快照同步读取", StringComparison.Ordinal));
            Assert.DoesNotContain(configLogger.Messages, message => message.Contains("Provider 列表读取", StringComparison.Ordinal));

            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={configPath}").Options;
            await using var db = new ConfigurationDbContext(options);
            Assert.False((await db.Models.AsNoTracking().SingleAsync(item => item.Id == model.Id)).Enabled);
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task ModelEnabledUpdateCanBeReenabledWithoutFullDesktopReload()
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
            var provider = await store.CreateProviderAsync(new ProviderInput("reenable-provider", "Re-enable Provider", "https://example.com", "openai", true, null, false, null));
            var model = await store.CreateModelAsync(provider.Id, new ModelInput("reenable-model", "Re-enable Model", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));

            await store.UpdateModelEnabledAsync(model.Id, false);
            Assert.DoesNotContain(store.CurrentConfig.Models, item => item.ModelId == model.ModelId);
            await store.UpdateModelEnabledAsync(model.Id, true);

            Assert.Contains(store.CurrentConfig.Models, item => item.ModelId == model.ModelId && item.ProviderId == provider.BusinessId);
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
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
