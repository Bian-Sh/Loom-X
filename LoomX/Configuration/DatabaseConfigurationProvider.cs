using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LoomX.Logging;
using LoomX.Services;

namespace LoomX.Configuration;

public interface IDatabaseConfigurationProvider
{
    ResolvedAppConfig Current { get; }
    IReadOnlyList<ResolvedModelConfig> GetModels();
    ResolvedModelConfig? FindModel(string? modelName);
    Task ReloadAsync(CancellationToken cancellationToken = default);
    Task ApplyLocalChangeAsync(ConfigurationChangedEventArgs change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class DatabaseConfigurationProvider(ConfigurationDbContext dbContext, ILogger<DatabaseConfigurationProvider>? logger = null) : IDatabaseConfigurationProvider
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
            logger?.LogInformation("数据库配置重载开始");
            var gateway = await dbContext.GatewayConfigurations.AsNoTracking().SingleAsync(cancellationToken);
            var settings = await dbContext.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
            LoggingBootstrap.SetIncludeStackTrace(settings.LogStackTrace);
            var providers = await dbContext.Providers.AsNoTracking().Include(provider => provider.Models).ToListAsync(cancellationToken);
            var combos = await dbContext.GatewayCombos.AsNoTracking().AsSplitQuery()
                .Include(combo => combo.Routes).ThenInclude(route => route.Model).ThenInclude(model => model.Provider)
                .ToListAsync(cancellationToken);
            var endpoints = await dbContext.GatewayEndpoints.AsNoTracking()
                .Include(endpoint => endpoint.ComboBindings)
                .ToListAsync(cancellationToken);
            logger?.LogInformation("数据库配置行读取完成，Provider {ProviderCount}，模型 {ModelCount}，Endpoint {EndpointCount}，启用 Provider {EnabledProviderCount}", providers.Count, providers.Sum(provider => provider.Models.Count), endpoints.Count, providers.Count(provider => provider.Enabled));
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
                    ProxyMode = settings.ProxyMode,
                    ProxyHost = settings.ProxyHost,
                    ProxyPort = settings.ProxyPort,
                    ProxyUsername = settings.ProxyUsername,
                    HasProxyPassword = !string.IsNullOrWhiteSpace(settings.ProtectedProxyPassword),
                    AutoCheckUpdates = settings.AutoCheckUpdates,
                    UpdateChannel = settings.UpdateChannel,
                    UseProxyForUpdates = settings.UseProxyForUpdates,
                    DiagnosticsEnabled = settings.DiagnosticsEnabled,
                    LogRetentionDays = settings.LogRetentionDays,
                    LogStackTrace = settings.LogStackTrace,
                    TransparencyEnabled = settings.TransparencyEnabled,
                    TransparencyOpacity = settings.TransparencyOpacity,
                    BlurAmount = AppearanceSettingsLimits.NormalizeBlurAmount(settings.BlurAmount),
                    TransparencyAlgorithm = "acrylic"
                },
                Providers = resolvedProviders,
                Models = models,
                GatewayCombos = combos.Where(combo => !combo.IsDeleted).OrderBy(combo => combo.SortOrder).ThenBy(combo => combo.Name, StringComparer.OrdinalIgnoreCase).Select(combo => new ResolvedGatewayComboConfig
                {
                    Id = combo.Id,
                    Name = combo.Name,
                    Enabled = combo.Enabled,
                    SortOrder = combo.SortOrder,
                    Routes = combo.Routes.OrderBy(route => route.SortOrder).Where(route => modelLookup.ContainsKey(route.ModelId)).Select(route => new ResolvedGatewayRouteConfig
                    {
                        Model = modelLookup[route.ModelId],
                        Enabled = route.Enabled,
                        SortOrder = route.SortOrder
                    }).ToArray()
                }).ToArray(),
                GatewayEndpoints = endpoints.OrderBy(endpoint => endpoint.Key).Select(endpoint => new ResolvedGatewayEndpointConfig
                {
                    Key = endpoint.Key,
                    PublicPath = endpoint.PublicPath,
                    Enabled = endpoint.Enabled,
                    ApiKey = ReadEndpointApiKey(endpoint),
                    ReasoningEffort = GatewayEndpointSettings.NormalizeReasoningEffort(endpoint.ReasoningEffort),
                    ComboBindings = endpoint.ComboBindings.OrderBy(binding => binding.SortOrder).Select(binding => new ResolvedGatewayComboBindingConfig
                    {
                        ComboId = binding.ComboId,
                        Enabled = binding.Enabled,
                        SortOrder = binding.SortOrder
                    }).ToArray()
                }).ToArray()
            });
            logger?.LogInformation("数据库配置重载完成，Provider {ProviderCount}，模型 {ModelCount}，Endpoint {EndpointCount}", current.Providers.Count, current.Models.Count, current.GatewayEndpoints.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("数据库配置重载已取消");
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "数据库配置重载失败");
            throw;
        }
        finally
        {
            reloadLock.Release();
        }
    }

    public async Task ApplyLocalChangeAsync(ConfigurationChangedEventArgs change, CancellationToken cancellationToken = default)
    {
        await reloadLock.WaitAsync(cancellationToken);
        try
        {
            switch (change.Kind)
            {
                case ConfigurationChangeKind.Settings:
                    await ApplySettingsAsync(cancellationToken);
                    break;
                case ConfigurationChangeKind.Provider when change.EntityId is Guid providerId:
                    await ApplyProviderAsync(providerId, change.EntityKey, cancellationToken);
                    break;
                case ConfigurationChangeKind.Model when change.EntityId is Guid modelId:
                    await ApplyModelAsync(modelId, cancellationToken);
                    break;
                case ConfigurationChangeKind.GatewayEndpoint when change.EntityKey is not null:
                    await ApplyEndpointAsync(change.EntityKey, cancellationToken);
                    break;
                case ConfigurationChangeKind.GatewayCombo when change.EntityId is Guid comboId:
                    await ApplyComboAsync(comboId, cancellationToken);
                    break;
                case ConfigurationChangeKind.GatewayRoute when change.EntityId is Guid routeId:
                    await ApplyRouteAsync(routeId, cancellationToken);
                    break;
            }
        }
        finally { reloadLock.Release(); }
    }

    private async Task ApplySettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        LoggingBootstrap.SetIncludeStackTrace(settings.LogStackTrace);
        var snapshot = Current;
        Volatile.Write(ref current, WithCurrent(snapshot, settings: new ResolvedAppSettings
        {
            Language = settings.Language,
            Theme = settings.Theme,
            ProxyMode = settings.ProxyMode,
            ProxyHost = settings.ProxyHost,
            ProxyPort = settings.ProxyPort,
            ProxyUsername = settings.ProxyUsername,
            HasProxyPassword = !string.IsNullOrWhiteSpace(settings.ProtectedProxyPassword),
            AutoCheckUpdates = settings.AutoCheckUpdates,
            UpdateChannel = settings.UpdateChannel,
            UseProxyForUpdates = settings.UseProxyForUpdates,
            DiagnosticsEnabled = settings.DiagnosticsEnabled,
            LogRetentionDays = settings.LogRetentionDays,
            LogStackTrace = settings.LogStackTrace,
            TransparencyEnabled = settings.TransparencyEnabled,
            TransparencyOpacity = settings.TransparencyOpacity,
            BlurAmount = AppearanceSettingsLimits.NormalizeBlurAmount(settings.BlurAmount),
            TransparencyAlgorithm = "acrylic"
        }));
    }

    private async Task ApplyProviderAsync(Guid providerId, string? previousBusinessId, CancellationToken cancellationToken)
    {
        var provider = await dbContext.Providers.AsNoTracking().Include(item => item.Models).SingleOrDefaultAsync(item => item.Id == providerId, cancellationToken);
        if (provider is null) return;
        var snapshot = Current;
        var resolvedProvider = new ResolvedProviderConfig
        {
            Id = provider.BusinessId,
            BaseUrl = provider.BaseUrl,
            ApiModes = SplitApiModes(provider.ApiMode),
            EndpointFormat = provider.EndpointFormat,
            HasApiKey = !string.IsNullOrWhiteSpace(provider.ProtectedApiKey),
            UseProxy = provider.UseProxy
        };
        var models = snapshot.Models.Where(model => !string.Equals(model.ProviderId, provider.BusinessId, StringComparison.OrdinalIgnoreCase) && !string.Equals(model.ProviderId, previousBusinessId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (provider.Enabled) models.AddRange(provider.Models.Where(model => model.Enabled).Select(model => ResolveModel(provider, model)));
        var providers = snapshot.Providers.Where(item => !string.Equals(item.Id, provider.BusinessId, StringComparison.OrdinalIgnoreCase) && !string.Equals(item.Id, previousBusinessId, StringComparison.OrdinalIgnoreCase)).Append(resolvedProvider).ToArray();
        var affected = provider.Models.Select(model => ModelKey(provider.BusinessId, model.ModelId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(previousBusinessId))
            foreach (var model in snapshot.Models.Where(model => string.Equals(model.ProviderId, previousBusinessId, StringComparison.OrdinalIgnoreCase))) affected.Add(ModelKey(previousBusinessId, model.ModelId));
        Volatile.Write(ref current, WithCurrent(snapshot, providers: providers, models: models, combos: ReplaceRouteModels(snapshot.GatewayCombos, models, affected)));
    }

    private async Task ApplyModelAsync(Guid modelId, CancellationToken cancellationToken)
    {
        var model = await dbContext.Models.AsNoTracking().Include(item => item.Provider).SingleOrDefaultAsync(item => item.Id == modelId, cancellationToken);
        if (model is null) return;
        var snapshot = Current;
        var key = ModelKey(model.Provider.BusinessId, model.ModelId);
        var models = snapshot.Models.Where(item => !string.Equals(ModelKey(item.ProviderId, item.ModelId), key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (model.Enabled && model.Provider.Enabled) models.Add(ResolveModel(model.Provider, model));
        var combos = ReplaceRouteModels(snapshot.GatewayCombos, models, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key }).ToArray();
        if (model.Enabled && model.Provider.Enabled)
        {
            var routes = await dbContext.GatewayRoutes.AsNoTracking().Where(item => item.ModelId == modelId).ToArrayAsync(cancellationToken);
            var resolvedModel = models.FirstOrDefault(item => string.Equals(ModelKey(item.ProviderId, item.ModelId), key, StringComparison.OrdinalIgnoreCase));
            if (resolvedModel is not null)
                foreach (var route in routes)
                {
                    var index = Array.FindIndex(combos, combo => combo.Id == route.ComboId);
                    if (index < 0) continue;
                    var combo = combos[index];
                    if (combo.Routes.Any(item => string.Equals(item.Model.ModelId, resolvedModel.ModelId, StringComparison.OrdinalIgnoreCase))) continue;
                    combos[index] = new ResolvedGatewayComboConfig { Id = combo.Id, Name = combo.Name, Enabled = combo.Enabled, SortOrder = combo.SortOrder, Routes = [.. combo.Routes, new ResolvedGatewayRouteConfig { Model = resolvedModel, Enabled = route.Enabled, SortOrder = route.SortOrder }] };
                }
        }
        Volatile.Write(ref current, WithCurrent(snapshot, models: models, combos: combos));
    }

    private async Task ApplyEndpointAsync(string key, CancellationToken cancellationToken)
    {
        var endpoint = await dbContext.GatewayEndpoints.AsNoTracking().Include(item => item.ComboBindings).SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (endpoint is null) return;
        var resolved = new ResolvedGatewayEndpointConfig
        {
            Key = endpoint.Key,
            PublicPath = endpoint.PublicPath,
            Enabled = endpoint.Enabled,
            ApiKey = ReadEndpointApiKey(endpoint),
            ReasoningEffort = GatewayEndpointSettings.NormalizeReasoningEffort(endpoint.ReasoningEffort),
            ComboBindings = endpoint.ComboBindings.OrderBy(item => item.SortOrder).Select(item => new ResolvedGatewayComboBindingConfig { ComboId = item.ComboId, Enabled = item.Enabled, SortOrder = item.SortOrder }).ToArray()
        };
        var snapshot = Current;
        Volatile.Write(ref current, WithCurrent(snapshot, endpoints: snapshot.GatewayEndpoints.Select(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase) ? resolved : item).ToArray()));
    }

    private async Task ApplyComboAsync(Guid comboId, CancellationToken cancellationToken)
    {
        var combo = await dbContext.GatewayCombos.AsNoTracking().Include(item => item.Routes).ThenInclude(item => item.Model).ThenInclude(item => item.Provider).SingleOrDefaultAsync(item => item.Id == comboId, cancellationToken);
        if (combo is null || combo.IsDeleted)
        {
            var currentSnapshot = Current;
            Volatile.Write(ref current, WithCurrent(currentSnapshot, combos: currentSnapshot.GatewayCombos.Where(item => item.Id != comboId).ToArray()));
            return;
        }
        var snapshot = Current;
        var modelLookup = snapshot.Models.ToDictionary(item => ModelKey(item.ProviderId, item.ModelId), StringComparer.OrdinalIgnoreCase);
        var routes = combo.Routes.OrderBy(item => item.SortOrder).Where(item => item.Model.Provider.Enabled && item.Model.Enabled).Select(item => modelLookup.TryGetValue(ModelKey(item.Model.Provider.BusinessId, item.Model.ModelId), out var model) ? new ResolvedGatewayRouteConfig { Model = model, Enabled = item.Enabled, SortOrder = item.SortOrder } : null).Where(item => item is not null).Cast<ResolvedGatewayRouteConfig>().ToArray();
        var resolved = new ResolvedGatewayComboConfig { Id = combo.Id, Name = combo.Name, Enabled = combo.Enabled, SortOrder = combo.SortOrder, Routes = routes };
        Volatile.Write(ref current, WithCurrent(snapshot, combos: snapshot.GatewayCombos.Select(item => item.Id == comboId ? resolved : item).ToArray()));
    }

    private async Task ApplyRouteAsync(Guid routeId, CancellationToken cancellationToken)
    {
        var route = await dbContext.GatewayRoutes.AsNoTracking().Include(item => item.Model).ThenInclude(item => item.Provider).SingleOrDefaultAsync(item => item.Id == routeId, cancellationToken);
        if (route is null) return;
        var snapshot = Current;
        var model = snapshot.Models.FirstOrDefault(item => string.Equals(item.ProviderId, route.Model.Provider.BusinessId, StringComparison.OrdinalIgnoreCase) && string.Equals(item.ModelId, route.Model.ModelId, StringComparison.OrdinalIgnoreCase));
        if (model is null) return;
        var combo = snapshot.GatewayCombos.FirstOrDefault(item => item.Id == route.ComboId);
        if (combo is null) return;
        var routes = combo.Routes.ToList();
        var index = routes.FindIndex(item => string.Equals(item.Model.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase));
        var replacement = new ResolvedGatewayRouteConfig { Model = model, Enabled = route.Enabled, SortOrder = route.SortOrder };
        if (index >= 0) routes[index] = replacement; else routes.Add(replacement);
        Volatile.Write(ref current, WithCurrent(snapshot, combos: snapshot.GatewayCombos.Select(item => item.Id == combo.Id ? new ResolvedGatewayComboConfig { Id = item.Id, Name = item.Name, Enabled = item.Enabled, SortOrder = item.SortOrder, Routes = routes } : item).ToArray()));
    }

    private static ResolvedAppConfig WithCurrent(ResolvedAppConfig source, ResolvedAppSettings? settings = null, IReadOnlyList<ResolvedProviderConfig>? providers = null, IReadOnlyList<ResolvedModelConfig>? models = null, IReadOnlyList<ResolvedGatewayComboConfig>? combos = null, IReadOnlyList<ResolvedGatewayEndpointConfig>? endpoints = null) => new()
    {
        Server = source.Server,
        Settings = settings ?? source.Settings,
        Providers = providers ?? source.Providers,
        Models = models ?? source.Models,
        GatewayCombos = combos ?? source.GatewayCombos,
        GatewayEndpoints = endpoints ?? source.GatewayEndpoints
    };

    private static IReadOnlyList<ResolvedGatewayComboConfig> ReplaceRouteModels(IReadOnlyList<ResolvedGatewayComboConfig> combos, IReadOnlyList<ResolvedModelConfig> models, IReadOnlySet<string> affectedKeys)
    {
        var lookup = models.ToDictionary(model => ModelKey(model.ProviderId, model.ModelId), StringComparer.OrdinalIgnoreCase);
        return combos.Select(combo =>
        {
            if (!combo.Routes.Any(route => affectedKeys.Contains(ModelKey(route.Model.ProviderId, route.Model.ModelId)))) return combo;
            var routes = combo.Routes.Select(route =>
            {
                var routeKey = ModelKey(route.Model.ProviderId, route.Model.ModelId);
                if (lookup.TryGetValue(routeKey, out var model)) return new ResolvedGatewayRouteConfig { Model = model, Enabled = route.Enabled, SortOrder = route.SortOrder };
                var renamedModel = models.FirstOrDefault(item => string.Equals(item.ModelId, route.Model.ModelId, StringComparison.OrdinalIgnoreCase));
                return renamedModel is null ? null : new ResolvedGatewayRouteConfig { Model = renamedModel, Enabled = route.Enabled, SortOrder = route.SortOrder };
            }).Where(route => route is not null).Cast<ResolvedGatewayRouteConfig>().ToArray();
            return new ResolvedGatewayComboConfig { Id = combo.Id, Name = combo.Name, Enabled = combo.Enabled, SortOrder = combo.SortOrder, Routes = routes };
        }).ToArray();
    }

    private static string ModelKey(string providerId, string modelId) => $"{providerId}\u001f{modelId}";

    private string ReadEndpointApiKey(GatewayEndpointEntity endpoint)
    {
        if (!GatewayEndpointSettings.RequiresApiKey(endpoint.Key) || string.IsNullOrWhiteSpace(endpoint.ProtectedApiKey)) return string.Empty;
        if (ProtectedApiKeyStore.TryUnprotect(endpoint.ProtectedApiKey, out var apiKey)) return apiKey;
        logger?.LogWarning("Endpoint 密钥解密失败，继续加载配置 {EndpointKey}", endpoint.Key);
        return string.Empty;
    }

    private ResolvedModelConfig ResolveModel(ProviderEntity provider, ModelEntity model)
    {
        var headers = ReadDictionary(provider.HeadersJson);
        foreach (var pair in ReadDictionary(model.HeadersJson)) headers[pair.Key] = pair.Value;
        var protectedApiKey = string.IsNullOrWhiteSpace(model.ProtectedApiKey) ? provider.ProtectedApiKey : model.ProtectedApiKey;
        var apiKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(protectedApiKey) && !ProtectedApiKeyStore.TryUnprotect(protectedApiKey, out apiKey))
            logger?.LogWarning("Provider 密钥解密失败，继续加载配置 {ProviderId}", provider.BusinessId);
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
