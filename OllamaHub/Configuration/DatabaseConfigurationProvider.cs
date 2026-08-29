using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OllamaHub.Configuration;

public interface IDatabaseConfigurationProvider
{
    ResolvedAppConfig Current { get; }
    IReadOnlyList<ResolvedModelConfig> GetModels();
    ResolvedModelConfig? FindModel(string? modelName);
    Task ReloadAsync(CancellationToken cancellationToken = default);
}

public sealed class DatabaseConfigurationProvider(ConfigurationDbContext dbContext) : IDatabaseConfigurationProvider
{
    private readonly SemaphoreSlim reloadLock = new(1, 1);
    private ResolvedAppConfig current = new();

    public ResolvedAppConfig Current => Volatile.Read(ref current);

    public IReadOnlyList<ResolvedModelConfig> GetModels() => Current.Models;

    public ResolvedModelConfig? FindModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;
        var normalized = modelName.Trim();
        return GetModels().FirstOrDefault(model => string.Equals(model.OllamaModelName, normalized, StringComparison.OrdinalIgnoreCase))
            ?? GetModels().FirstOrDefault(model => string.Equals(model.DisplayName, normalized, StringComparison.OrdinalIgnoreCase))
            ?? GetModels().FirstOrDefault(model => string.Equals(model.ModelId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await reloadLock.WaitAsync(cancellationToken);
        try
        {
            var gateway = await dbContext.GatewayConfigurations.AsNoTracking().SingleAsync(cancellationToken);
            var settings = await dbContext.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
            var providers = await dbContext.Providers.AsNoTracking().Include(provider => provider.Models).ToListAsync(cancellationToken);
            var endpoints = await dbContext.GatewayEndpoints.AsNoTracking().Include(endpoint => endpoint.Routes).ThenInclude(route => route.Model).ThenInclude(model => model.Provider).ToListAsync(cancellationToken);
            var resolvedProviders = providers.OrderBy(provider => provider.SortOrder).Select(provider => new ResolvedProviderConfig
            {
                Id = provider.BusinessId,
                BaseUrl = provider.BaseUrl,
                ApiModes = SplitApiModes(provider.ApiMode),
                EndpointFormat = provider.EndpointFormat,
                HasApiKey = !string.IsNullOrWhiteSpace(provider.ProtectedApiKey),
                UseProxy = provider.UseProxy
            }).ToArray();
            var models = providers.Where(provider => provider.Enabled)
                .SelectMany(provider => provider.Models.Where(model => model.Enabled).OrderBy(model => model.SortOrder).Select(model => ResolveModel(provider, model)))
                .ToArray();
            var modelLookup = providers.Where(provider => provider.Enabled).SelectMany(provider => provider.Models.Where(model => model.Enabled).Select(model => (model.Id, Config: ResolveModel(provider, model)))).ToDictionary(item => item.Id, item => item.Config);
            Volatile.Write(ref current, new ResolvedAppConfig
            {
                Server = new ResolvedServerConfig { Urls = [gateway.ListenUrl] },
                Settings = new ResolvedAppSettings
                {
                    Language = settings.Language,
                    Theme = settings.Theme,
                    OpenControlCenterOnStartup = settings.OpenControlCenterOnStartup,
                    ProxyMode = settings.ProxyMode,
                    ProxyHost = settings.ProxyHost,
                    ProxyPort = settings.ProxyPort,
                    ProxyUsername = settings.ProxyUsername,
                    HasProxyPassword = !string.IsNullOrWhiteSpace(settings.ProtectedProxyPassword),
                    AutoCheckUpdates = settings.AutoCheckUpdates,
                    UpdateChannel = settings.UpdateChannel,
                    DiagnosticsEnabled = settings.DiagnosticsEnabled,
                    LogRetentionDays = settings.LogRetentionDays
                },
                Providers = resolvedProviders,
                Models = models,
                GatewayEndpoints = endpoints.OrderBy(endpoint => endpoint.Key).Select(endpoint => new ResolvedGatewayEndpointConfig
                {
                    Key = endpoint.Key,
                    PublicPath = endpoint.PublicPath,
                    Enabled = endpoint.Enabled,
                    Routes = endpoint.Routes.OrderBy(route => route.SortOrder).Where(route => modelLookup.ContainsKey(route.ModelId)).Select(route => new ResolvedGatewayRouteConfig
                    {
                        Alias = string.IsNullOrWhiteSpace(route.Alias) ? modelLookup[route.ModelId].OllamaModelName : route.Alias!,
                        Model = modelLookup[route.ModelId],
                        Enabled = route.Enabled,
                        SortOrder = route.SortOrder
                    }).ToArray()
                }).ToArray()
            });
        }
        finally
        {
            reloadLock.Release();
        }
    }

    private static ResolvedModelConfig ResolveModel(ProviderEntity provider, ModelEntity model)
    {
        var headers = ReadDictionary(provider.HeadersJson);
        foreach (var pair in ReadDictionary(model.HeadersJson)) headers[pair.Key] = pair.Value;
        var protectedApiKey = string.IsNullOrWhiteSpace(model.ProtectedApiKey) ? provider.ProtectedApiKey : model.ProtectedApiKey;
        var apiKey = string.IsNullOrWhiteSpace(protectedApiKey) ? string.Empty : ProtectedApiKeyStore.Unprotect(protectedApiKey);
        var apiMode = string.IsNullOrWhiteSpace(model.ApiMode) ? provider.ApiMode : model.ApiMode;
        return new ResolvedModelConfig
        {
            ModelId = model.ModelId,
            AnthropicModel = model.ModelId,
            UseProxy = provider.UseProxy,
            ProviderId = provider.BusinessId,
            ApiModes = SplitApiModes(apiMode),
            EndpointFormat = provider.EndpointFormat,
            BaseUrl = (string.IsNullOrWhiteSpace(model.BaseUrl) ? provider.BaseUrl : model.BaseUrl).TrimEnd('/'),
            ApiKey = apiKey,
            DisplayName = model.DisplayName,
            OllamaModelName = string.IsNullOrWhiteSpace(model.ConfigId) ? model.DisplayName : $"{model.DisplayName}::{model.ConfigId}",
            Family = model.Family,
            ContextLength = model.ContextLength,
            MaxTokens = model.MaxTokens,
            Vision = model.Vision,
            Temperature = model.Temperature,
            TopP = model.TopP,
            Headers = headers,
            Extra = ReadJson(model.ExtraJson)
        };
    }

    internal static IReadOnlyList<string> SplitApiModes(string? raw)
    {
        var modes = raw?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(mode => !string.IsNullOrWhiteSpace(mode)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        return modes.Length > 0 ? modes : ["openai"];
    }

    internal static Dictionary<string, string> ReadDictionary(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.OrdinalIgnoreCase);

    internal static Dictionary<string, JsonNode?> ReadJson(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(json) ?? new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConfigurationRefreshService(IDatabaseConfigurationProvider configurationProvider, ILogger<ConfigurationRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await configurationProvider.ReloadAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogWarning(exception, "刷新 SQLite 配置快照失败"); }
        }
    }
}
