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
    public event EventHandler? ConfigurationChanged;
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
        await ReloadCoreAsync(cancellationToken, isInitialLoad: false);

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
        var result = await configService.UpdateSettingsAsync(input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<ProviderResponse> CreateProviderAsync(ProviderInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateProviderAsync(input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<ProviderResponse> UpdateProviderAsync(Guid id, ProviderInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateProviderAsync(id, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteProviderAsync(id, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task<ModelResponse> CreateModelAsync(Guid providerId, ModelInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateModelAsync(providerId, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<ModelResponse> UpdateModelAsync(Guid id, ModelInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateModelAsync(id, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task DeleteModelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteModelAsync(id, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task<GatewayEndpointResponse> SetGatewayEndpointEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        var result = await configService.SetGatewayEndpointEnabledAsync(key, enabled, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<GatewayComboResponse> CreateGatewayComboAsync(string endpointKey, GatewayComboInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateGatewayComboAsync(endpointKey, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<GatewayComboResponse> UpdateGatewayComboAsync(Guid id, GatewayComboInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateGatewayComboAsync(id, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task DeleteGatewayComboAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteGatewayComboAsync(id, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task<GatewayRouteResponse> CreateGatewayRouteAsync(Guid comboId, GatewayRouteInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.CreateGatewayRouteAsync(comboId, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task<GatewayRouteResponse> UpdateGatewayRouteAsync(Guid id, GatewayRouteInput input, CancellationToken cancellationToken = default)
    {
        var result = await configService.UpdateGatewayRouteAsync(id, input, cancellationToken);
        await RefreshAsync(cancellationToken);
        return result;
    }

    public async Task DeleteGatewayRouteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await configService.DeleteGatewayRouteAsync(id, cancellationToken);
        await RefreshAsync(cancellationToken);
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
            await ReloadCoreAsync(cancellationToken, isInitialLoad: true);
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

    private async Task ReloadCoreAsync(CancellationToken cancellationToken, bool isInitialLoad)
    {
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            isLoading = true;
            var config = await configService.LoadAsync(cancellationToken);
            var providers = await configService.ListProvidersAsync(cancellationToken);
            var settings = await configService.GetSettingsAsync(cancellationToken);
            var endpoints = await configService.ListGatewayEndpointsAsync(cancellationToken);
            var enabledModels = await configService.ListEnabledGatewayModelsAsync(cancellationToken);
            CurrentConfig = config;
            Providers = providers;
            Settings = settings;
            GatewayEndpoints = endpoints;
            EnabledGatewayModels = enabledModels;
            logger.LogInformation("桌面数据中心配置快照完成 {ProviderCount} {ModelCount} {EndpointCount}", providers.Count, config.Models.Count, endpoints.Count);
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
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
