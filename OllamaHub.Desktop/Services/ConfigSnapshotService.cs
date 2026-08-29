using Microsoft.EntityFrameworkCore;
using OllamaHub.Configuration;

namespace OllamaHub.Desktop.Services;

public sealed class ConfigSnapshotService
{
    private readonly string databasePath = AppDataPaths.DatabasePath;

    public ConfigSnapshotService() => AppDataPaths.EnsureCreated();

    public ResolvedAppConfig Load()
    {
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        using var db = new ConfigurationDbContext(options);
        ConfigurationDatabase.InitializeAsync(db).GetAwaiter().GetResult();
        var provider = new DatabaseConfigurationProvider(db);
        provider.ReloadAsync().GetAwaiter().GetResult();
        return provider.Current;
    }

    public async Task<IReadOnlyList<ProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        await ConfigurationDatabase.InitializeAsync(db, cancellationToken);
        var provider = new DatabaseConfigurationProvider(db);
        await provider.ReloadAsync(cancellationToken);
        return await new ConfigurationManagementService(new DesktopDbContextFactory(CreateOptions()), provider).ListProvidersAsync(cancellationToken);
    }

    public async Task<AppSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        await ConfigurationDatabase.InitializeAsync(db, cancellationToken);
        var provider = new DatabaseConfigurationProvider(db);
        await provider.ReloadAsync(cancellationToken);
        return await new ConfigurationManagementService(new DesktopDbContextFactory(CreateOptions()), provider).GetSettingsAsync(cancellationToken);
    }

    public Task<AppSettingsResponse> UpdateSettingsAsync(AppSettingsInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateSettingsAsync(input, token), cancellationToken);

    public Task<ProviderResponse> CreateProviderAsync(ProviderInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateProviderAsync(input, token), cancellationToken);
    public Task<ProviderResponse> UpdateProviderAsync(Guid id, ProviderInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateProviderAsync(id, input, token), cancellationToken);
    public Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteProviderAsync(id, token); return true; }, cancellationToken);
    public Task<ModelResponse> CreateModelAsync(Guid providerId, ModelInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateModelAsync(providerId, input, token), cancellationToken);
    public Task<ModelResponse> UpdateModelAsync(Guid id, ModelInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateModelAsync(id, input, token), cancellationToken);
    public Task DeleteModelAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteModelAsync(id, token); return true; }, cancellationToken);
    public async Task<IReadOnlyList<GatewayModelSourceResponse>> ListEnabledGatewayModelsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        return await db.Models.AsNoTracking()
            .Where(model => model.Enabled && model.Provider.Enabled)
            .OrderBy(model => model.Provider.SortOrder)
            .ThenBy(model => model.SortOrder)
            .ThenBy(model => model.ModelId)
            .Select(model => new GatewayModelSourceResponse(model.Id, model.DisplayName, model.Provider.DisplayName))
            .ToArrayAsync(cancellationToken);
    }
    public Task<IReadOnlyList<GatewayEndpointResponse>> ListGatewayEndpointsAsync(CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.ListGatewayEndpointsAsync(token), cancellationToken);
    public Task<GatewayEndpointResponse> SetGatewayEndpointEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.SetGatewayEndpointEnabledAsync(key, enabled, token), cancellationToken);
    public Task<GatewayComboResponse> CreateGatewayComboAsync(string endpointKey, GatewayComboInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateGatewayComboAsync(endpointKey, input, token), cancellationToken);
    public Task<GatewayComboResponse> UpdateGatewayComboAsync(Guid id, GatewayComboInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateGatewayComboAsync(id, input, token), cancellationToken);
    public Task DeleteGatewayComboAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteGatewayComboAsync(id, token); return true; }, cancellationToken);
    public Task<GatewayRouteResponse> CreateGatewayRouteAsync(Guid comboId, GatewayRouteInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateGatewayRouteAsync(comboId, input, token), cancellationToken);
    public Task<GatewayRouteResponse> UpdateGatewayRouteAsync(Guid id, GatewayRouteInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateGatewayRouteAsync(id, input, token), cancellationToken);
    public Task DeleteGatewayRouteAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteGatewayRouteAsync(id, token); return true; }, cancellationToken);

    private async Task<TResult> ExecuteManagementAsync<TResult>(Func<ConfigurationManagementService, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        await ConfigurationDatabase.InitializeAsync(db, cancellationToken);
        var provider = new DatabaseConfigurationProvider(db);
        await provider.ReloadAsync(cancellationToken);
        var service = new ConfigurationManagementService(new DesktopDbContextFactory(CreateOptions()), provider);
        return await operation(service, cancellationToken);
    }

    private ConfigurationDbContext CreateContext() => new(CreateOptions());
    private DbContextOptions<ConfigurationDbContext> CreateOptions() => new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
}

file sealed class DesktopDbContextFactory(DbContextOptions<ConfigurationDbContext> options) : IDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext() => new(options);
    public Task<ConfigurationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ConfigurationDbContext(options));
}
