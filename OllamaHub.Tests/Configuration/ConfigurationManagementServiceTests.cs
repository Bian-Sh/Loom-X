using Microsoft.EntityFrameworkCore;
using OllamaHub.Configuration;
using Xunit;

namespace OllamaHub.Tests.Configuration;

public sealed class ConfigurationManagementServiceTests
{
    [Fact]
    public async Task ProviderAndModelCrud_RebuildsRuntimeSnapshot()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);
            var configurationProvider = new DatabaseConfigurationProvider(context);
            await configurationProvider.ReloadAsync();
            var service = new ConfigurationManagementService(new TestDbContextFactory(options), configurationProvider);

            var provider = await service.CreateProviderAsync(new ProviderInput("test", "测试 Provider", "https://example.com", "openai", true, null, false, null));
            var model = await service.CreateModelAsync(provider.Id, new ModelInput("demo-model", "演示模型", null, "gpt", null, null, 128000, 4096, false, 0, 1, true, null, false, null, null));

            Assert.Equal("test", model.ProviderId);
            Assert.Single(configurationProvider.GetModels());
            Assert.Equal("demo-model", configurationProvider.FindModel("演示模型")?.ModelId);

            await service.DeleteModelAsync(model.Id);
            await service.DeleteProviderAsync(provider.Id);
            Assert.Empty(await service.ListProvidersAsync());
            await context.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task DeleteProvider_WithModels_IsRejected()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);
            var configurationProvider = new DatabaseConfigurationProvider(context);
            await configurationProvider.ReloadAsync();
            var service = new ConfigurationManagementService(new TestDbContextFactory(options), configurationProvider);
            var provider = await service.CreateProviderAsync(new ProviderInput("test", "测试 Provider", "https://example.com", "openai", true, null, false, null));
            await service.CreateModelAsync(provider.Id, new ModelInput("demo-model", "演示模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteProviderAsync(provider.Id));
            await context.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task SettingsAndProviderProxy_ArePersistedInSqlite()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);
            var configurationProvider = new DatabaseConfigurationProvider(context);
            await configurationProvider.ReloadAsync();
            var service = new ConfigurationManagementService(new TestDbContextFactory(options), configurationProvider);

            var defaults = await service.GetSettingsAsync();
            Assert.Equal("direct", defaults.ProxyMode);
            Assert.Equal(30, defaults.LogRetentionDays);

            var updatedSettings = await service.UpdateSettingsAsync(new AppSettingsInput("zh-CN", "dark", false, "custom", "http://127.0.0.1", 7890, "user", "password", false, true, "stable", true, 7));
            Assert.Equal("dark", updatedSettings.Theme);
            Assert.True(updatedSettings.HasProxyPassword);
            Assert.True(configurationProvider.Current.Settings.DiagnosticsEnabled);

            var provider = await service.CreateProviderAsync(new ProviderInput("proxy", "代理 Provider", "https://example.com", "anthropic", true, null, false, null, true));
            Assert.True(provider.UseProxy);
            Assert.Equal("anthropic", provider.ApiMode);

            await using var verifyContext = new ConfigurationDbContext(options);
            var storedProvider = await verifyContext.Providers.SingleAsync(item => item.Id == provider.Id);
            var storedSettings = await verifyContext.AppSettings.SingleAsync();
            Assert.True(storedProvider.UseProxy);
            Assert.Equal("dark", storedSettings.Theme);
            Assert.Equal("custom", storedSettings.ProxyMode);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task ProviderType_RejectsUnsupportedValue()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);
            var configurationProvider = new DatabaseConfigurationProvider(context);
            await configurationProvider.ReloadAsync();
            var service = new ConfigurationManagementService(new TestDbContextFactory(options), configurationProvider);

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProviderAsync(new ProviderInput("unsupported", "不支持", "https://example.com", "antigravity", true, null, false, null)));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ConfigurationDbContext> options) : IDbContextFactory<ConfigurationDbContext>
    {
        public ConfigurationDbContext CreateDbContext() => new(options);
        public Task<ConfigurationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ConfigurationDbContext(options));
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { File.Delete(path); break; }
                catch (IOException) when (attempt < 4) { Thread.Sleep(50); }
                catch (IOException) { }
            }
        }
    }
}
