using Microsoft.EntityFrameworkCore;
using OllamaHub.Configuration;
using OllamaHub.Desktop.ViewModels;
using Xunit;

namespace OllamaHub.Tests.Configuration;

public sealed class ConfigurationManagementServiceTests
{
    [Fact]
    public void ProviderEditor_EmptyApiKeyIsTreatedAsUnchanged()
    {
        var editor = new ProviderEditorViewModel
        {
            BusinessId = "provider",
            DisplayName = "Provider",
            BaseUrl = "https://example.com",
            ApiMode = "openai",
            ApiKey = string.Empty,
        };

        var input = editor.ToInput();

        Assert.Null(input.ApiKey);
    }

    [Fact]
    public void ProviderEditor_UserClearingApiKeySubmitsExplicitClear()
    {
        var editor = new ProviderEditorViewModel { ApiKey = "replacement" };

        editor.ApiKey = string.Empty;

        Assert.Equal(string.Empty, editor.ToInput().ApiKey);
    }

    [Fact]
    public void ProviderEditor_LoadedApiKeyIsNotMarkedAsEdited()
    {
        var editor = ProviderEditorViewModel.FromResponse(new ProviderResponse(Guid.NewGuid(), "provider", "Provider", "https://example.com", "openai", true, false, true, 0, "{}", []));

        Assert.Null(editor.ToInput().ApiKey);
    }

    [Fact]
    public async Task NewProvider_DefaultsEndpointFormatToResponses()
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

            var provider = await service.CreateProviderAsync(new ProviderInput("default-format", "默认格式", "https://example.com", "openai", true, null, false, null));

            Assert.Equal("responses", provider.EndpointFormat);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task ProviderEndpointFormat_CanBeUpdatedAndAppearsInSnapshot()
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

            var provider = await service.CreateProviderAsync(new ProviderInput("format", "格式", "https://example.com", "openai", true, null, false, null));
            provider = await service.UpdateProviderAsync(provider.Id, new ProviderInput("format", "格式", "https://example.com", "openai", true, null, false, null, false, null, "chat_completions"));
            Assert.Equal("chat_completions", provider.EndpointFormat);

            var model = await service.CreateModelAsync(provider.Id, new ModelInput("model", "模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));
            Assert.Equal("chat_completions", configurationProvider.FindModel(model.DisplayName)?.EndpointFormat);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task InvalidEndpointFormat_IsRejectedWithoutChangingStoredValue()
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
            var provider = await service.CreateProviderAsync(new ProviderInput("format", "格式", "https://example.com", "openai", true, null, false, null, false, null, "responses"));

            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProviderAsync(provider.Id, new ProviderInput("format", "格式", "https://example.com", "openai", true, null, false, null, false, null, "invalid")));

            await using var verifyContext = new ConfigurationDbContext(options);
            Assert.Equal("responses", (await verifyContext.Providers.SingleAsync(item => item.Id == provider.Id)).EndpointFormat);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task ExistingDatabase_MissingEndpointFormatColumn_GetsDefaultValue()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath}";
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite(connectionString).Options;
            await using (var context = new ConfigurationDbContext(options))
            {
                await ConfigurationDatabase.InitializeAsync(context);
                context.Providers.Add(new ProviderEntity { BusinessId = "legacy", DisplayName = "旧 Provider", BaseUrl = "https://example.com" });
                await context.SaveChangesAsync();
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Providers DROP COLUMN EndpointFormat");
                context.ChangeTracker.Clear();
            }

            await using (var migratedContext = new ConfigurationDbContext(options))
            {
                await ConfigurationDatabase.InitializeAsync(migratedContext);
                var provider = await migratedContext.Providers.SingleAsync();
                Assert.Equal("responses", provider.EndpointFormat);
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

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

            var provider = await service.CreateProviderAsync(new ProviderInput("proxy", "代理 Provider", "https://example.com", "anthropic", true, null, false, null, true, "https://models.example.com/list"));
            Assert.True(provider.UseProxy);
            Assert.Equal("anthropic", provider.ApiMode);
            Assert.Equal("https://models.example.com/list", provider.ModelListUrl);

            await using var verifyContext = new ConfigurationDbContext(options);
            var storedProvider = await verifyContext.Providers.SingleAsync(item => item.Id == provider.Id);
            var storedSettings = await verifyContext.AppSettings.SingleAsync();
            Assert.True(storedProvider.UseProxy);
            Assert.Equal("https://models.example.com/list", storedProvider.ModelListUrl);
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

    [Fact]
    public async Task ModelListUrl_RejectsNonHttpAddress()
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

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProviderAsync(new ProviderInput("invalid-url", "非法 URL", "https://example.com", "openai", true, null, false, null, false, "ftp://example.com/models")));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task EmptyProviderApiKey_ClearsStoredKey()
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
            var provider = await service.CreateProviderAsync(new ProviderInput("empty-key", "空密钥", "https://example.com", "openai", true, "secret", false, null));
            Assert.True(provider.HasApiKey);

            var updated = await service.UpdateProviderAsync(provider.Id, new ProviderInput("empty-key", "空密钥", "https://example.com", "openai", true, string.Empty, false, null));
            Assert.False(updated.HasApiKey);
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
