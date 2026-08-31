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
    public void ProviderEditor_LoadedApiKeyIsVisibleButNotMarkedAsEdited()
    {
        var editor = ProviderEditorViewModel.FromResponse(new ProviderResponse(Guid.NewGuid(), "provider", "Provider", "https://example.com", "openai", true, false, true, 0, "{}", [], ApiKey: "secret"));

        Assert.Equal("secret", editor.ApiKey);
        Assert.Null(editor.ToInput().ApiKey);
    }

    [Fact]
    public void ProviderEditor_ApiKeyVisibilityTogglesEyeIcon()
    {
        var editor = new ProviderEditorViewModel();

        Assert.False(editor.IsApiKeyVisible);
        Assert.True(editor.IsApiKeyHidden);
        Assert.Equal("显示 API Key", editor.ApiKeyVisibilityToolTip);

        editor.ToggleApiKeyVisibility();

        Assert.True(editor.IsApiKeyVisible);
        Assert.False(editor.IsApiKeyHidden);
        Assert.Equal("隐藏 API Key", editor.ApiKeyVisibilityToolTip);
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
    public async Task GatewayEndpoints_UseUniqueCanonicalPaths()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);

            var paths = await context.GatewayEndpoints.OrderBy(item => item.Key).Select(item => new { item.Key, item.PublicPath }).ToListAsync();

            Assert.Equal("/openai", paths.Single(item => item.Key == "openai").PublicPath);
            Assert.Equal("/", paths.Single(item => item.Key == "ollama").PublicPath);
            Assert.Equal("/azure", paths.Single(item => item.Key == "azure").PublicPath);
            Assert.Equal(3, paths.Select(item => item.PublicPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
    public async Task GatewayCombos_AreIndependentPerEndpoint()
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
            var provider = await service.CreateProviderAsync(new ProviderInput("gateway", "网关 Provider", "https://example.com", "openai", true, null, false, null));
            var model = await service.CreateModelAsync(provider.Id, new ModelInput("model", "模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));

            var openAiCombo = await service.CreateGatewayComboAsync("openai", new GatewayComboInput("公共模型", true, 2));
            var ollamaCombo = await service.CreateGatewayComboAsync("ollama", new GatewayComboInput("本地模型", false, 0));
            await service.CreateGatewayRouteAsync(openAiCombo.Id, new GatewayRouteInput(model.Id, true, 0));
            await service.CreateGatewayRouteAsync(ollamaCombo.Id, new GatewayRouteInput(model.Id, false, 0));
            var endpoints = await service.ListGatewayEndpointsAsync();
            Assert.Contains(endpoints.Single(item => item.Key == "openai").Combos, combo => combo.Name == "公共模型" && combo.Enabled && combo.SortOrder == 2);
            Assert.Contains(endpoints.Single(item => item.Key == "ollama").Combos, combo => !combo.Enabled);
            Assert.Equal(2, configurationProvider.Current.GatewayEndpoints.Count(item => item.Combos.Count > 0));
        }
        finally { DeleteDatabaseFiles(databasePath); }
    }

    [Fact]
    public async Task GatewayCombos_ExposeNamedGroupsAndMembers()
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
            var provider = await service.CreateProviderAsync(new ProviderInput("combo", "Combo Provider", "https://example.com", "openai", true, null, false, null));
            var model = await service.CreateModelAsync(provider.Id, new ModelInput("model", "模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));

            var combo = await service.CreateGatewayComboAsync("openai", new GatewayComboInput("coding", true, 0));
            await service.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(model.Id, true, 0));

            var endpoints = await service.ListGatewayEndpointsAsync();
            var listedCombo = endpoints.Single(item => item.Key == "openai").Combos.Single();
            Assert.Equal("coding", listedCombo.Name);
            Assert.Single(listedCombo.Routes);
            Assert.Equal(model.Id, listedCombo.Routes[0].ModelId);
        }
        finally { DeleteDatabaseFiles(databasePath); }
    }

    [Fact]
    public async Task GatewayModelSource_ReadsEnabledProviderModelsFromOrmRelationship()
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
            var provider = await service.CreateProviderAsync(new ProviderInput("provider", "Provider", "https://example.com", "openai", true, null, false, null));

            for (var index = 1; index <= 4; index++)
            {
                await service.CreateModelAsync(provider.Id, new ModelInput($"model-{index}", $"模型 {index}", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));
            }

            var models = await service.ListEnabledGatewayModelsAsync();

            Assert.Equal(4, models.Count);
            Assert.All(models, model => Assert.Equal("Provider", model.ProviderName));
            Assert.Equal(["模型 1", "模型 2", "模型 3", "模型 4"], models.Select(model => model.ModelName));
        }
        finally { DeleteDatabaseFiles(databasePath); }
    }

    [Fact]
    public async Task GatewayComboNames_AreCaseInsensitivePerEndpoint()
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

            await service.CreateGatewayComboAsync("openai", new GatewayComboInput("Coding", true, 0));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateGatewayComboAsync("openai", new GatewayComboInput("coding", true, 1)));
        }
        finally { DeleteDatabaseFiles(databasePath); }
    }

    [Fact]
    public async Task LegacyGatewayRoutes_AreNotExposedUntilAddedToCombo()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);
            var configurationProvider = new DatabaseConfigurationProvider(context);
            var provider = new ProviderEntity { BusinessId = "legacy", DisplayName = "旧 Provider", BaseUrl = "https://example.com" };
            var model = new ModelEntity { Provider = provider, ModelId = "legacy-model", DisplayName = "旧模型" };
            context.Providers.Add(provider);
            context.Models.Add(model);
            context.GatewayRoutes.Add(new GatewayRouteEntity { EndpointKey = "ollama", Model = model, ModelId = model.Id });
            await context.SaveChangesAsync();

            await configurationProvider.ReloadAsync();

            Assert.Empty(configurationProvider.Current.GatewayEndpoints.Single(item => item.Key == "ollama").Combos);
        }
        finally { DeleteDatabaseFiles(databasePath); }
    }

    [Fact]
    public async Task DeleteModel_WithLegacyGatewayRoute_IsRejected()
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
            var provider = await service.CreateProviderAsync(new ProviderInput("legacy-delete", "旧 Provider", "https://example.com", "openai", true, null, false, null));
            var model = await service.CreateModelAsync(provider.Id, new ModelInput("legacy-model", "旧模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));

            await using (var routeContext = new ConfigurationDbContext(options))
            {
                routeContext.GatewayRoutes.Add(new GatewayRouteEntity { EndpointKey = "openai", ModelId = model.Id });
                await routeContext.SaveChangesAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteModelAsync(model.Id));
            Assert.Contains("网关路由", exception.Message);
        }
        finally { DeleteDatabaseFiles(databasePath); }
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
            Assert.False(defaults.LogStackTrace);

            var updatedSettings = await service.UpdateSettingsAsync(new AppSettingsInput("zh-CN", "dark", "custom", "http://127.0.0.1", 7890, "user", "password", false, true, "stable", true, 7, true));
            Assert.Equal("dark", updatedSettings.Theme);
            Assert.True(updatedSettings.HasProxyPassword);
            Assert.True(configurationProvider.Current.Settings.DiagnosticsEnabled);
            Assert.True(updatedSettings.LogStackTrace);

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
            Assert.Equal("secret", provider.ApiKey);

            var updated = await service.UpdateProviderAsync(provider.Id, new ProviderInput("empty-key", "空密钥", "https://example.com", "openai", true, string.Empty, false, null));
            Assert.False(updated.HasApiKey);
            Assert.Null(updated.ApiKey);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task InvalidProtectedProviderKey_DoesNotHideProvider()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ollamahub-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var context = new ConfigurationDbContext(options);
            await ConfigurationDatabase.InitializeAsync(context);
            context.Providers.Add(new ProviderEntity
            {
                BusinessId = "corrupt-key",
                DisplayName = "密钥异常 Provider",
                BaseUrl = "https://example.com",
                ProtectedApiKey = "dpapi:not-valid-base64"
            });
            await context.SaveChangesAsync();

            var configurationProvider = new DatabaseConfigurationProvider(context);
            await configurationProvider.ReloadAsync();
            var service = new ConfigurationManagementService(new TestDbContextFactory(options), configurationProvider);

            var providers = await service.ListProvidersAsync();
            var provider = Assert.Single(providers);
            Assert.Equal("corrupt-key", provider.BusinessId);
            Assert.True(provider.HasApiKey);
            Assert.Null(provider.ApiKey);
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
