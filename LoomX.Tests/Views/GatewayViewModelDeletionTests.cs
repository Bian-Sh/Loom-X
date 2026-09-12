using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
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
    public async Task ComboDeleteRemovesTheRequestedCombo()
    {
        var directory = CreateDirectory();
        var configPath = Path.Combine(directory, "LoomX.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            var first = await configService.CreateGatewayComboAsync(new GatewayComboInput("第一个组合", true, 0));
            var second = await configService.CreateGatewayComboAsync(new GatewayComboInput("第二个组合", true, 1));
            using var gatewayService = new GatewayProcessService();
            using var dataStore = new AppDataStore(configService, gatewayService);
            await dataStore.InitializeAsync();
            using var viewModel = new GatewayViewModel(dataStore);
            await WaitForAsync(() => viewModel.Combos.Count == 2);

            viewModel.RemoveComboCommand.Execute(viewModel.Combos.Single(item => item.Id == first.Id));

            await WaitForAsync(() => viewModel.Combos.Count == 1);
            Assert.Equal(second.Id, viewModel.Combos[0].Id);
            Assert.Single(await configService.ListGatewayCombosAsync());
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
