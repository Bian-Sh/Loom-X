using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using LoomX.Activity;
using LoomX.Configuration;

namespace LoomX.Services;

public sealed class AppDataStore : IDisposable
{
    public const int ActivityWindowLimit = 500;

    private readonly ConfigSnapshotService configService;
    private readonly ActivityQueryService activityQueryService;
    private readonly GatewayProcessService gatewayService;
    private readonly ILogger<AppDataStore> logger;
    private readonly SemaphoreSlim stateLock = new(1, 1);
    private readonly object initializationGate = new();
    private readonly List<ActivityEventRecord> activityWindow = [];
    private readonly List<ActivityEventRecord> pendingActivities = [];
    private Task? initializationTask;
    private bool disposed;
    private bool isLoading;
    private ActivityQuery? activityQuery;
    private ActivityCursor? activityCursor;
    private bool activityHasMore;
    private bool activityHistoryMode;
    private int pendingActivityCount;

    public ResolvedAppConfig CurrentConfig { get; private set; } = new();
    public IReadOnlyList<ProviderResponse> Providers { get; private set; } = [];
    public IReadOnlyList<GatewayEndpointResponse> GatewayEndpoints { get; private set; } = [];
    public IReadOnlyList<GatewayComboResponse> GatewayCombos { get; private set; } = [];
    public AppSettingsResponse? Settings { get; private set; }
    public IReadOnlyList<GatewayModelSourceResponse> EnabledGatewayModels { get; private set; } = [];
    public IReadOnlyList<ActivityEventRecord> ActivityWindow => activityWindow.ToArray();
    public bool ActivityHasMore => activityHasMore;
    public bool ActivityHistoryMode => activityHistoryMode;
    public int PendingActivityCount => pendingActivityCount;
    public bool IsInitialized { get; private set; }
    public bool IsLoading => isLoading;
    public Exception? InitializationError { get; private set; }

    public event EventHandler? ConfigurationReady;
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
    public event EventHandler? ActivityWindowChanged;

    public AppDataStore(
        ConfigSnapshotService configService,
        GatewayProcessService gatewayService,
        ILogger<AppDataStore>? logger = null,
        ActivityQueryService? activityQueryService = null)
    {
        this.configService = configService;
        this.gatewayService = gatewayService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AppDataStore>.Instance;
        this.activityQueryService = activityQueryService ?? new ActivityQueryService();
        gatewayService.ActivityEnqueued += OnActivityEnqueued;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (initializationGate)
        {
            if (initializationTask is { IsCompleted: true } completedTask && (completedTask.IsFaulted || completedTask.IsCanceled))
                initializationTask = null;
            initializationTask ??= InitializeCoreAsync(cancellationToken);
            return initializationTask;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.Snapshot, ConfigurationChangeKind.Snapshot));

    public async Task<IReadOnlyList<ProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return Providers;
    }

    public async Task<AppSettingsResponse?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return Settings;
    }

    public async Task<UpdateProxySettings> GetUpdateProxySettingsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return await configService.GetUpdateProxySettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayEndpointResponse>> ListGatewayEndpointsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return GatewayEndpoints;
    }

    public async Task<IReadOnlyList<GatewayComboResponse>> ListGatewayCombosAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return GatewayCombos;
    }

    public async Task<IReadOnlyList<GatewayModelSourceResponse>> ListEnabledGatewayModelsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return EnabledGatewayModels;
    }

    public Task<ActivityPage> QueryRecentActivitiesAsync(int limit = 8, CancellationToken cancellationToken = default) =>
        activityQueryService.QueryPageAsync(new ActivityQuery(Limit: Math.Clamp(limit, 1, 500)), null, cancellationToken);

    public Task<ActivityPage> QueryRecentActivitiesAsync(ActivityQuery query, CancellationToken cancellationToken = default) =>
        activityQueryService.QueryPageAsync(query with { Limit = Math.Clamp(query.Limit, 1, 500) }, null, cancellationToken);

    public async Task<AppSettingsResponse> UpdateSettingsAsync(AppSettingsInput input, CancellationToken cancellationToken = default)
    {
        var previous = Settings;
        var result = await configService.UpdateSettingsAsync(input, cancellationToken);
        var fields = GetSettingsFields(previous, result);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Settings, EntityKey: "settings", Fields: fields),
            () => ApplySettingsSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task<ProviderResponse> CreateProviderAsync(ProviderInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateProviderAsync(input, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Provider, result.Id));
        return result;
    }

    public async Task<ProviderResponse> UpdateProviderAsync(Guid id, ProviderInput input, CancellationToken cancellationToken = default)
    {
        var previous = Providers.FirstOrDefault(item => item.Id == id);
        var result = await configService.UpdateProviderAsync(id, input, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Provider, result.Id, EntityKey: previous?.BusinessId, Fields: GetProviderFields(previous, result)),
            () => ApplyProviderSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteProviderAsync(id, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Provider, id));
    }

    public async Task<ModelResponse> CreateModelAsync(Guid providerId, ModelInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateModelAsync(providerId, input, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Model, result.Id));
        return result;
    }

    public async Task<ModelResponse> UpdateModelAsync(Guid id, ModelInput input, CancellationToken cancellationToken = default)
    {
        var previous = Providers.SelectMany(item => item.Models).FirstOrDefault(item => item.Id == id);
        var result = await configService.UpdateModelAsync(id, input, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Model, result.Id, Fields: GetModelFields(previous, result)),
            () => ApplyModelSnapshot(result, previous),
            cancellationToken);
        return result;
    }

    internal async Task<ModelResponse> UpdateModelEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var existing = Providers.SelectMany(provider => provider.Models).FirstOrDefault(model => model.Id == id) ?? throw new KeyNotFoundException("模型不存在。");
        await configService.UpdateModelEnabledAsync(id, enabled, cancellationToken);
        var result = existing with { Enabled = enabled };
        var routes = enabled ? await configService.ListRoutesForModelAsync(id, cancellationToken) : [];
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Model, result.Id, Fields: ConfigurationChangeFields.ModelAvailability),
            () => { ApplyModelSnapshot(result, existing); if (enabled && routes.Count > 0) ApplyModelRoutes(routes); },
            cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ModelResponse>> UpdateModelOrderAsync(Guid providerId, ModelOrderInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateModelOrderAsync(providerId, input, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Provider, providerId, Fields: ConfigurationChangeFields.ModelMetadata),
            () => ApplyModelOrderSnapshot(providerId, result),
            cancellationToken);
        return result;
    }

    public async Task DeleteModelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteModelAsync(id, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.Model, id));
    }

    public async Task<GatewayEndpointResponse> SetGatewayEndpointEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        var result = await configService.SetGatewayEndpointEnabledAsync(key, enabled, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayEndpoint, EntityKey: result.Key, Fields: ConfigurationChangeFields.EndpointAvailability),
            () => ApplyEndpointSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task<GatewayEndpointResponse> RotateGatewayApiKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await configService.RotateGatewayApiKeyAsync(key, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayEndpoint, EntityKey: result.Key, Fields: ConfigurationChangeFields.EndpointCredentials),
            () => ApplyEndpointSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task<GatewayEndpointResponse> UpdateGatewayEndpointReasoningEffortAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateGatewayEndpointReasoningEffortAsync(key, value, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayEndpoint, EntityKey: result.Key, Fields: ConfigurationChangeFields.EndpointReasoning),
            () => ApplyEndpointSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task<GatewayEndpointResponse> UpdateGatewayEndpointComboBindingsAsync(string endpointKey, GatewayEndpointComboSelectionInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateGatewayEndpointComboBindingsAsync(endpointKey, input, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayEndpoint, EntityKey: result.Key, Fields: ConfigurationChangeFields.EndpointBindings),
            () => ApplyEndpointSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task<GatewayComboResponse> CreateGatewayComboAsync(GatewayComboInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateGatewayComboAsync(input, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayCombo, result.Id));
        return result;
    }

    public async Task<GatewayComboResponse> UpdateGatewayComboAsync(Guid id, GatewayComboInput input, CancellationToken cancellationToken = default)
    {
        var previous = GatewayCombos.FirstOrDefault(item => item.Id == id);
        var result = await configService.UpdateGatewayComboAsync(id, input, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayCombo, id, Fields: GetComboFields(previous, result)),
            () => ApplyComboSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task DeleteGatewayComboAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteGatewayComboAsync(id, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayCombo, id));
    }

    public async Task<GatewayRouteResponse> CreateGatewayRouteAsync(Guid comboId, GatewayRouteInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateGatewayRouteAsync(comboId, input, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayRoute, comboId));
        return result;
    }

    public async Task<GatewayRouteResponse> UpdateGatewayRouteAsync(Guid id, GatewayRouteInput input, CancellationToken cancellationToken = default)
    {
        var previous = GatewayCombos.SelectMany(item => item.Routes).FirstOrDefault(item => item.Id == id);
        var result = await configService.UpdateGatewayRouteAsync(id, input, cancellationToken);
        await ApplyLocalChangeAsync(
            new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayRoute, id, ParentEntityId: result.ComboId, Fields: GetRouteFields(previous, result)),
            () => ApplyRouteSnapshot(result),
            cancellationToken);
        return result;
    }

    public async Task DeleteGatewayRouteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteGatewayRouteAsync(id, cancellationToken);
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false, new ConfigurationChangedEventArgs(ConfigurationChangeSource.LocalSave, ConfigurationChangeKind.GatewayRoute, id));
    }

    public async Task<ActivityPage> LoadActivityPageAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var normalized = NormalizeActivityQuery(query);
            activityQuery = normalized;
            activityHistoryMode = false;
            pendingActivities.Clear();
            pendingActivityCount = 0;
            var page = await activityQueryService.QueryPageAsync(normalized, null, cancellationToken);
            ReplaceActivityWindow(page);
            ActivityWindowChanged?.Invoke(this, EventArgs.Empty);
            return BuildActivityPage();
        }
        finally { stateLock.Release(); }
    }

    public async Task<ActivityPage> LoadOlderActivityPageAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var normalized = NormalizeActivityQuery(query);
            if (activityQuery is null || !Equals(activityQuery, normalized))
            {
                activityQuery = normalized;
                var first = await activityQueryService.QueryPageAsync(normalized, null, cancellationToken);
                ReplaceActivityWindow(first);
            }
            activityHistoryMode = true;
            if (!activityHasMore || activityCursor is null) return BuildActivityPage();
            var page = await activityQueryService.QueryPageAsync(normalized, activityCursor, cancellationToken);
            AppendOlder(page);
            ActivityWindowChanged?.Invoke(this, EventArgs.Empty);
            return BuildActivityPage();
        }
        finally { stateLock.Release(); }
    }

    public async Task<ActivityPage> ReturnToLatestAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var pending = pendingActivities.ToArray();
            var normalized = NormalizeActivityQuery(query);
            var page = await activityQueryService.QueryPageAsync(normalized, null, cancellationToken);
            activityQuery = normalized;
            ReplaceActivityWindow(page);
            foreach (var item in pending.Reverse())
                if (MatchesActivityQuery(item, normalized) && !ContainsActivity(item, activityWindow)) activityWindow.Insert(0, item);
            while (activityWindow.Count > ActivityWindowLimit) activityWindow.RemoveAt(activityWindow.Count - 1);
            activityHistoryMode = false;
            pendingActivities.Clear();
            pendingActivityCount = 0;
            ActivityWindowChanged?.Invoke(this, EventArgs.Empty);
            return BuildActivityPage();
        }
        finally { stateLock.Release(); }
    }

    public void SetActivityHistoryMode(bool value)
    {
        activityHistoryMode = value;
        if (!value)
        {
            pendingActivities.Clear();
            pendingActivityCount = 0;
        }
        ActivityWindowChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReloadCoreAsync(cancellationToken, isInitialLoad: true, new ConfigurationChangedEventArgs(ConfigurationChangeSource.Initialization, ConfigurationChangeKind.Snapshot));
            IsInitialized = true;
            InitializationError = null;
            ConfigurationReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            InitializationError = exception;
            logger.LogError(exception, "桌面数据中心初始化失败");
            throw;
        }
    }

    private async Task ReloadCoreAsync(CancellationToken cancellationToken, bool isInitialLoad, ConfigurationChangedEventArgs? changeArgs = null)
    {
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            isLoading = true;
            var config = await configService.LoadAsync(cancellationToken);
            var providers = await configService.ListProvidersAsync(cancellationToken);
            var settings = await configService.GetSettingsAsync(cancellationToken);
            var endpoints = await configService.ListGatewayEndpointsAsync(cancellationToken);
            var combos = await configService.ListGatewayCombosAsync(cancellationToken);
            var enabledModels = await configService.ListEnabledGatewayModelsAsync(cancellationToken);
            CurrentConfig = config;
            Providers = providers;
            Settings = settings;
            GatewayEndpoints = endpoints;
            GatewayCombos = combos;
            EnabledGatewayModels = enabledModels;
            logger.LogInformation("桌面数据中心配置快照完成 {ProviderCount} {ModelCount} {EndpointCount}", providers.Count, config.Models.Count, endpoints.Count);
            ConfigurationChanged?.Invoke(this, changeArgs ?? new ConfigurationChangedEventArgs(ConfigurationChangeSource.Snapshot, ConfigurationChangeKind.Snapshot));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("桌面数据中心配置刷新已取消");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "桌面数据中心配置刷新失败");
            if (isInitialLoad) InitializationError = exception;
            throw;
        }
        finally
        {
            isLoading = false;
            stateLock.Release();
        }
    }

    private void ApplyModelRoutes(IReadOnlyList<GatewayRouteResponse> routes)
    {
        foreach (var route in routes)
        {
            var combo = CurrentConfig.GatewayCombos.FirstOrDefault(item => item.Id == route.ComboId);
            var resolvedRoute = ToResolvedRoute(route);
            if (combo is null || resolvedRoute is null) continue;
            var routeList = combo.Routes.ToList();
            if (!routeList.Any(item => string.Equals(item.Model.DisplayName, route.ModelName, StringComparison.OrdinalIgnoreCase))) routeList.Add(resolvedRoute);
            var updated = new ResolvedGatewayComboConfig { Id = combo.Id, Name = combo.Name, Enabled = combo.Enabled, SortOrder = combo.SortOrder, Routes = routeList };
            CurrentConfig = WithConfig(CurrentConfig, combos: CurrentConfig.GatewayCombos.Select(item => item.Id == combo.Id ? updated : item).ToArray());
        }
    }

    private async Task ApplyLocalChangeAsync(ConfigurationChangedEventArgs change, Action applySnapshot, CancellationToken cancellationToken)
    {
        await stateLock.WaitAsync(cancellationToken);
        try { applySnapshot(); }
        finally { stateLock.Release(); }

        var runtimeProvider = gatewayService.GetHostedService<IDatabaseConfigurationProvider>();
        if (runtimeProvider is not null)
        {
            try { await runtimeProvider.ApplyLocalChangeAsync(change, cancellationToken); }
            catch (Exception exception) { logger.LogError(exception, "运行时配置局部更新失败 {Kind} {EntityId} {EntityKey}", change.Kind, change.EntityId, change.EntityKey); }
        }

        ConfigurationChanged?.Invoke(this, change);
    }

    private void ApplySettingsSnapshot(AppSettingsResponse result)
    {
        Settings = result;
        CurrentConfig = WithConfig(CurrentConfig, settings: new ResolvedAppSettings
        {
            Language = result.Language,
            Theme = result.Theme,
            ProxyMode = result.ProxyMode,
            ProxyHost = result.ProxyHost,
            ProxyPort = result.ProxyPort,
            ProxyUsername = result.ProxyUsername,
            HasProxyPassword = result.HasProxyPassword,
            AutoCheckUpdates = result.AutoCheckUpdates,
            UpdateChannel = result.UpdateChannel,
            UseProxyForUpdates = result.UseProxyForUpdates,
            DiagnosticsEnabled = result.DiagnosticsEnabled,
            LogRetentionDays = result.LogRetentionDays,
            LogStackTrace = result.LogStackTrace,
            TransparencyEnabled = result.TransparencyEnabled,
            TransparencyOpacity = result.TransparencyOpacity,
            BlurAmount = result.BlurAmount,
            TransparencyAlgorithm = result.TransparencyAlgorithm
        });
    }

    private void ApplyProviderSnapshot(ProviderResponse result)
    {
        var previous = Providers.FirstOrDefault(item => item.Id == result.Id);
        Providers = Providers.Select(item => item.Id == result.Id ? result : item).ToArray();
        var oldBusinessId = previous?.BusinessId;
        var affectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (oldBusinessId is not null)
            foreach (var model in previous!.Models) affectedKeys.Add(ModelKey(oldBusinessId, model.ModelId));
        foreach (var model in result.Models) affectedKeys.Add(ModelKey(result.BusinessId, model.ModelId));

        var resolvedProvider = new ResolvedProviderConfig
        {
            Id = result.BusinessId,
            BaseUrl = result.BaseUrl,
            ApiModes = DatabaseConfigurationProvider.SplitApiModes(result.ApiMode),
            EndpointFormat = result.EndpointFormat,
            HasApiKey = result.HasApiKey,
            UseProxy = result.UseProxy
        };
        var models = CurrentConfig.Models.Where(model => !affectedKeys.Contains(ModelKey(model.ProviderId, model.ModelId))).ToList();
        if (result.Enabled)
            models.AddRange(result.Models.Where(model => model.Enabled).Select(model => ToResolvedModel(result, model, CurrentConfig.Models.FirstOrDefault(item => item.ModelId == model.ModelId && string.Equals(item.ProviderId, oldBusinessId ?? result.BusinessId, StringComparison.OrdinalIgnoreCase)))));
        var providers = CurrentConfig.Providers.Where(provider => !string.Equals(provider.Id, oldBusinessId, StringComparison.OrdinalIgnoreCase)).Append(resolvedProvider).ToArray();
        CurrentConfig = WithConfig(CurrentConfig, providers: providers, models: models, combos: ReplaceRouteModels(CurrentConfig.GatewayCombos, models, affectedKeys));
        RebuildEnabledGatewayModels();
    }

    private void ApplyModelOrderSnapshot(Guid providerId, IReadOnlyList<ModelResponse> result)
    {
        var provider = Providers.FirstOrDefault(item => item.Id == providerId);
        if (provider is not null)
            Providers = Providers.Select(item => item.Id == providerId ? item with { Models = result, ModelCount = result.Count } : item).ToArray();
        RebuildEnabledGatewayModels();
    }

    private void ApplyModelSnapshot(ModelResponse result, ModelResponse? previous = null)
    {
        var provider = Providers.FirstOrDefault(item => string.Equals(item.BusinessId, result.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider is not null)
        {
            var models = provider.Models.ToList();
            var index = models.FindIndex(item => item.Id == result.Id);
            if (index >= 0) models[index] = result;
            else models.Add(result);
            Providers = Providers.Select(item => item.Id == provider.Id ? item with { Models = models, ModelCount = models.Count } : item).ToArray();
        }

        var key = ModelKey(result.ProviderId, result.ModelId);
        var previousKey = previous is null ? key : ModelKey(previous.ProviderId, previous.ModelId);
        var resolvedProvider = CurrentConfig.Providers.FirstOrDefault(item => string.Equals(item.Id, result.ProviderId, StringComparison.OrdinalIgnoreCase));
        var modelsSnapshot = CurrentConfig.Models.Where(model => !string.Equals(ModelKey(model.ProviderId, model.ModelId), key, StringComparison.OrdinalIgnoreCase) && !string.Equals(ModelKey(model.ProviderId, model.ModelId), previousKey, StringComparison.OrdinalIgnoreCase)).ToList();
        if (result.Enabled && resolvedProvider is not null)
            modelsSnapshot.Add(ToResolvedModel(provider, result, CurrentConfig.Models.FirstOrDefault(model => ModelKey(model.ProviderId, model.ModelId) == key || ModelKey(model.ProviderId, model.ModelId) == previousKey)));
        CurrentConfig = WithConfig(CurrentConfig, models: modelsSnapshot, combos: ReplaceRouteModels(CurrentConfig.GatewayCombos, modelsSnapshot, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key, previousKey }));
        RebuildEnabledGatewayModels();
    }

    private void ApplyEndpointSnapshot(GatewayEndpointResponse result)
    {
        GatewayEndpoints = GatewayEndpoints.Select(item => string.Equals(item.Key, result.Key, StringComparison.OrdinalIgnoreCase) ? result : item).ToArray();
        var endpoint = new ResolvedGatewayEndpointConfig
        {
            Key = result.Key,
            PublicPath = result.PublicPath,
            Enabled = result.Enabled,
            ApiKey = result.ApiKey ?? string.Empty,
            ReasoningEffort = result.ReasoningEffort,
            ComboBindings = result.Combos.Select(binding => new ResolvedGatewayComboBindingConfig { ComboId = binding.ComboId, Enabled = binding.Enabled, SortOrder = binding.SortOrder }).ToArray()
        };
        var endpoints = CurrentConfig.GatewayEndpoints.Select(item => string.Equals(item.Key, result.Key, StringComparison.OrdinalIgnoreCase) ? endpoint : item).ToArray();
        CurrentConfig = WithConfig(CurrentConfig, endpoints: endpoints);
    }

    private void ApplyComboSnapshot(GatewayComboResponse result)
    {
        GatewayCombos = GatewayCombos.Select(item => item.Id == result.Id ? result : item).ToArray();
        var combo = new ResolvedGatewayComboConfig
        {
            Id = result.Id,
            Name = result.Name,
            Enabled = result.Enabled,
            SortOrder = result.SortOrder,
            Routes = result.Routes.Select(route => ToResolvedRoute(route)).Where(route => route is not null).Cast<ResolvedGatewayRouteConfig>().ToArray()
        };
        CurrentConfig = WithConfig(CurrentConfig, combos: CurrentConfig.GatewayCombos.Select(item => item.Id == result.Id ? combo : item).ToArray());
    }

    private void ApplyRouteSnapshot(GatewayRouteResponse result)
    {
        var combo = GatewayCombos.FirstOrDefault(item => item.Id == result.ComboId);
        if (combo is not null)
        {
            var routes = combo.Routes.Select(route => route.Id == result.Id ? result : route).ToList();
            if (!routes.Any(route => route.Id == result.Id)) routes.Add(result);
            var updatedCombo = combo with { Routes = routes };
            GatewayCombos = GatewayCombos.Select(item => item.Id == result.ComboId ? updatedCombo : item).ToArray();
        }
        var resolvedRoute = ToResolvedRoute(result);
        if (resolvedRoute is not null)
            CurrentConfig = WithConfig(CurrentConfig, combos: CurrentConfig.GatewayCombos.Select(combo =>
            {
                if (combo.Id != result.ComboId) return combo;
                var routes = combo.Routes.ToArray();
                var index = Array.FindIndex(routes, route => string.Equals(route.Model.DisplayName, result.ModelName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) routes[index] = resolvedRoute;
                else routes = [.. routes, resolvedRoute];
                return new ResolvedGatewayComboConfig { Id = combo.Id, Name = combo.Name, Enabled = combo.Enabled, SortOrder = combo.SortOrder, Routes = routes };
            }).ToArray());
    }

    private void RebuildEnabledGatewayModels() => EnabledGatewayModels = Providers.Where(provider => provider.Enabled).SelectMany(provider => provider.Models.Where(model => model.Enabled).Select(model => new GatewayModelSourceResponse(model.Id, model.DisplayName, provider.DisplayName))).ToArray();

    private static ResolvedAppConfig WithConfig(ResolvedAppConfig source, ResolvedAppSettings? settings = null, IReadOnlyList<ResolvedProviderConfig>? providers = null, IReadOnlyList<ResolvedModelConfig>? models = null, IReadOnlyList<ResolvedGatewayComboConfig>? combos = null, IReadOnlyList<ResolvedGatewayEndpointConfig>? endpoints = null) => new()
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

    private static ResolvedModelConfig ToResolvedModel(ProviderResponse? provider, ModelResponse model, ResolvedModelConfig? previous)
    {
        var providerId = provider?.BusinessId ?? model.ProviderId;
        var baseUrl = provider?.BaseUrl ?? previous?.BaseUrl ?? string.Empty;
        var apiMode = provider?.ApiMode ?? (previous is not null ? string.Join(';', previous.ApiModes) : "openai");
        var headers = provider is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(provider.HeadersJson) ?? new(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(model.HeadersJson) ?? []) headers[pair.Key] = pair.Value;
        var effectiveApiMode = string.IsNullOrWhiteSpace(model.ApiMode) ? apiMode : model.ApiMode;
        var effectiveBaseUrl = string.IsNullOrWhiteSpace(model.BaseUrl) ? baseUrl : model.BaseUrl;
        return new ResolvedModelConfig
        {
            ModelId = model.ModelId,
            AnthropicModel = model.ModelId,
            OllamaModelName = string.IsNullOrWhiteSpace(model.ConfigId) ? model.DisplayName : $"{model.DisplayName}::{model.ConfigId}",
            DisplayName = model.DisplayName,
            ProviderId = providerId,
            ApiModes = DatabaseConfigurationProvider.SplitApiModes(effectiveApiMode),
            EndpointFormat = provider?.EndpointFormat ?? previous?.EndpointFormat ?? "responses",
            BaseUrl = effectiveBaseUrl.TrimEnd('/'),
            ApiKey = previous?.ApiKey ?? provider?.ApiKey ?? string.Empty,
            UseProxy = provider?.UseProxy ?? previous?.UseProxy ?? false,
            Family = model.Family,
            ContextLength = model.ContextLength,
            MaxTokens = model.MaxTokens,
            Vision = model.Vision,
            Temperature = model.Temperature,
            TopP = model.TopP,
            Headers = headers,
            Extra = JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(model.ExtraJson) ?? new(StringComparer.OrdinalIgnoreCase)
        };
    }

    private ResolvedGatewayRouteConfig? ToResolvedRoute(GatewayRouteResponse route)
    {
        var model = CurrentConfig.Models.FirstOrDefault(item => string.Equals(item.DisplayName, route.ModelName, StringComparison.OrdinalIgnoreCase));
        return model is null ? null : new ResolvedGatewayRouteConfig { Model = model, Enabled = route.Enabled, SortOrder = route.SortOrder };
    }
    private static string ModelKey(string providerId, string modelId) => $"{providerId}\u001f{modelId}";

    private static ConfigurationChangeFields GetSettingsFields(AppSettingsResponse? before, AppSettingsResponse after)
    {
        if (before is null) return ConfigurationChangeFields.SettingsLanguage | ConfigurationChangeFields.SettingsTheme | ConfigurationChangeFields.SettingsProxy | ConfigurationChangeFields.SettingsUpdates | ConfigurationChangeFields.SettingsDiagnostics | ConfigurationChangeFields.SettingsLogging | ConfigurationChangeFields.SettingsAppearance;
        var fields = ConfigurationChangeFields.None;
        if (!string.Equals(before.Language, after.Language, StringComparison.OrdinalIgnoreCase)) fields |= ConfigurationChangeFields.SettingsLanguage;
        if (!string.Equals(before.Theme, after.Theme, StringComparison.OrdinalIgnoreCase)) fields |= ConfigurationChangeFields.SettingsTheme;
        if (before.ProxyMode != after.ProxyMode || before.ProxyHost != after.ProxyHost || before.ProxyPort != after.ProxyPort || before.ProxyUsername != after.ProxyUsername || before.HasProxyPassword != after.HasProxyPassword) fields |= ConfigurationChangeFields.SettingsProxy;
        if (before.AutoCheckUpdates != after.AutoCheckUpdates || before.UpdateChannel != after.UpdateChannel || before.UseProxyForUpdates != after.UseProxyForUpdates) fields |= ConfigurationChangeFields.SettingsUpdates;
        if (before.DiagnosticsEnabled != after.DiagnosticsEnabled) fields |= ConfigurationChangeFields.SettingsDiagnostics;
        if (before.LogRetentionDays != after.LogRetentionDays || before.LogStackTrace != after.LogStackTrace) fields |= ConfigurationChangeFields.SettingsLogging;
        if (before.TransparencyEnabled != after.TransparencyEnabled || before.TransparencyOpacity != after.TransparencyOpacity || before.BlurAmount != after.BlurAmount || !string.Equals(before.TransparencyAlgorithm, after.TransparencyAlgorithm, StringComparison.OrdinalIgnoreCase)) fields |= ConfigurationChangeFields.SettingsAppearance;
        return fields;
    }

    private static ConfigurationChangeFields GetProviderFields(ProviderResponse? before, ProviderResponse after)
    {
        if (before is null) return ConfigurationChangeFields.ProviderIdentity | ConfigurationChangeFields.ProviderConnection | ConfigurationChangeFields.ProviderAvailability | ConfigurationChangeFields.ProviderCredentials | ConfigurationChangeFields.ProviderHeaders;
        var fields = ConfigurationChangeFields.None;
        if (!string.Equals(before.BusinessId, after.BusinessId, StringComparison.OrdinalIgnoreCase) || !string.Equals(before.DisplayName, after.DisplayName, StringComparison.Ordinal)) fields |= ConfigurationChangeFields.ProviderIdentity;
        if (!string.Equals(before.BaseUrl, after.BaseUrl, StringComparison.OrdinalIgnoreCase) || !string.Equals(before.ApiMode, after.ApiMode, StringComparison.OrdinalIgnoreCase) || !string.Equals(before.EndpointFormat, after.EndpointFormat, StringComparison.OrdinalIgnoreCase) || before.UseProxy != after.UseProxy) fields |= ConfigurationChangeFields.ProviderConnection;
        if (before.Enabled != after.Enabled) fields |= ConfigurationChangeFields.ProviderAvailability;
        if (before.HasApiKey != after.HasApiKey) fields |= ConfigurationChangeFields.ProviderCredentials;
        if (!string.Equals(before.HeadersJson, after.HeadersJson, StringComparison.Ordinal)) fields |= ConfigurationChangeFields.ProviderHeaders;
        return fields;
    }

    private static ConfigurationChangeFields GetModelFields(ModelResponse? before, ModelResponse after)
    {
        if (before is null) return ConfigurationChangeFields.ModelIdentity | ConfigurationChangeFields.ModelParameters | ConfigurationChangeFields.ModelAvailability | ConfigurationChangeFields.ModelCredentials | ConfigurationChangeFields.ModelHeaders | ConfigurationChangeFields.ModelMetadata;
        var fields = ConfigurationChangeFields.None;
        if (!string.Equals(before.ModelId, after.ModelId, StringComparison.OrdinalIgnoreCase) || !string.Equals(before.DisplayName, after.DisplayName, StringComparison.Ordinal) || !string.Equals(before.ConfigId, after.ConfigId, StringComparison.Ordinal)) fields |= ConfigurationChangeFields.ModelIdentity;
        if (before.ContextLength != after.ContextLength || before.MaxTokens != after.MaxTokens || before.Vision != after.Vision || before.Temperature != after.Temperature || before.TopP != after.TopP || !string.Equals(before.BaseUrl, after.BaseUrl, StringComparison.OrdinalIgnoreCase) || !string.Equals(before.ApiMode, after.ApiMode, StringComparison.OrdinalIgnoreCase)) fields |= ConfigurationChangeFields.ModelParameters;
        if (before.Enabled != after.Enabled) fields |= ConfigurationChangeFields.ModelAvailability;
        if (before.HasApiKey != after.HasApiKey) fields |= ConfigurationChangeFields.ModelCredentials;
        if (!string.Equals(before.HeadersJson, after.HeadersJson, StringComparison.Ordinal) || !string.Equals(before.ExtraJson, after.ExtraJson, StringComparison.Ordinal)) fields |= ConfigurationChangeFields.ModelHeaders;
        if (!string.Equals(before.Family, after.Family, StringComparison.OrdinalIgnoreCase) || !string.Equals(before.OwnedBy, after.OwnedBy, StringComparison.Ordinal) || !string.Equals(before.RemoteFamily, after.RemoteFamily, StringComparison.Ordinal) || before.RemoteContextLength != after.RemoteContextLength || before.RemoteMaxTokens != after.RemoteMaxTokens || before.RemoteVision != after.RemoteVision) fields |= ConfigurationChangeFields.ModelMetadata;
        return fields;
    }

    private static ConfigurationChangeFields GetComboFields(GatewayComboResponse? before, GatewayComboResponse after)
    {
        if (before is null) return ConfigurationChangeFields.ComboIdentity | ConfigurationChangeFields.ComboAvailability | ConfigurationChangeFields.ComboOrder;
        var fields = ConfigurationChangeFields.None;
        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal)) fields |= ConfigurationChangeFields.ComboIdentity;
        if (before.Enabled != after.Enabled) fields |= ConfigurationChangeFields.ComboAvailability;
        if (before.SortOrder != after.SortOrder) fields |= ConfigurationChangeFields.ComboOrder;
        return fields;
    }

    private static ConfigurationChangeFields GetRouteFields(GatewayRouteResponse? before, GatewayRouteResponse after)
    {
        if (before is null) return ConfigurationChangeFields.RouteModel | ConfigurationChangeFields.RouteAvailability | ConfigurationChangeFields.RouteOrder;
        var fields = ConfigurationChangeFields.None;
        if (before.ModelId != after.ModelId) fields |= ConfigurationChangeFields.RouteModel;
        if (before.Enabled != after.Enabled) fields |= ConfigurationChangeFields.RouteAvailability;
        if (before.SortOrder != after.SortOrder) fields |= ConfigurationChangeFields.RouteOrder;
        return fields;
    }

    private void OnActivityEnqueued(object? sender, ActivityEventInput input) => _ = HandleActivityEnqueuedAsync(input);

    internal async Task HandleActivityEnqueuedAsync(ActivityEventInput input)
    {
        await stateLock.WaitAsync();
        try
        {
            var record = new ActivityEventRecord(0, input.CreatedAt, input.RequestId, input.Method, input.IncomingPath, input.Protocol, input.Route, input.ProviderId, input.ModelId, input.StatusCode, input.ElapsedMs, input.ResponseBytes, input.IsStreaming, input.ErrorType);
            if (activityHistoryMode)
            {
                if (!ContainsActivity(record, pendingActivities)) pendingActivities.Add(record);
                pendingActivityCount = pendingActivities.Count;
            }
            else
            {
                if (activityQuery is null || !MatchesActivityQuery(record, activityQuery)) return;
                if (ContainsActivity(record, activityWindow)) return;
                activityWindow.Insert(0, record);
                while (activityWindow.Count > ActivityWindowLimit) activityWindow.RemoveAt(activityWindow.Count - 1);
            }
            ActivityWindowChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { stateLock.Release(); }
    }

    private void ReplaceActivityWindow(ActivityPage page)
    {
        activityWindow.Clear();
        foreach (var item in page.Items.Take(ActivityWindowLimit)) activityWindow.Add(item);
        activityCursor = page.NextCursor;
        activityHasMore = page.HasMore;
    }

    private void AppendOlder(ActivityPage page)
    {
        foreach (var item in page.Items)
            if (!ContainsActivity(item, activityWindow)) activityWindow.Add(item);
        while (activityWindow.Count > ActivityWindowLimit) activityWindow.RemoveAt(0);
        activityCursor = page.NextCursor;
        activityHasMore = page.HasMore;
    }

    private ActivityPage BuildActivityPage() => new(activityWindow.ToArray(), activityCursor, activityHasMore);

    private static ActivityQuery NormalizeActivityQuery(ActivityQuery query) => query with
    {
        SearchText = string.IsNullOrWhiteSpace(query.SearchText) ? null : query.SearchText.Trim(),
        Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim(),
        Protocol = string.IsNullOrWhiteSpace(query.Protocol) ? null : query.Protocol.Trim(),
        Limit = ActivityWindowLimit
    };

    private static bool MatchesActivityQuery(ActivityEventRecord item, ActivityQuery query)
    {
        if (query.Status is "ok" && (item.StatusCode < 200 || item.StatusCode >= 300)) return false;
        if (query.Status is "fail" && item.StatusCode < 500) return false;
        if (query.Status is "warn" && (item.StatusCode < 400 || item.StatusCode >= 500)) return false;
        if (!string.IsNullOrWhiteSpace(query.Protocol) && !string.Equals(item.Protocol, query.Protocol, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(query.SearchText)) return true;
        var search = query.SearchText.Trim();
        return item.RequestId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (item.ProviderId ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
            || (item.ModelId ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Route.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsActivity(ActivityEventRecord candidate, IEnumerable<ActivityEventRecord> existing) =>
        existing.Any(item => candidate.Id > 0 && item.Id > 0
            ? candidate.Id == item.Id
            : candidate.CreatedAt == item.CreatedAt && string.Equals(candidate.RequestId, item.RequestId, StringComparison.Ordinal));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gatewayService.ActivityEnqueued -= OnActivityEnqueued;
        stateLock.Dispose();
    }
}
