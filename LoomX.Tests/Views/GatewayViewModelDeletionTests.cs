using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
using LoomX.Localization;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class GatewayViewModelDeletionTests
{
    [Fact]
    public async Task RouteDeleteWorksWhenTheComboHasNotBeenSelected()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var provider = await configService.CreateProviderAsync(new ProviderInput("sensenova", "SenseNova", "https://example.com", "openai", true, null, false, null));
            var model = await configService.CreateModelAsync(provider.Id, new ModelInput("deepseek-v4-flash", "deepseek-v4-flash", null, "deepseek", null, "openai", 128000, 4096, false, null, null, true, null, false, null, null));
            var combo = await configService.CreateGatewayComboAsync(new GatewayComboInput("测试组合", true, 0));
            await configService.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(model.Id, true, 0));

            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            Assert.Single(dataStore.GatewayCombos);
            using var viewModel = new GatewayViewModel(dataStore);
            await WaitForAsync(() => viewModel.Combos.Count == 1 && viewModel.Combos[0].Routes.Count == 1, () => $"status={viewModel.Status}; combos={viewModel.Combos.Count}; initialized={dataStore.IsInitialized}; loading={dataStore.IsLoading}");

            Assert.Null(viewModel.SelectedCombo);
            viewModel.RemoveRouteCommand.Execute(viewModel.Combos[0].Routes[0]);

            await WaitForAsync(() => viewModel.Combos[0].Routes.Count == 0);
            var persisted = await configService.ListGatewayCombosAsync();
            Assert.Empty(Assert.Single(persisted).Routes);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RouteToggleWorksWhenTheComboHasNotBeenSelected()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var provider = await configService.CreateProviderAsync(new ProviderInput("toggle-owner", "切换 Provider", "https://example.com", "openai", true, null, false, null));
            var model = await configService.CreateModelAsync(provider.Id, new ModelInput("toggle-model", "切换模型", null, "gpt", null, "openai", 128000, 4096, false, null, null, true, null, false, null, null));
            var combo = await configService.CreateGatewayComboAsync(new GatewayComboInput("切换组合", true, 0));
            await configService.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(model.Id, true, 0));
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new GatewayViewModel(dataStore);
            await WaitForAsync(() => viewModel.Combos.Count == 1 && viewModel.Combos[0].Routes.Count == 1);

            viewModel.ToggleRouteCommand.Execute(viewModel.Combos[0].Routes[0]);

            await WaitForAsync(() => !viewModel.Combos[0].Routes[0].Enabled);
            var persisted = Assert.Single((await configService.ListGatewayCombosAsync()).Single().Routes);
            Assert.False(persisted.Enabled);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ComboAddUpdatesEveryEndpointPicker()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new GatewayViewModel(dataStore);
            await WaitForAsync(() => viewModel.Endpoints.Count > 0 && viewModel.Combos.Count == 0);

            viewModel.AddComboCommand.Execute(null);

            await WaitForAsync(() => viewModel.Combos.Count == 1 && viewModel.Endpoints.All(endpoint => endpoint.ComboOptions.Count == 1));
            var added = viewModel.Combos[0];
            Assert.All(viewModel.Endpoints, endpoint =>
            {
                var option = Assert.Single(endpoint.ComboOptions);
                Assert.Equal(added.Id, option.ComboId);
                Assert.Equal(added.Name, option.Name);
                Assert.False(option.IsSelected);
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ComboToggleUpdatesEveryEndpointPicker()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var combo = await configService.CreateGatewayComboAsync(new GatewayComboInput("待停用组合", true, 0));
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new GatewayViewModel(dataStore);
            await WaitForAsync(() => viewModel.Combos.Count == 1 && viewModel.Endpoints.All(endpoint => endpoint.ComboOptions.Count == 1));

            viewModel.ToggleComboCommand.Execute(viewModel.Combos.Single(item => item.Id == combo.Id));

            await WaitForAsync(() => viewModel.Endpoints.All(endpoint => !endpoint.ComboOptions.Single().ComboEnabled));
            Assert.All(viewModel.Endpoints, endpoint =>
                Assert.Equal(ResourceLookup.Resolve("gateway.combo.disabled"), endpoint.ComboOptions.Single().StatusText));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ComboDeletePreservesBoundComboAsSelectedMissingOption()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var first = await configService.CreateGatewayComboAsync(new GatewayComboInput("第一个组合", true, 0));
            var second = await configService.CreateGatewayComboAsync(new GatewayComboInput("第二个组合", true, 1));
            await configService.UpdateGatewayEndpointComboBindingsAsync("openai", new GatewayEndpointComboSelectionInput([first.Id]));
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new GatewayViewModel(dataStore);
            await WaitForAsync(() => viewModel.Combos.Count == 2 && viewModel.Endpoints.All(endpoint => endpoint.ComboOptions.Count == 2));

            viewModel.RemoveComboCommand.Execute(viewModel.Combos.Single(item => item.Id == first.Id));

            await WaitForAsync(() => viewModel.Combos.Count == 2 && viewModel.Combos.Single(item => item.Id == first.Id).IsDeleted && viewModel.Endpoints.Single(endpoint => endpoint.Key == "openai").ComboOptions.Single(item => item.ComboId == first.Id).StatusText == ResourceLookup.Resolve("gateway.combo.missing"));
            Assert.True(viewModel.Combos.Single(item => item.Id == first.Id).IsDeleted);
            var openAi = viewModel.Endpoints.Single(endpoint => endpoint.Key == "openai");
            Assert.Equal(2, openAi.ComboOptions.Count);
            Assert.Equal("第一个组合", openAi.ComboOptions.Single(item => item.ComboId == first.Id).Name);
            Assert.True(openAi.ComboOptions.Single(item => item.ComboId == first.Id).IsSelected);
            var persistedCombos = await configService.ListGatewayCombosAsync();
            Assert.Equal(2, persistedCombos.Count);
            Assert.True(persistedCombos.Single(item => item.Id == first.Id).IsDeleted);

            await viewModel.ToggleEndpointComboAsync(openAi.ComboOptions.Single(item => item.ComboId == second.Id));
            Assert.True(openAi.ComboOptions.Single(item => item.ComboId == second.Id).IsSelected);
            Assert.Equal([first.Id, second.Id], (await configService.ListGatewayEndpointsAsync()).Single(endpoint => endpoint.Key == "openai").Combos.OrderBy(item => item.SortOrder).Select(item => item.ComboId));

            await viewModel.ToggleEndpointComboAsync(openAi.ComboOptions.Single(item => item.ComboId == first.Id));
            await WaitForAsync(() => openAi.ComboOptions.All(item => item.ComboId != first.Id));
            Assert.DoesNotContain((await configService.ListGatewayEndpointsAsync()).Single(endpoint => endpoint.Key == "openai").Combos, item => item.ComboId == first.Id);
            Assert.DoesNotContain(await configService.ListGatewayCombosAsync(), item => item.Id == first.Id);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task DeletedBoundComboSurvivesViewModelReload()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var combo = await configService.CreateGatewayComboAsync(new GatewayComboInput("重载保留组合", true, 0));
            await configService.UpdateGatewayEndpointComboBindingsAsync("ollama", new GatewayEndpointComboSelectionInput([combo.Id]));
            await configService.DeleteGatewayComboAsync(combo.Id);

            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new GatewayViewModel(dataStore);

            await WaitForAsync(() => viewModel.Combos.SingleOrDefault(item => item.Id == combo.Id)?.IsDeleted == true
                && viewModel.Endpoints.Single(endpoint => endpoint.Key == "ollama").ComboOptions.Any(item => item.ComboId == combo.Id));

            var option = viewModel.Endpoints.Single(endpoint => endpoint.Key == "ollama").ComboOptions.Single(item => item.ComboId == combo.Id);
            Assert.True(option.IsSelected);
            Assert.True(option.IsDeleted);
            Assert.Equal(ResourceLookup.Resolve("gateway.combo.missing"), option.StatusText);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ProviderDeleteRemovesTheProviderFromTheDirectory()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            await configService.CreateProviderAsync(new ProviderInput("delete-provider", "待删除 Provider", "https://example.com", "openai", true, null, false, null));
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new ProvidersViewModel(dataStore);
            await WaitForAsync(() => viewModel.Providers.Count == 1);

            viewModel.DeleteProviderCommand.Execute(viewModel.Providers[0]);

            await WaitForAsync(() => viewModel.Providers.Count == 0);
            Assert.Empty(await configService.ListProvidersAsync());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ModelDeleteRemovesTheModelFromTheSelectedProvider()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var provider = await configService.CreateProviderAsync(new ProviderInput("model-owner", "模型 Provider", "https://example.com", "openai", true, null, false, null));
            await configService.CreateModelAsync(provider.Id, new ModelInput("delete-model", "待删除模型", null, "gpt", null, "openai", 128000, 4096, false, null, null, true, null, false, null, null));
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new ProvidersViewModel(dataStore);
            await WaitForAsync(() => viewModel.Providers.Count == 1 && viewModel.Providers[0].Models.Count == 1);

            viewModel.DeleteModelCommand.Execute(viewModel.Providers[0].Models[0]);

            await WaitForAsync(() => viewModel.Providers[0].Models.Count == 0);
            Assert.Empty((await configService.ListProvidersAsync()).Single().Models);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static async Task InitializeConfigurationAsync(string path)
    {
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={path}").Options;
        await using var db = new ConfigurationDbContext(options);
        await ConfigurationDatabase.InitializeAsync(db);
    }

    private static async Task WaitForAsync(Func<bool> condition, Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        Assert.True(condition(), $"等待 ViewModel 状态更新超时：{diagnostic?.Invoke()}");
    }

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
