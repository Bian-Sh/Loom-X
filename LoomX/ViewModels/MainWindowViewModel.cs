using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using LoomX;
using LoomX.Configuration;
using LoomX.Activity;
using LoomX.NodeGraph;
using LoomX.Services;

namespace LoomX.ViewModels;

public sealed class MainWindowViewModel : NotifyViewModel
{
    private readonly GatewayProcessService gatewayService;
    private readonly AppDataStore dataStore;
    private readonly ToastService toastService;
    private readonly ILoggerFactory loggerFactory;
    private readonly ConsoleViewModel consoleViewModel;
    private readonly SettingsViewModel settingsViewModel;
    private readonly OverviewViewModel overviewViewModel;
    private readonly ProvidersViewModel providersViewModel;
    private readonly GatewayViewModel gatewayViewModel;
    private readonly ActivityViewModel activityViewModel;
    private readonly UpdateCoordinator updateCoordinator;
    private readonly Action<bool, int, int, string>? applyAppearance;
    private object currentView = new PlaceholderViewModel("加载中", "正在加载 Loom-X。");
    private string pageTitle = "概览";
    private string pageDescription = "确认本地服务健康，快速查看网关与模型配置。";

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public object CurrentView { get => currentView; private set => SetProperty(ref currentView, value); }
    public string PageTitle { get => pageTitle; private set => SetProperty(ref pageTitle, value); }
    public string PageDescription { get => pageDescription; private set => SetProperty(ref pageDescription, value); }
    public UpdateCoordinator Update => updateCoordinator;

    public MainWindowViewModel(GatewayProcessService gatewayService, ToastService? toastService = null, ILoggerFactory? loggerFactory = null, ConfigSnapshotService? configService = null, Action<bool, int, int, string>? applyAppearance = null, AppDataStore? dataStore = null)
    {
        this.gatewayService = gatewayService;
        this.toastService = toastService ?? new ToastService();
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var ownedConfigService = configService ?? new ConfigSnapshotService(this.loggerFactory.CreateLogger<ConfigSnapshotService>());
        this.dataStore = dataStore ?? new AppDataStore(ownedConfigService, gatewayService, this.loggerFactory.CreateLogger<AppDataStore>());
        this.applyAppearance = applyAppearance;
        consoleViewModel = new ConsoleViewModel(toastService: this.toastService);
        overviewViewModel = new OverviewViewModel(gatewayService, this.dataStore, this.loggerFactory.CreateLogger<MainWindowViewModel>());
        providersViewModel = new ProvidersViewModel(this.dataStore, this.toastService, this.loggerFactory.CreateLogger<ProvidersViewModel>());
        gatewayViewModel = new GatewayViewModel(this.dataStore, this.toastService);
        activityViewModel = new ActivityViewModel(this.dataStore, this.loggerFactory.CreateLogger<ActivityViewModel>());
        updateCoordinator = new UpdateCoordinator(this.dataStore, logger: this.loggerFactory.CreateLogger<UpdateCoordinator>());
        settingsViewModel = new SettingsViewModel(dataStore: this.dataStore, logger: this.loggerFactory.CreateLogger<SettingsViewModel>(), toastService: this.toastService, applyAppearance: this.applyAppearance, updateCoordinator: updateCoordinator);
        NavigationItems = new([
            new("概览", "M 4,18 L 12,10 L 20,18 L 20,30 L 4,30 Z M 9,30 L 9,20 L 15,20 L 15,30", () => ShowOverview()),
            new("网关", "M 16,4 L 16,9 M 16,9 L 8,16 M 16,9 L 24,16 M 8,16 L 8,25 M 24,16 L 24,25 M 4,25 L 12,25 M 20,25 L 28,25", () => ShowGateway()),
            new("Provider", "M 7,8 L 25,8 M 7,16 L 25,16 M 7,24 L 25,24 M 4,8 L 4,8 M 4,16 L 4,16 M 4,24 L 4,24", () => ShowProviders()),
            new("活动", "M 7,28 L 7,5 M 8,6 C 13,4 18,8 25,6 L 25,18 C 18,20 13,16 8,18", () => ShowActivity()),
            new("控制台", "M 5,6 L 27,6 L 27,26 L 5,26 Z M 9,12 L 13,16 L 9,20 M 16,20 L 23,20", () => ShowConsole()),
            new("设置", "M 16,4 L 18,7 L 22,8 L 25,6 L 28,9 L 26,12 L 27,16 L 30,18 L 28,22 L 24,21 L 21,24 L 21,28 L 16,29 L 14,25 L 10,24 L 7,26 L 4,22 L 6,19 L 5,15 L 2,13 L 4,8 L 8,9 L 11,6 L 11,3 Z M 16,12 A 4,4 0 1,0 16,20 A 4,4 0 1,0 16,12 Z", () => ShowSettings())
        ]);
        this.dataStore.ConfigurationReady += OnConfigurationReady;
        this.dataStore.ConfigurationChanged += OnConfigurationChanged;
        _ = InitializeDataStoreAsync();
    }

    private void SetActive(string title)
    {
        foreach (var item in NavigationItems) item.IsActive = item.Title == title;
    }

    private void ShowOverview() { SetActive("概览"); PageTitle = "概览"; PageDescription = "确认本地服务健康，快速查看网关与模型配置。"; CurrentView = overviewViewModel; }
    private void ShowProviders() { SetActive("Provider"); PageTitle = "Provider"; PageDescription = "管理上游连接、请求协议、密钥与可用模型。"; CurrentView = providersViewModel; }
    private void ShowGateway() { SetActive("网关"); PageTitle = "网关"; PageDescription = "组合对外 Endpoint 的模型路由，并按优先级自动故障转移。"; CurrentView = gatewayViewModel; }
    private void ShowConsole() { SetActive("控制台"); PageTitle = "控制台"; PageDescription = "查看本地网关、协议转换与上游请求的脱敏运行日志。"; CurrentView = consoleViewModel; }
    private void ShowActivity() { SetActive("活动"); PageTitle = "请求活动"; PageDescription = "定位协议转换、上游延迟与 HTTP 错误，保留可追溯的脱敏上下文。"; CurrentView = activityViewModel; }
    private void ShowSettings() { SetActive("设置"); PageTitle = "设置"; PageDescription = "调整 Loom-X 的显示、连接、更新与隐私偏好。"; CurrentView = settingsViewModel; }
    private void ShowPlaceholder(string title, string description) { SetActive(title); PageTitle = title; PageDescription = description; CurrentView = new PlaceholderViewModel(title, description); }

    private void OnConfigurationReady(object? sender, EventArgs args)
    {
        void Apply()
        {
            ShowOverview();
            updateCoordinator.Start();
            if (dataStore.Settings is { } settings)
                applyAppearance?.Invoke(settings.TransparencyEnabled, settings.TransparencyOpacity, settings.BlurAmount, settings.TransparencyAlgorithm);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    private async Task InitializeDataStoreAsync()
    {
        try { await dataStore.InitializeAsync(); }
        catch (Exception exception)
        {
            var description = $"数据中心加载失败：{exception.Message}，请通过页面刷新重试。";
            if (Dispatcher.UIThread.CheckAccess()) ShowPlaceholder("加载失败", description);
            else Dispatcher.UIThread.Post(() => ShowPlaceholder("加载失败", description));
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        void Apply()
        {
            if (dataStore.Settings is { } settings)
                applyAppearance?.Invoke(settings.TransparencyEnabled, settings.TransparencyOpacity, settings.BlurAmount, settings.TransparencyAlgorithm);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    public void Dispose()
    {
        dataStore.ConfigurationReady -= OnConfigurationReady;
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        overviewViewModel.Dispose();
        providersViewModel.Dispose();
        gatewayViewModel.Dispose();
        activityViewModel.Dispose();
        settingsViewModel.Dispose();
        updateCoordinator.Dispose();
        consoleViewModel.Dispose();
        dataStore.Dispose();
    }
}

public sealed class NavigationItemViewModel : NotifyViewModel
{
    public string Title { get; }
    public string Icon { get; }
    public Geometry IconData { get; }
    private bool isActive;
    public bool IsActive { get => isActive; set => SetProperty(ref isActive, value); }
    public ICommand NavigateCommand { get; }
    public NavigationItemViewModel(string title, string icon, Action action) { Title = title; Icon = icon; IconData = Geometry.Parse(icon); NavigateCommand = new DelegateCommand(action); }
}

public sealed class OverviewViewModel : NotifyViewModel, IDisposable
{
    // ActivityQueryService 由 AppDataStore 统一持有，概览只读取数据中心提供的最近活动结果。
    private readonly GatewayProcessService gatewayService;
    private readonly AppDataStore dataStore;
    private readonly ILogger<MainWindowViewModel>? logger;
    private string gatewayStatus = "未运行";
    private string endpoint = "未配置";
    private string version = "未知";
    private string lastChecked = "尚未检查";
    private int providerCount;
    private int modelCount;
    private int activeRequestCount;
    private int throughput;
    private string p95Latency = "—";
    private string graphStatus = "等待网关启动";
    private string gatewayActionLabel = "启动网关";
    private RuntimeGraphSnapshot? graphSnapshot;
    private OverviewEndpointViewModel? selectedEndpoint;
    private bool gatewayToggleInProgress;
    private bool refreshInProgress;
    private string topologyJson = "{\"endpoints\":[],\"combos\":[],\"providers\":[],\"models\":[],\"edges\":[]}";
    private readonly Dictionary<string, RequestTelemetryEvent> activeRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> activeEdgeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> requestEdges = new(StringComparer.Ordinal);
    private readonly List<RequestTelemetryEvent> completionWindow = [];

    public event EventHandler? TopologyChanged;
    public event EventHandler? GraphMetricsChanged;
    public event EventHandler<RequestTelemetryEvent>? GraphTelemetryPublished;

    public string GatewayStatus { get => gatewayStatus; private set => SetProperty(ref gatewayStatus, value); }
    public string Endpoint { get => endpoint; private set => SetProperty(ref endpoint, value); }
    public string Version { get => version; private set => SetProperty(ref version, value); }
    public string LastChecked { get => lastChecked; private set => SetProperty(ref lastChecked, value); }
    public int ProviderCount { get => providerCount; private set => SetProperty(ref providerCount, value); }
    public int ModelCount { get => modelCount; private set => SetProperty(ref modelCount, value); }
    public int ActiveRequestCount { get => activeRequestCount; private set => SetProperty(ref activeRequestCount, value); }
    public int Throughput { get => throughput; private set => SetProperty(ref throughput, value); }
    public string P95Latency { get => p95Latency; private set => SetProperty(ref p95Latency, value); }
    public string GraphStatus { get => graphStatus; private set => SetProperty(ref graphStatus, value); }
    public string GatewayActionLabel { get => gatewayActionLabel; private set => SetProperty(ref gatewayActionLabel, value); }
    public RuntimeGraphSnapshot? GraphSnapshot { get => graphSnapshot; private set => SetProperty(ref graphSnapshot, value); }
    public OverviewEndpointViewModel? SelectedEndpoint { get => selectedEndpoint; private set => SetProperty(ref selectedEndpoint, value); }
    public ObservableCollection<OverviewEndpointViewModel> Endpoints { get; } = [];
    public ObservableCollection<OverviewComboViewModel> Combos { get; } = [];
    public ObservableCollection<OverviewProviderViewModel> Providers { get; } = [];
    public ObservableCollection<OverviewModelViewModel> Models { get; } = [];
    public ObservableCollection<OverviewRecentRequestViewModel> RecentRequests { get; } = [];
    public bool RecentRequestsEmpty => RecentRequests.Count == 0;
    public string TopologyJson => topologyJson;
    public string MetricsJson => JsonSerializer.Serialize(new
    {
        activeRequests = ActiveRequestCount,
        throughput = Throughput,
        p95Latency = P95Latency
    });
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleGatewayCommand { get; }
    public ICommand RefreshCommand { get; }

    public OverviewViewModel(GatewayProcessService gatewayService, AppDataStore dataStore, ILogger<MainWindowViewModel>? logger = null)
    {
        this.gatewayService = gatewayService;
        this.dataStore = dataStore;
        this.logger = logger;
        StartCommand = new AsyncCommand(StartAsync);
        StopCommand = new AsyncCommand(StopAsync);
        ToggleGatewayCommand = new AsyncCommand(ToggleGatewayAsync, CanToggleGateway);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        gatewayService.StateChanged += OnGatewayStateChanged;
        gatewayService.TelemetryPublished += OnTelemetryPublished;
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        _ = RefreshAsync();
    }

    public OverviewViewModel(GatewayProcessService gatewayService, ConfigSnapshotService configService, ILogger<MainWindowViewModel>? logger = null)
        : this(gatewayService, new AppDataStore(configService, gatewayService), logger) { }

    private async Task StartAsync()
    {
        var endpoint = LoadEndpoint();
        await gatewayService.StartAsync(endpoint);
        await RefreshAsync();
    }

    private async Task StopAsync()
    {
        await gatewayService.StopAsync();
        await RefreshAsync();
    }

    private async Task ToggleGatewayAsync()
    {
        if (!CanToggleGateway()) return;
        gatewayToggleInProgress = true;
        UpdateGatewayControls();
        try
        {
            if (gatewayService.State == GatewayState.Running)
            {
                await gatewayService.StopAsync();
                logger?.LogInformation("概览网关切换完成，操作 {Action}", "停止");
            }
            else
            {
                await gatewayService.StartAsync(LoadEndpoint());
                logger?.LogInformation("概览网关切换完成，操作 {Action}", "启动");
            }
        }
        finally
        {
            gatewayToggleInProgress = false;
            await RefreshAsync();
        }
    }

    private bool CanToggleGateway() => !gatewayToggleInProgress && gatewayService.State is not (GatewayState.Starting or GatewayState.Stopping);

    private void UpdateGatewayControls()
    {
        GatewayActionLabel = gatewayToggleInProgress
            ? gatewayService.State == GatewayState.Running ? "停止中" : "启动中"
            : gatewayService.State == GatewayState.Running ? "停止网关" : gatewayService.State switch
            {
                GatewayState.Starting => "启动中",
                GatewayState.Stopping => "停止中",
                _ => "启动网关"
            };
        (ToggleGatewayCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private async Task RefreshAsync()
    {
        if (refreshInProgress) return;
        refreshInProgress = true;
        try
        {
            await dataStore.RefreshAsync();
            var config = dataStore.CurrentConfig;
            Endpoint = config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
            ProviderCount = config.Providers.Count;
            ModelCount = config.Models.Count;
            BuildTopology(config);
            await RefreshRecentRequestsAsync();
            GatewayStatus = gatewayService.State switch
            {
                GatewayState.Running => "运行中",
                GatewayState.Starting => "启动中",
                GatewayState.Stopping => "停止中",
                GatewayState.Failed => $"异常：{gatewayService.Error}",
                _ => "未运行"
            };
            LastChecked = gatewayService.LastCheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未检查";
            Version = gatewayService.State == GatewayState.Running ? "Loom-X API 在线" : "未连接";
            GraphStatus = gatewayService.State == GatewayState.Running ? "实时拓扑已连接" : "等待网关启动";
            UpdateGatewayControls();
            GraphMetricsChanged?.Invoke(this, EventArgs.Empty);
            logger?.LogInformation("概览刷新完成 {ProviderCount} 个 Provider、{ModelCount} 个模型、{EndpointCount} 个 Endpoint、{RouteCount} 条路由，网关状态 {GatewayState}，配置库 {DatabasePath}，进程 {ProcessId}", ProviderCount, ModelCount, Endpoints.Count, Endpoints.Sum(item => item.Routes.Count), gatewayService.State, AppDataPaths.DatabasePath, Environment.ProcessId);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "概览刷新失败");
            GraphStatus = "概览加载失败";
        }
        finally
        {
            refreshInProgress = false;
        }
    }

    private async Task RefreshRecentRequestsAsync()
    {
        try
        {
            var records = await dataStore.QueryRecentActivitiesAsync(new ActivityQuery(Limit: 8));
            await Dispatcher.UIThread.InvokeAsync(() => MergeRecentRequests(records.Items.Take(8).Select(OverviewRecentRequestViewModel.From)));
            logger?.LogInformation("概览最近请求回填完成 {RequestCount} 条", RecentRequests.Count);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "概览最近请求回填失败");
        }
    }

    private void MergeRecentRequests(IEnumerable<OverviewRecentRequestViewModel> persistedRequests)
    {
        var merged = OverviewRecentRequestViewModel.Merge(persistedRequests, RecentRequests);
        RecentRequests.Clear();
        foreach (var item in merged) RecentRequests.Add(item);
        OnPropertyChanged(nameof(RecentRequestsEmpty));
    }

    private void BuildTopology(ResolvedAppConfig config)
    {
        SelectedEndpoint = null;
        Endpoints.Clear();
        Combos.Clear();
        Providers.Clear();
        Models.Clear();
        GraphSnapshot = RuntimeGraphProjection.Create(config, dataStore.Providers);
        foreach (var provider in dataStore.Providers
                     .GroupBy(item => item.BusinessId, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            Providers.Add(new OverviewProviderViewModel(provider.BusinessId, provider.DisplayName, provider.Enabled, provider.ModelCount));
        }
        foreach (var model in config.Models.GroupBy(item => $"{item.ProviderId}:{item.ModelId}", StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
            Models.Add(new OverviewModelViewModel(model.DisplayName, model.ModelId, model.ProviderId));
        foreach (var endpoint in config.GatewayEndpoints.OrderBy(item => item.Key))
        {
            var endpointVm = new OverviewEndpointViewModel(endpoint.Key, EndpointLabel(endpoint.Key), endpoint.PublicPath, endpoint.Enabled);
            endpointVm.GraphSnapshot = GraphSnapshot.ForEndpoint(endpoint.Key);
            foreach (var combo in endpoint.Combos.OrderBy(item => item.SortOrder))
            {
                var comboVm = new OverviewComboViewModel(ComboId(endpoint.Key, combo.Name), endpoint.Key, combo.Name, combo.Enabled);
                Combos.Add(comboVm);
                foreach (var route in combo.Routes.Where(item => item.Enabled))
                {
                    var routeVm = new OverviewRouteViewModel(combo.Name, route.Model.DisplayName, route.Model.ModelId, route.Model.ProviderId)
                    {
                        IsActive = activeEdgeCounts.ContainsKey(EdgeKey(endpoint.Key, route.Model.ProviderId, route.Model.ModelId))
                    };
                    endpointVm.Routes.Add(routeVm);
                }
            }
            Endpoints.Add(endpointVm);
        }
        SelectEndpoint(Endpoints.FirstOrDefault());
        topologyJson = CreateTopologyJson(config, dataStore.Providers);
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectEndpoint(OverviewEndpointViewModel? endpoint)
    {
        if (endpoint is null || !Endpoints.Contains(endpoint)) return;
        SelectedEndpoint = endpoint;
        foreach (var item in Endpoints) item.IsGraphVisible = ReferenceEquals(item, endpoint);
    }

    internal static string CreateTopologyJson(ResolvedAppConfig config, IReadOnlyList<ProviderResponse> providerResponses)
    {
        var providers = providerResponses
            .GroupBy(item => item.BusinessId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item => new { id = item.BusinessId, displayName = item.DisplayName, enabled = item.Enabled, modelCount = item.ModelCount })
            .ToArray();
        var models = config.Models
            .GroupBy(item => $"{item.ProviderId}:{item.ModelId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item => new { displayName = item.DisplayName, modelId = item.ModelId, providerId = item.ProviderId })
            .ToArray();
        var endpoints = config.GatewayEndpoints.OrderBy(item => item.Key)
            .Select(item => new { key = item.Key, displayName = EndpointLabel(item.Key), publicPath = item.PublicPath, enabled = item.Enabled })
            .ToArray();
        var combos = config.GatewayEndpoints.SelectMany(endpoint => endpoint.Combos.OrderBy(combo => combo.SortOrder)
            .Select(combo => new { id = ComboId(endpoint.Key, combo.Name), endpointKey = endpoint.Key, displayName = combo.Name, enabled = combo.Enabled }))
            .ToArray();
        var edges = new List<object>();
        var comboProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providerModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in config.Models)
        {
            var modelKey = $"{model.ProviderId}|{model.ModelId}";
            if (providerModels.Add(modelKey))
                edges.Add(new { type = "provider-model", sourceType = "provider", sourceId = model.ProviderId, targetType = "model", targetId = modelKey, endpointKey = "", comboId = "", providerId = model.ProviderId, modelId = model.ModelId });
        }
        foreach (var endpoint in config.GatewayEndpoints)
        foreach (var combo in endpoint.Combos)
        {
            var comboId = ComboId(endpoint.Key, combo.Name);
            edges.Add(new { type = "endpoint-combo", sourceType = "endpoint", sourceId = endpoint.Key, targetType = "combo", targetId = comboId, endpointKey = endpoint.Key, comboId });
            foreach (var route in combo.Routes)
            {
                var providerKey = $"{comboId}|{route.Model.ProviderId}";
                if (comboProviders.Add(providerKey))
                    edges.Add(new { type = "combo-provider", sourceType = "combo", sourceId = comboId, targetType = "provider", targetId = route.Model.ProviderId, endpointKey = endpoint.Key, comboId, providerId = route.Model.ProviderId, modelId = route.Model.ModelId });
                var modelKey = $"{route.Model.ProviderId}|{route.Model.ModelId}";
                if (providerModels.Add(modelKey))
                    edges.Add(new { type = "provider-model", sourceType = "provider", sourceId = route.Model.ProviderId, targetType = "model", targetId = modelKey, endpointKey = endpoint.Key, comboId, providerId = route.Model.ProviderId, modelId = route.Model.ModelId });
                edges.Add(new { type = "route", sourceType = "endpoint", sourceId = endpoint.Key, targetType = "model", targetId = modelKey, endpointKey = endpoint.Key, comboId, providerId = route.Model.ProviderId, modelId = route.Model.ModelId, alias = combo.Name, enabled = combo.Enabled && route.Enabled });
            }
        }
        return JsonSerializer.Serialize(new { endpoints, combos, providers, models, edges });
    }

    private static string ComboId(string endpointKey, string name) => $"{endpointKey}|{name}";

    private string LoadEndpoint()
    {
        var config = dataStore.CurrentConfig;
        return config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
    }

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        if (refreshInProgress) return;
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => { if (!refreshInProgress) _ = RefreshAsync(); });
    }

    private void OnGatewayStateChanged(object? sender, EventArgs args)
    {
        if (refreshInProgress) return;
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => { if (!refreshInProgress) _ = RefreshAsync(); });
    }

    private void OnTelemetryPublished(object? sender, RequestTelemetryEvent telemetryEvent) => Dispatcher.UIThread.Post(() =>
    {
        ApplyTelemetry(telemetryEvent);
        GraphTelemetryPublished?.Invoke(this, telemetryEvent);
    });

    private void ApplyTelemetry(RequestTelemetryEvent telemetryEvent)
    {
        if (telemetryEvent.Kind == TelemetryEventKind.RequestStarted)
        {
            activeRequests[telemetryEvent.RequestId] = telemetryEvent;
            requestEdges.TryAdd(telemetryEvent.RequestId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ActiveRequestCount = activeRequests.Count;
            GraphMetricsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (telemetryEvent.Kind == TelemetryEventKind.EdgeAttemptStarted)
        {
            var edgeKey = EdgeKey(telemetryEvent.EndpointKey, telemetryEvent.ProviderId, telemetryEvent.ModelId);
            if (requestEdges.TryGetValue(telemetryEvent.RequestId, out var edges) && edges.Add(edgeKey)) SetRouteActive(edgeKey, true);
            return;
        }
        if (telemetryEvent.Kind is TelemetryEventKind.EdgeAttemptCompleted or TelemetryEventKind.EdgeAttemptFailed or TelemetryEventKind.EdgeAttemptCancelled)
        {
            var edgeKey = EdgeKey(telemetryEvent.EndpointKey, telemetryEvent.ProviderId, telemetryEvent.ModelId);
            if (requestEdges.TryGetValue(telemetryEvent.RequestId, out var edges) && edges.Remove(edgeKey)) SetRouteActive(edgeKey, false);
            return;
        }
        if (telemetryEvent.Kind != TelemetryEventKind.RequestCompleted) return;
        activeRequests.Remove(telemetryEvent.RequestId);
        if (requestEdges.Remove(telemetryEvent.RequestId, out var pendingEdges)) foreach (var edgeKey in pendingEdges) SetRouteActive(edgeKey, false);
        ActiveRequestCount = activeRequests.Count;
        completionWindow.Add(telemetryEvent);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        completionWindow.RemoveAll(item => item.Timestamp < cutoff);
        Throughput = completionWindow.Count;
        if (telemetryEvent.StatusCode.HasValue)
        {
            var request = OverviewRecentRequestViewModel.From(telemetryEvent);
            if (!RecentRequests.Any(item => item.RequestId.Equals(request.RequestId, StringComparison.Ordinal))) RecentRequests.Insert(0, request);
            while (RecentRequests.Count > 8) RecentRequests.RemoveAt(RecentRequests.Count - 1);
            OnPropertyChanged(nameof(RecentRequestsEmpty));
        }
        P95Latency = completionWindow.Count == 0 ? "—" : $"{completionWindow.Select(item => item.ElapsedMs).OrderBy(item => item).ElementAt(Math.Max(0, (int)Math.Ceiling(completionWindow.Count * .95) - 1))} ms";
        GraphMetricsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetRouteActive(string edgeKey, bool active)
    {
        if (active) activeEdgeCounts[edgeKey] = activeEdgeCounts.GetValueOrDefault(edgeKey) + 1;
        else if (activeEdgeCounts.TryGetValue(edgeKey, out var count) && count <= 1) activeEdgeCounts.Remove(edgeKey);
        else if (!active) activeEdgeCounts[edgeKey] = count - 1;
        var separator = edgeKey.IndexOf('|');
        if (separator < 0) return;
        var endpointKey = edgeKey[..separator];
        var providerSeparator = edgeKey.IndexOf('|', separator + 1);
        if (providerSeparator < 0) return;
        var providerId = edgeKey[(separator + 1)..providerSeparator];
        var modelId = edgeKey[(providerSeparator + 1)..];
        var route = Endpoints.FirstOrDefault(item => item.Key.Equals(endpointKey, StringComparison.OrdinalIgnoreCase))?.Routes.FirstOrDefault(item => item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase) && item.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (route is not null) route.IsActive = activeEdgeCounts.ContainsKey(edgeKey);
    }

    internal static string EdgeKey(string endpointKey, string? providerId, string? modelId) => OverviewGraphEdgeKey.Create(endpointKey, providerId, modelId);

    public void Dispose()
    {
        gatewayService.StateChanged -= OnGatewayStateChanged;
        gatewayService.TelemetryPublished -= OnTelemetryPublished;
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        activeRequests.Clear();
        requestEdges.Clear();
        activeEdgeCounts.Clear();
    }

    private static string EndpointLabel(string key) => key.ToLowerInvariant() switch { "openai" => "OpenAI", "ollama" => "Ollama", "azure" => "Azure", _ => key };
}

public static class OverviewGraphEdgeKey
{
    public static string Create(string endpointKey, string? providerId, string? modelId) => $"{endpointKey}|{providerId}|{modelId}";
}

public sealed class OverviewEndpointViewModel : NotifyViewModel
{
    private bool isGraphVisible;
    private RuntimeGraphSnapshot? graphSnapshot;
    public string Key { get; }
    public string DisplayName { get; }
    public string PublicPath { get; }
    public bool Enabled { get; }
    public ObservableCollection<OverviewRouteViewModel> Routes { get; } = [];
    public bool IsGraphVisible { get => isGraphVisible; internal set => SetProperty(ref isGraphVisible, value); }
    public RuntimeGraphSnapshot? GraphSnapshot { get => graphSnapshot; internal set => SetProperty(ref graphSnapshot, value); }
    public int ActiveCount => Routes.Count(item => item.IsActive);
    public string Status => !Enabled ? "已停用" : Routes.Count == 0 ? "无路由" : ActiveCount > 0 ? "有请求" : "已就绪";
    public OverviewEndpointViewModel(string key, string displayName, string publicPath, bool enabled) => (Key, DisplayName, PublicPath, Enabled) = (key, displayName, publicPath, enabled);
}

public sealed class OverviewComboViewModel
{
    public string Id { get; }
    public string EndpointKey { get; }
    public string DisplayName { get; }
    public bool Enabled { get; }
    public OverviewComboViewModel(string id, string endpointKey, string displayName, bool enabled) => (Id, EndpointKey, DisplayName, Enabled) = (id, endpointKey, displayName, enabled);
}

public sealed class OverviewProviderViewModel
{
    public string Id { get; }
    public string DisplayName { get; }
    public bool Enabled { get; }
    public int ModelCount { get; }
    public OverviewProviderViewModel(string id, string displayName, bool enabled, int modelCount) => (Id, DisplayName, Enabled, ModelCount) = (id, displayName, enabled, modelCount);
}

public sealed class OverviewRouteViewModel : NotifyViewModel
{
    private bool isActive;
    public string Alias { get; }
    public string ModelName { get; }
    public string ModelId { get; }
    public string ProviderId { get; }
    public bool IsActive { get => isActive; set { if (!SetProperty(ref isActive, value)) return; OnPropertyChanged(nameof(Status)); } }
    public string Status => IsActive ? "活跃" : "待命";
    public OverviewRouteViewModel(string alias, string modelName, string modelId, string providerId) => (Alias, ModelName, ModelId, ProviderId) = (alias, modelName, modelId, providerId);
}

public sealed class OverviewModelViewModel
{
    public string DisplayName { get; }
    public string ModelId { get; }
    public string ProviderId { get; }
    public OverviewModelViewModel(string displayName, string modelId, string providerId) => (DisplayName, ModelId, ProviderId) = (displayName, modelId, providerId);
}

public sealed class OverviewRecentRequestViewModel
{
    public string RequestId { get; }
    public DateTimeOffset CreatedAt { get; }
    public string Time { get; }
    public string Endpoint { get; }
    public string Model { get; }
    public string Status { get; }
    public long ElapsedMs { get; }
    public string Latency => $"{ElapsedMs} ms";
    private OverviewRecentRequestViewModel(string requestId, DateTimeOffset createdAt, string endpoint, string model, int statusCode, long elapsedMs)
    {
        RequestId = requestId;
        CreatedAt = createdAt;
        Time = createdAt.ToLocalTime().ToString("HH:mm:ss");
        Endpoint = endpoint;
        Model = model;
        Status = statusCode is >= 200 and < 300 ? "成功" : "失败";
        ElapsedMs = elapsedMs;
    }

    private OverviewRecentRequestViewModel(RequestTelemetryEvent item)
        : this(item.RequestId, item.Timestamp, item.EndpointKey, item.ModelId ?? item.ModelAlias ?? "未知模型", item.StatusCode ?? 0, item.ElapsedMs) { }

    private OverviewRecentRequestViewModel(ActivityEventRecord item)
        : this(item.RequestId, item.CreatedAt, item.Protocol, string.IsNullOrWhiteSpace(item.ModelId) ? "未知模型" : item.ModelId, item.StatusCode, item.ElapsedMs) { }

    public static OverviewRecentRequestViewModel From(RequestTelemetryEvent item) => new(item);
    public static OverviewRecentRequestViewModel From(ActivityEventRecord item) => new(item);

    internal static IReadOnlyList<OverviewRecentRequestViewModel> Merge(
        IEnumerable<OverviewRecentRequestViewModel> persistedRequests,
        IEnumerable<OverviewRecentRequestViewModel> currentRequests) => persistedRequests
        .Concat(currentRequests)
        .GroupBy(item => item.RequestId, StringComparer.Ordinal)
        .Select(group => group.OrderByDescending(item => item.CreatedAt).First())
        .OrderByDescending(item => item.CreatedAt)
        .Take(8)
        .ToArray();
}

public sealed class ProvidersViewModel : NotifyViewModel, IDisposable
{
    private readonly AppDataStore dataStore;
    private readonly ToastService toastService;
    private readonly ILogger<ProvidersViewModel>? logger;
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private ProviderEditorViewModel? selectedProvider;
    private ModelEditorViewModel? selectedModel;
    private string status = "";
    private string connectionStatus = "尚未测试连接";
    private int totalModelCount;
    private int protectedKeyCount;
    private int healthyProviderCount;
    private int enabledProviderCount;
    private int activeTabIndex;
    private CancellationTokenSource? connectionCancellation;
    private CancellationTokenSource? modelSyncCancellation;
    private DispatcherTimer? modelSyncAnimationTimer;
    private bool isModelSyncing;
    private double syncIconAngle;
    private CancellationTokenSource? providerAutoSaveCancellation;
    private CancellationTokenSource? modelAutoSaveCancellation;
    private ModelEditorViewModel? draggingModel;
    private ModelEditorViewModel? modelDragPlaceholder;
    private ProviderEditorViewModel? modelDragOwnerProvider;
    private int draggingModelOriginIndex = -1;
    private bool suppressConfigurationRefresh;
    private bool suppressSelectionInvariant;
    private readonly object refreshSync = new();
    private bool refreshRequested;
    private Task? refreshTask;
    public ObservableCollection<ProviderEditorViewModel> Providers { get; } = [];
    public IReadOnlyList<string> ProviderTypeOptions { get; } = ["openai", "anthropic", "ollama"];
    public ProviderEditorViewModel? SelectedProvider
    {
        get => selectedProvider;
        set
        {
            if (value is null && !suppressSelectionInvariant && Providers.Count > 0)
                value = Providers[0];
            if (ReferenceEquals(selectedProvider, value)) return;
            DetachProvider(selectedProvider);
            SetProperty(ref selectedProvider, value);
            AttachProvider(selectedProvider);
            SelectedModel = null;
            OnPropertyChanged(nameof(HasSelectedProvider));
            OnPropertyChanged(nameof(HasNoSelectedProvider));
            OnPropertyChanged(nameof(EnabledModelCount));
            OnPropertyChanged(nameof(AllModelsEnabled));
            OnPropertyChanged(nameof(EnabledModelSummary));
        }
    }
    public bool HasSelectedProvider => SelectedProvider is not null;
    public bool HasNoSelectedProvider => Providers.Count == 0;

    public ModelEditorViewModel? SelectedModel
    {
        get => selectedModel;
        set
        {
            if (ReferenceEquals(selectedModel, value)) return;
            DetachModel(selectedModel);
            SetProperty(ref selectedModel, value);
            AttachModel(selectedModel);
            OnPropertyChanged(nameof(HasSelectedModel));
        }
    }
    public bool HasSelectedModel => SelectedModel is not null;
    public ModelEditorViewModel? DraggingModel { get => draggingModel; private set { if (ReferenceEquals(draggingModel, value)) return; draggingModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsModelDragActive)); } }
    public bool IsModelDragActive => DraggingModel is not null;
    public int EnabledModelCount => SelectedProvider?.Models.Count(model => model.IsRealModel && model.Enabled) ?? 0;
    public string EnabledModelSummary => $"已启用 {EnabledModelCount} / {SelectedProvider?.Models.Count(model => model.IsRealModel) ?? 0} 个模型";
    public bool AllModelsEnabled
    {
        get
        {
            var models = SelectedProvider?.Models.Where(model => model.IsRealModel).ToArray() ?? [];
            return models.Length > 0 && models.All(model => model.Enabled);
        }
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public bool IsModelSyncing { get => isModelSyncing; private set => SetProperty(ref isModelSyncing, value); }
    public double SyncIconAngle { get => syncIconAngle; private set => SetProperty(ref syncIconAngle, value); }
    public string ConnectionStatus { get => connectionStatus; private set => SetProperty(ref connectionStatus, value); }
    public int TotalModelCount { get => totalModelCount; private set => SetProperty(ref totalModelCount, value); }
    public int ProtectedKeyCount { get => protectedKeyCount; private set => SetProperty(ref protectedKeyCount, value); }
    public int HealthyProviderCount { get => healthyProviderCount; private set => SetProperty(ref healthyProviderCount, value); }
    public int EnabledProviderCount { get => enabledProviderCount; private set => SetProperty(ref enabledProviderCount, value); }
    public int ActiveTabIndex { get => activeTabIndex; set => SetProperty(ref activeTabIndex, value); }
    public ICommand RefreshCommand { get; }
    public ICommand NewProviderCommand { get; }
    public ICommand SaveProviderCommand { get; }
    public ICommand DeleteProviderCommand { get; }
    public ICommand NewModelCommand { get; }
    public ICommand SaveModelCommand { get; }
    public ICommand DeleteModelCommand { get; }
    public ICommand ToggleAllModelsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand SyncModelsCommand { get; }

    public ProvidersViewModel(AppDataStore dataStore, ToastService? toastService = null, ILogger<ProvidersViewModel>? logger = null)
    {
        this.dataStore = dataStore;
        this.toastService = toastService ?? new ToastService();
        this.logger = logger;
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        RefreshCommand = new AsyncCommand(RefreshAsync); NewProviderCommand = new DelegateCommand(NewProvider); SaveProviderCommand = new AsyncCommand(SaveProviderAsync); DeleteProviderCommand = new AsyncCommand(parameter => DeleteProviderAsync(parameter as ProviderEditorViewModel)); NewModelCommand = new DelegateCommand(NewModel); SaveModelCommand = new AsyncCommand(SaveModelAsync); DeleteModelCommand = new AsyncCommand(parameter => DeleteModelAsync(parameter as ModelEditorViewModel)); ToggleAllModelsCommand = new AsyncCommand(ToggleAllModelsAsync); TestConnectionCommand = new AsyncCommand(TestConnectionAsync); SyncModelsCommand = new AsyncCommand(SyncModelsAsync); _ = RefreshAsync();
    }

    public ProvidersViewModel(ConfigSnapshotService configService, ToastService? toastService = null, ILogger<ProvidersViewModel>? logger = null)
        : this(new AppDataStore(configService, new GatewayProcessService()), toastService, logger) { }

    private Task RefreshAsync()
    {
        lock (refreshSync)
        {
            refreshRequested = true;
            if (refreshTask is { IsCompleted: false }) return refreshTask;
            refreshTask = RefreshLoopAsync();
            return refreshTask;
        }
    }

    private async Task RefreshLoopAsync()
    {
        while (true)
        {
            lock (refreshSync)
            {
                if (!refreshRequested)
                {
                    refreshTask = null;
                    return;
                }

                refreshRequested = false;
            }

            await RefreshCoreAsync();
        }
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            logger?.LogInformation("Provider 页面刷新开始，进程 {ProcessId}", Environment.ProcessId);
            var selectedId = SelectedProvider?.Id;
            suppressSelectionInvariant = true;
            Providers.Clear();
            await dataStore.InitializeAsync();
            foreach (var provider in dataStore.Providers) Providers.Add(ProviderEditorViewModel.FromResponse(provider));
            suppressSelectionInvariant = false;
            SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == selectedId) ?? Providers.FirstOrDefault();
            OnPropertyChanged(nameof(HasNoSelectedProvider));
            UpdateSummary();
            Status = $"已加载 {Providers.Count} 个 Provider";
            logger?.LogInformation("Provider 页面刷新完成，Provider {ProviderCount}，模型 {ModelCount}，启用 Provider {EnabledProviderCount}，健康 Provider {HealthyProviderCount}", Providers.Count, TotalModelCount, EnabledProviderCount, HealthyProviderCount);
        }
        catch (Exception exception) { suppressSelectionInvariant = false; logger?.LogError(exception, "Provider 页面刷新失败"); Status = $"加载失败：{exception.Message}"; }
    }

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        if (suppressConfigurationRefresh) return;
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }

    public void Dispose()
    {
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        providerAutoSaveCancellation?.Cancel();
        providerAutoSaveCancellation?.Dispose();
        modelAutoSaveCancellation?.Cancel();
        modelAutoSaveCancellation?.Dispose();
        connectionCancellation?.Cancel();
        modelSyncCancellation?.Cancel();
        modelSyncAnimationTimer?.Stop();
        modelSyncAnimationTimer = null;
        httpClient.Dispose();
    }

    private void NewProvider() { var provider = new ProviderEditorViewModel { DisplayName = "新 Provider", ApiMode = "openai", EndpointFormat = "responses", Enabled = true }; Providers.Add(provider); SelectedProvider = provider; UpdateSummary(); Status = "正在编辑新 Provider"; }

    internal void QueueProviderAutoSave(ProviderEditorViewModel provider)
    {
        providerAutoSaveCancellation?.Cancel();
        providerAutoSaveCancellation?.Dispose();
        providerAutoSaveCancellation = new CancellationTokenSource();
        _ = AutoSaveProviderAfterDelayAsync(provider, providerAutoSaveCancellation.Token);
    }

    private async Task AutoSaveProviderAfterDelayAsync(ProviderEditorViewModel provider, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            await SaveProviderAsync(provider);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal void QueueModelAutoSave(ProviderEditorViewModel provider, ModelEditorViewModel model)
    {
        modelAutoSaveCancellation?.Cancel();
        modelAutoSaveCancellation?.Dispose();
        modelAutoSaveCancellation = new CancellationTokenSource();
        _ = AutoSaveModelAfterDelayAsync(provider, model, modelAutoSaveCancellation.Token);
    }

    private async Task AutoSaveModelAfterDelayAsync(ProviderEditorViewModel provider, ModelEditorViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            await SaveModelAsync(provider, model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private Task SaveProviderAsync() => SaveProviderAsync(SelectedProvider);

    private async Task SaveProviderAsync(ProviderEditorViewModel? target)
    {
        var provider = target;
        if (provider is null) return;
        if (!provider.HasUnsavedChanges) return;
        suppressConfigurationRefresh = true;
        try
        {
            var input = provider.ToInput();
            var response = provider.Id == Guid.Empty ? await dataStore.CreateProviderAsync(input) : await dataStore.UpdateProviderAsync(provider.Id, input);
            provider.ApplyResponse(response);
            UpdateSummary();
            if (provider.IncompleteHeaderCount > 0)
            {
                Status = $"Provider 已保存 · {provider.IncompleteHeaderCount} 个请求头待补全";
                toastService.Show($"请补全 {provider.IncompleteHeaderCount} 个自定义请求头", ToastLevel.Warning);
                logger?.LogWarning("Provider 保存完成但存在未完成请求头 {ProviderId} {IncompleteHeaderCount}", provider.BusinessId, provider.IncompleteHeaderCount);
            }
            else
            {
                Status = "Provider 已保存";
                logger?.LogInformation("Provider 保存完成 {ProviderId}", provider.BusinessId);
            }
        }
        catch (Exception exception) { logger?.LogError(exception, "Provider 保存失败 {ProviderId}", provider.BusinessId); Status = $"保存失败：{exception.Message}"; }
        finally { suppressConfigurationRefresh = false; }
    }

    private async Task DeleteProviderAsync(ProviderEditorViewModel? provider = null)
    {
        provider ??= SelectedProvider;
        if (provider is null) return;
        try { if (provider.Id != Guid.Empty) await dataStore.DeleteProviderAsync(provider.Id); Providers.Remove(provider); if (ReferenceEquals(SelectedProvider, provider)) SelectedProvider = Providers.FirstOrDefault(); UpdateSummary(); Status = "Provider 已删除"; }
        catch (Exception exception) { Status = $"删除失败：{exception.Message}"; }
    }

    private void NewModel() { if (SelectedProvider is null || SelectedProvider.Id == Guid.Empty) { Status = "请先保存 Provider，再添加模型"; return; } SelectedModel = new ModelEditorViewModel { ProviderId = SelectedProvider.BusinessId }; Status = "正在编辑新模型"; }

    private Task SaveModelAsync() => SaveModelAsync(SelectedProvider, SelectedModel);

    private async Task SaveModelAsync(ProviderEditorViewModel? provider, ModelEditorViewModel? model)
    {
        if (provider is null || model is null) return;
        if (!model.HasUnsavedChanges) return;
        suppressConfigurationRefresh = true;
        try { var input = model.ToInput(); var response = model.Id == Guid.Empty ? await dataStore.CreateModelAsync(provider.Id, input) : await dataStore.UpdateModelAsync(model.Id, input); var index = provider.Models.IndexOf(model); var updated = ModelEditorViewModel.FromResponse(response); if (index < 0) provider.Models.Add(updated); else provider.Models[index] = updated; if (ReferenceEquals(SelectedModel, model)) SelectedModel = updated; Status = "模型已保存"; }
        catch (Exception exception) { Status = $"模型保存失败：{exception.Message}"; }
        finally { suppressConfigurationRefresh = false; }
    }

    private Task DeleteModelAsync() => DeleteModelAsync(SelectedModel);

    private async Task DeleteModelAsync(ModelEditorViewModel? model)
    {
        if (model is null) return;
        try { if (model.Id != Guid.Empty) await dataStore.DeleteModelAsync(model.Id); SelectedProvider?.Models.Remove(model); if (ReferenceEquals(SelectedModel, model)) SelectedModel = null; Status = "模型已删除"; }
        catch (Exception exception) { Status = $"模型删除失败：{exception.Message}"; }
    }

    private async Task TestConnectionAsync()
    {
        var provider = SelectedProvider;
        if (provider is null || string.IsNullOrWhiteSpace(provider.BaseUrl)) { ConnectionStatus = "请先填写 Base URL"; toastService.Show("请先填写 Provider Base URL", ToastLevel.Warning); return; }
        connectionCancellation?.Cancel();
        connectionCancellation = new CancellationTokenSource();
        var token = connectionCancellation.Token;
        ConnectionStatus = "正在测试连接…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var baseUrl = provider.BaseUrl.TrimEnd('/');
            var endpoint = $"{baseUrl}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(provider.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            foreach (var header in ProviderEditorViewModel.ParseDictionary(provider.HeadersJson) ?? []) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            stopwatch.Stop();
            ConnectionStatus = response.IsSuccessStatusCode ? $"连接正常 · {(int)response.StatusCode} · {stopwatch.ElapsedMilliseconds} ms" : $"连接失败 · {(int)response.StatusCode} {response.ReasonPhrase}";
            toastService.Show(response.IsSuccessStatusCode ? "Provider 连通性测试成功" : "Provider 连通性测试失败", response.IsSuccessStatusCode ? ToastLevel.Success : ToastLevel.Error);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { stopwatch.Stop(); ConnectionStatus = $"连接失败 · {exception.Message}"; toastService.Show("Provider 连通性测试失败", ToastLevel.Error); }
    }

    private async Task SyncModelsAsync()
    {
        var provider = SelectedProvider;
        if (provider is null) return;
        if (provider.Id == Guid.Empty)
        {
            Status = "请先保存 Provider，再同步模型";
            toastService.Show(Status, ToastLevel.Warning);
            return;
        }

        if (!Uri.TryCreate(BuildModelListEndpoint(provider), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            Status = "模型列表 URL 必须是 HTTP 或 HTTPS 绝对地址";
            toastService.Show(Status, ToastLevel.Warning);
            return;
        }

        modelSyncCancellation?.Cancel();
        var requestCancellation = new CancellationTokenSource();
        modelSyncCancellation = requestCancellation;
        var token = requestCancellation.Token;
        Status = "正在同步模型…";
        IsModelSyncing = true;
        StartModelSyncAnimation();
        logger?.LogInformation("模型同步开始 {ProviderId}", provider.BusinessId);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(provider.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            foreach (var header in ProviderEditorViewModel.ParseDictionary(provider.HeadersJson) ?? []) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                Status = $"模型同步失败 · {(int)response.StatusCode} {response.ReasonPhrase}";
                toastService.Show($"模型同步失败 · {(int)response.StatusCode}", ToastLevel.Error);
                logger?.LogWarning("模型同步失败 {ProviderId} {StatusCode}", provider.BusinessId, (int)response.StatusCode);
                return;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            var descriptors = ExtractModelDescriptors(document.RootElement)
                .GroupBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (descriptors.Length == 0)
            {
                Status = "模型同步失败 · 响应中没有可用模型";
                toastService.Show(Status, ToastLevel.Error);
                logger?.LogWarning("模型同步失败，响应中没有可用模型 {ProviderId}", provider.BusinessId);
                return;
            }

            var existing = provider.Models.Where(model => model.IsRealModel).ToDictionary(model => model.ModelId, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var updated = 0;
            suppressConfigurationRefresh = true;
            foreach (var descriptor in descriptors)
            {
                if (existing.TryGetValue(descriptor.ModelId, out var current))
                {
                    var index = provider.Models.IndexOf(current);
                    var responseModel = await dataStore.UpdateModelAsync(current.Id, current.ToRemoteInput(descriptor), token);
                    provider.Models[index] = ModelEditorViewModel.FromResponse(responseModel);
                    updated++;
                }
                else
                {
                    var created = await dataStore.CreateModelAsync(provider.Id, ModelEditorViewModel.CreateRemoteInput(provider.ApiMode, descriptor), token);
                    provider.Models.Add(ModelEditorViewModel.FromResponse(created));
                    added++;
                }
            }
            suppressConfigurationRefresh = false;
            Status = $"模型同步完成 · 发现 {descriptors.Length} 个，新增 {added} 个，更新 {updated} 个";
            toastService.Show(Status, ToastLevel.Success);
            logger?.LogInformation("模型同步完成 {ProviderId} {DiscoveredCount} {AddedCount} {UpdatedCount}", provider.BusinessId, descriptors.Length, added, updated);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (JsonException exception)
        {
            Status = "模型同步失败 · 响应格式无法解析";
            toastService.Show(Status, ToastLevel.Error);
            logger?.LogWarning(exception, "模型同步响应格式无法解析 {ProviderId}", provider.BusinessId);
        }
        catch (Exception exception)
        {
            Status = $"模型同步失败 · {exception.Message}";
            toastService.Show("模型同步失败", ToastLevel.Error);
            logger?.LogError(exception, "模型同步异常 {ProviderId}", provider.BusinessId);
        }
        finally
        {
            suppressConfigurationRefresh = false;
            if (ReferenceEquals(modelSyncCancellation, requestCancellation))
            {
                modelSyncCancellation = null;
                StopModelSyncAnimation();
            }

            requestCancellation.Dispose();
        }
    }

    private void StartModelSyncAnimation()
    {
        modelSyncAnimationTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        if (modelSyncAnimationTimer.IsEnabled) return;
        modelSyncAnimationTimer.Tick += ModelSyncAnimationTimerOnTick;
        SyncIconAngle = 0;
        modelSyncAnimationTimer.Start();
    }

    private void StopModelSyncAnimation()
    {
        if (modelSyncAnimationTimer is not null)
        {
            modelSyncAnimationTimer.Stop();
            modelSyncAnimationTimer.Tick -= ModelSyncAnimationTimerOnTick;
        }

        SyncIconAngle = 0;
        IsModelSyncing = false;
    }

    private void ModelSyncAnimationTimerOnTick(object? sender, EventArgs e) => SyncIconAngle = (SyncIconAngle + 18) % 360;

    internal static string BuildModelListEndpoint(ProviderEditorViewModel provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.ModelListUrl)) return provider.ModelListUrl.Trim();
        var baseUrl = provider.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/models";
    }

    internal sealed record RemoteModelDescriptor(string ModelId, string? OwnedBy, string? Family, int? ContextLength, int? MaxTokens, bool? Vision);

    internal static IEnumerable<RemoteModelDescriptor> ExtractModelDescriptors(JsonElement root)
    {
        var items = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                ? data
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var models)
                    ? models
                    : default;
        if (items.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var modelId = ReadString(item, "id") ?? ReadString(item, "name") ?? ReadString(item, "model");
            if (string.IsNullOrWhiteSpace(modelId)) continue;
            var topProvider = item.TryGetProperty("top_provider", out var top) && top.ValueKind == JsonValueKind.Object ? top : default;
            var contextLength = ReadPositiveInt(item, "context_length") ?? ReadPositiveInt(topProvider, "context_length") ?? ReadPositiveInt(item, "inputTokenLimit");
            var maxTokens = ReadPositiveInt(topProvider, "max_completion_tokens") ?? ReadPositiveInt(item, "max_completion_tokens") ?? ReadPositiveInt(item, "max_output_tokens") ?? ReadPositiveInt(item, "outputTokenLimit");
            var family = ReadString(item, "family");
            var ownedBy = ReadString(item, "owned_by") ?? ReadString(item, "ownedBy");
            var vision = ReadNullableBool(item, "vision") ?? ReadCapabilitiesVision(item);
            yield return new RemoteModelDescriptor(modelId.Trim(), ownedBy, family, contextLength, maxTokens, vision);
        }
    }

    public bool BeginModelDrag(ModelEditorViewModel? model)
    {
        if (model is null || model.IsPlaceholder || SelectedProvider is null || DraggingModel is not null) return false;
        var provider = SelectedProvider;
        var index = provider.Models.IndexOf(model);
        if (index < 0 || provider.Models.Count(item => item.IsRealModel) < 2) return false;
        modelDragOwnerProvider = provider;
        draggingModelOriginIndex = index;
        modelDragPlaceholder = ModelEditorViewModel.CreatePlaceholder();
        provider.Models.RemoveAt(index);
        provider.Models.Insert(index, modelDragPlaceholder);
        provider.IsModelDragPreviewOwner = true;
        model.IsDragging = true;
        DraggingModel = model;
        return true;
    }

    public bool MoveModelDragPlaceholder(int targetIndex)
    {
        if (modelDragOwnerProvider is null || modelDragPlaceholder is null) return false;
        var currentIndex = modelDragOwnerProvider.Models.IndexOf(modelDragPlaceholder);
        if (currentIndex < 0) return false;
        var clampedIndex = Math.Clamp(targetIndex, 0, modelDragOwnerProvider.Models.Count - 1);
        if (currentIndex == clampedIndex) return false;
        modelDragOwnerProvider.Models.Move(currentIndex, clampedIndex);
        return true;
    }

    public async Task CompleteModelDragAsync()
    {
        if (modelDragOwnerProvider is null || DraggingModel is null || modelDragPlaceholder is null) return;
        var provider = modelDragOwnerProvider;
        var targetIndex = provider.Models.IndexOf(modelDragPlaceholder);
        if (targetIndex < 0) { CancelModelDrag(); return; }

        provider.Models.RemoveAt(targetIndex);
        DraggingModel.IsDragging = false;
        provider.Models.Insert(targetIndex, DraggingModel);
        ClearModelDragState();
        RenumberModels(provider);
        suppressConfigurationRefresh = true;
        try
        {
            await dataStore.UpdateModelOrderAsync(provider.Id, new ModelOrderInput(provider.Models.Select(model => model.Id).ToArray()));
            Status = "模型顺序已保存";
            toastService.Show(Status, ToastLevel.Success);
        }
        catch (Exception exception)
        {
            Status = $"模型排序保存失败：{exception.Message}";
            toastService.Show("模型排序保存失败", ToastLevel.Error);
            logger?.LogError(exception, "模型排序保存失败 {ProviderId}", provider.BusinessId);
        }
        finally { suppressConfigurationRefresh = false; }
    }

    public void CancelModelDrag()
    {
        if (modelDragOwnerProvider is null || DraggingModel is null || modelDragPlaceholder is null) return;
        var provider = modelDragOwnerProvider;
        var placeholderIndex = provider.Models.IndexOf(modelDragPlaceholder);
        if (placeholderIndex >= 0) provider.Models.RemoveAt(placeholderIndex);
        var model = DraggingModel;
        model.IsDragging = false;
        var restoreIndex = Math.Clamp(draggingModelOriginIndex, 0, provider.Models.Count);
        provider.Models.Insert(restoreIndex, model);
        ClearModelDragState();
        RenumberModels(provider);
    }

    private void ClearModelDragState()
    {
        if (modelDragOwnerProvider is not null) modelDragOwnerProvider.IsModelDragPreviewOwner = false;
        DraggingModel = null;
        modelDragPlaceholder = null;
        modelDragOwnerProvider = null;
        draggingModelOriginIndex = -1;
    }

    private async Task ToggleAllModelsAsync()
    {
        var provider = SelectedProvider;
        var models = provider?.Models.Where(model => model.IsRealModel).ToArray() ?? [];
        if (models.Length == 0) return;
        var enabled = !models.All(model => model.Enabled);
        modelAutoSaveCancellation?.Cancel();
        modelAutoSaveCancellation?.Dispose();
        modelAutoSaveCancellation = null;
        suppressConfigurationRefresh = true;
        try
        {
            foreach (var model in models)
            {
                model.Enabled = enabled;
                await SaveModelAsync(provider, model);
            }
            Status = enabled ? "已启用全部模型" : "已停用全部模型";
            toastService.Show(Status, ToastLevel.Success);
        }
        finally { suppressConfigurationRefresh = false; }
    }

    private static void RenumberModels(ProviderEditorViewModel provider)
    {
        var index = 0;
        foreach (var model in provider.Models.Where(model => model.IsRealModel)) model.SortOrder = index++;
    }

    private static string? ReadString(JsonElement element, string propertyName) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;
    private static int? ReadPositiveInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) && number > 0) return number;
        return null;
    }
    private static bool? ReadNullableBool(JsonElement element, string propertyName) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static bool? ReadCapabilitiesVision(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("vision", out _)) return ReadNullableBool(element, "vision");
        if (element.TryGetProperty("capabilities", out var capabilities))
        {
            if (capabilities.ValueKind == JsonValueKind.Array) return capabilities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && IsVisionCapability(item.GetString()));
            if (capabilities.ValueKind == JsonValueKind.Object) return ReadNullableBool(capabilities, "vision");
        }

        if (element.TryGetProperty("input_modalities", out var inputModalities) && inputModalities.ValueKind == JsonValueKind.Array)
            return inputModalities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "image", StringComparison.OrdinalIgnoreCase));
        if (element.TryGetProperty("architecture", out var architecture) && architecture.ValueKind == JsonValueKind.Object && architecture.TryGetProperty("input_modalities", out var architectureModalities) && architectureModalities.ValueKind == JsonValueKind.Array)
            return architectureModalities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "image", StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private static bool IsVisionCapability(string? value) => value is not null && (string.Equals(value, "vision", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "image", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "multimodal", StringComparison.OrdinalIgnoreCase));

    private void AttachProvider(ProviderEditorViewModel? provider)
    {
        if (provider is null) return;
        provider.PropertyChanged += ProviderChanged;
        provider.Models.CollectionChanged += ModelsChanged;
        foreach (var model in provider.Models) AttachModel(model);
    }

    private void DetachProvider(ProviderEditorViewModel? provider)
    {
        if (provider is null) return;
        provider.PropertyChanged -= ProviderChanged;
        provider.Models.CollectionChanged -= ModelsChanged;
        foreach (var model in provider.Models) DetachModel(model);
    }

    private void ProviderChanged(object? sender, PropertyChangedEventArgs args)
    {
        UpdateSummary();
        if (!suppressConfigurationRefresh && sender is ProviderEditorViewModel provider && provider.HasUnsavedChanges)
            QueueProviderAutoSave(provider);
    }
    private void ModelsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null) foreach (ModelEditorViewModel model in args.NewItems) model.PropertyChanged += ModelChanged;
        if (args.OldItems is not null) foreach (ModelEditorViewModel model in args.OldItems) model.PropertyChanged -= ModelChanged;
        UpdateSummary();
        OnPropertyChanged(nameof(EnabledModelCount));
        OnPropertyChanged(nameof(AllModelsEnabled));
        OnPropertyChanged(nameof(EnabledModelSummary));
    }

    private void AttachModel(ModelEditorViewModel? model) { if (model is not null) model.PropertyChanged += ModelChanged; }
    private void DetachModel(ModelEditorViewModel? model) { if (model is not null) model.PropertyChanged -= ModelChanged; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not ModelEditorViewModel model) return;
        if (!ReferenceEquals(SelectedModel, model))
            SelectedModel = model;
        if (args.PropertyName is nameof(ModelEditorViewModel.Enabled))
        {
            OnPropertyChanged(nameof(EnabledModelCount));
            OnPropertyChanged(nameof(AllModelsEnabled));
            OnPropertyChanged(nameof(EnabledModelSummary));
        }
        if (!suppressConfigurationRefresh && SelectedProvider is { } provider && model.HasUnsavedChanges)
            QueueModelAutoSave(provider, model);
    }

    private void UpdateSummary()
    {
        TotalModelCount = Providers.Sum(provider => provider.Models.Count(model => model.IsRealModel));
        ProtectedKeyCount = Providers.Count(provider => provider.HasApiKey);
        EnabledProviderCount = Providers.Count(provider => provider.Enabled);
        HealthyProviderCount = Providers.Count(provider => provider.Enabled && !string.IsNullOrWhiteSpace(provider.BaseUrl));
    }

}

public sealed class ProviderEditorViewModel : NotifyViewModel
{
    public Guid Id { get; set; }
    private string businessId = ""; private string displayName = ""; private string baseUrl = ""; private string modelListUrl = ""; private string apiMode = "openai"; private string endpointFormat = "responses"; private bool enabled; private bool useProxy; private string apiKey = ""; private bool apiKeyEdited; private bool isApiKeyVisible; private string headersJson = "{}";
    private bool isDirty;
    private bool isModelDragPreviewOwner;
    private bool suppressDirtyTracking;
    public string BusinessId { get => businessId; set => SetProperty(ref businessId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ModelListUrl { get => modelListUrl; set => SetProperty(ref modelListUrl, value); }
    public string ApiMode { get => apiMode; set { if (!SetProperty(ref apiMode, value)) return; OnPropertyChanged(nameof(IsEndpointFormatVisible)); } }
    public string EndpointFormat { get => endpointFormat; set { var normalized = EndpointFormatOption.Normalize(value); if (!SetProperty(ref endpointFormat, normalized)) return; OnPropertyChanged(nameof(SelectedEndpointFormat)); } }
    public IReadOnlyList<EndpointFormatOption> EndpointFormatOptions { get; } = EndpointFormatOption.All;
    public EndpointFormatOption SelectedEndpointFormat { get => EndpointFormatOption.FromValue(EndpointFormat); set { if (value is not null) EndpointFormat = value.Value; } }
    public bool IsEndpointFormatVisible => string.Equals(ApiMode, "openai", StringComparison.OrdinalIgnoreCase);
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public bool UseProxy { get => useProxy; set => SetProperty(ref useProxy, value); } public string ApiKey { get => apiKey; set { if (SetProperty(ref apiKey, value)) apiKeyEdited = true; } } public bool IsApiKeyVisible { get => isApiKeyVisible; private set { if (SetProperty(ref isApiKeyVisible, value)) { OnPropertyChanged(nameof(IsApiKeyHidden)); OnPropertyChanged(nameof(ApiKeyPasswordChar)); OnPropertyChanged(nameof(ApiKeyVisibilityToolTip)); } } } public bool IsApiKeyHidden => !IsApiKeyVisible; public char ApiKeyPasswordChar => IsApiKeyVisible ? '\0' : '●'; public string ApiKeyVisibilityToolTip => IsApiKeyVisible ? "隐藏 API Key" : "显示 API Key"; public string HeadersJson { get => headersJson; private set => SetProperty(ref headersJson, value); } public bool HasApiKey { get; private set; } public string ApiKeyWatermark => HasApiKey ? "已配置 API Key" : "请输入 API Key";
    public bool HasUnsavedChanges => Id == Guid.Empty || isDirty;
    public ObservableCollection<ModelEditorViewModel> Models { get; } = [];
    public bool IsModelDragPreviewOwner { get => isModelDragPreviewOwner; set => SetProperty(ref isModelDragPreviewOwner, value); }
    public bool HasModels => Models.Any(model => model.IsRealModel);
    public ObservableCollection<HeaderEditorViewModel> Headers { get; } = [];
    public bool HasNoHeaders => Headers.Count == 0;
    public int IncompleteHeaderCount => Headers.Count(IsIncomplete);
    public bool HasIncompleteHeaders => IncompleteHeaderCount > 0;
    public ProviderEditorViewModel()
    {
        PropertyChanged += (_, args) =>
        {
            if (!suppressDirtyTracking && IsPersistedProperty(args.PropertyName) && !isDirty)
            {
                isDirty = true;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        };
        Headers.CollectionChanged += HeadersChanged;
        Models.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModels));
    }
    public static ProviderEditorViewModel FromResponse(ProviderResponse response) { var value = new ProviderEditorViewModel(); value.ApplyResponse(response); foreach (var model in response.Models) value.Models.Add(ModelEditorViewModel.FromResponse(model)); return value; }
    public ProviderInput ToInput() => new(BusinessId, DisplayName, BaseUrl, ApiMode, Enabled, apiKeyEdited ? ApiKey : null, false, ToHeaderDictionary(), UseProxy, string.IsNullOrWhiteSpace(ModelListUrl) ? null : ModelListUrl, EndpointFormat);
    public void ApplyResponse(ProviderResponse response)
    {
        suppressDirtyTracking = true;
        try
        {
            Id = response.Id; BusinessId = response.BusinessId; DisplayName = response.DisplayName; BaseUrl = response.BaseUrl; ModelListUrl = response.ModelListUrl ?? ""; ApiMode = response.ApiMode; EndpointFormat = response.EndpointFormat; Enabled = response.Enabled; UseProxy = response.UseProxy; HasApiKey = response.HasApiKey; OnPropertyChanged(nameof(ApiKeyWatermark));
            if (response.ApiKey is not null || !response.HasApiKey)
                SetApiKeyFromResponse(response.ApiKey ?? "");
            else
                apiKeyEdited = false;
            SetHeadersFromJson(response.HeadersJson);
        }
        finally
        {
            suppressDirtyTracking = false;
            if (isDirty)
            {
                isDirty = false;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }
    }
    public void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;
    public void AddHeader() => Headers.Add(new HeaderEditorViewModel());
    public void RemoveHeader(HeaderEditorViewModel header) { if (Headers.Contains(header)) Headers.Remove(header); }
    private void SetApiKeyFromResponse(string value)
    {
        SetProperty(ref apiKey, value, nameof(ApiKey));
        apiKeyEdited = false;
    }
    private void SetHeadersFromJson(string json)
    {
        var incompleteHeaders = Headers.Where(IsIncomplete).ToArray();
        Headers.CollectionChanged -= HeadersChanged;
        foreach (var header in Headers) header.PropertyChanged -= HeaderChanged;
        Headers.Clear();
        var incompleteNames = incompleteHeaders
            .Where(header => !string.IsNullOrWhiteSpace(header.Name))
            .Select(header => header.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ParseDictionary(json) ?? [])
        {
            if (incompleteNames.Contains(pair.Key.Trim())) continue;
            var header = new HeaderEditorViewModel { Name = pair.Key, Value = pair.Value };
            header.PropertyChanged += HeaderChanged;
            Headers.Add(header);
        }
        foreach (var header in incompleteHeaders)
        {
            header.PropertyChanged += HeaderChanged;
            Headers.Add(header);
        }
        Headers.CollectionChanged += HeadersChanged;
        OnPropertyChanged(nameof(HasNoHeaders));
        OnPropertyChanged(nameof(IncompleteHeaderCount));
        OnPropertyChanged(nameof(HasIncompleteHeaders));
        HeadersJson = JsonSerializer.Serialize(ToHeaderDictionary());
    }
    private void HeadersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null) foreach (HeaderEditorViewModel header in args.NewItems) header.PropertyChanged += HeaderChanged;
        if (args.OldItems is not null) foreach (HeaderEditorViewModel header in args.OldItems) header.PropertyChanged -= HeaderChanged;
        OnPropertyChanged(nameof(HasNoHeaders));
        OnPropertyChanged(nameof(IncompleteHeaderCount));
        OnPropertyChanged(nameof(HasIncompleteHeaders));
        HeadersJson = JsonSerializer.Serialize(ToHeaderDictionary());
        OnPropertyChanged(nameof(Headers));
    }
    private void HeaderChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(IncompleteHeaderCount));
        OnPropertyChanged(nameof(HasIncompleteHeaders));
        HeadersJson = JsonSerializer.Serialize(ToHeaderDictionary());
    }
    private Dictionary<string, string> ToHeaderDictionary()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Headers)
            if (!string.IsNullOrWhiteSpace(header.Name) && !string.IsNullOrWhiteSpace(header.Value)) result[header.Name.Trim()] = header.Value;
        return result;
    }
    private static bool IsIncomplete(HeaderEditorViewModel header) => string.IsNullOrWhiteSpace(header.Name) || string.IsNullOrWhiteSpace(header.Value);
    private static bool IsPersistedProperty(string? propertyName) => propertyName is nameof(BusinessId) or nameof(DisplayName) or nameof(BaseUrl) or nameof(ModelListUrl) or nameof(ApiMode) or nameof(EndpointFormat) or nameof(Enabled) or nameof(UseProxy) or nameof(ApiKey) or nameof(HeadersJson) or nameof(Headers);
    internal static Dictionary<string, string>? ParseDictionary(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
}

public sealed record EndpointFormatOption(string Value, string DisplayName)
{
    public static IReadOnlyList<EndpointFormatOption> All { get; } = [new("chat_completions", "OpenAI-Completions"), new("responses", "Responses API")];
    public static EndpointFormatOption FromValue(string? value) => All.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)) ?? All[1];
    public static string Normalize(string? value) => FromValue(value).Value;
    public override string ToString() => DisplayName;
}

public sealed class HeaderEditorViewModel : NotifyViewModel
{
    private string name = "";
    private string value = "";
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Value { get => value; set => SetProperty(ref this.value, value); }
}

public sealed class ModelEditorViewModel : NotifyViewModel
{
    public Guid Id { get; set; } public string ProviderId { get; set; } = "";
    private bool isDirty;
    private bool isDragging;
    private bool isPlaceholder;
    private string modelId = ""; private string displayName = ""; private string family = "claude"; private string configId = ""; private string baseUrl = ""; private string apiMode = ""; private int contextLength = 128000; private int maxTokens = 4096; private bool vision; private double? temperature; private double? topP; private bool enabled = true; private string apiKey = ""; private bool clearApiKey; private string headersJson = "{}"; private string extraJson = "{}";
    private string? ownedBy; private string? remoteFamily; private int? remoteContextLength; private int? remoteMaxTokens; private bool? remoteVision; private int sortOrder;
    public ModelEditorViewModel()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not nameof(HasApiKey) and not nameof(HasUnsavedChanges) and not nameof(SortOrder) and not nameof(IsDragging))
                isDirty = true;
        };
    }
    public bool HasUnsavedChanges => Id == Guid.Empty || isDirty;
    public string ModelId { get => modelId; set => SetProperty(ref modelId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string Family { get => family; set => SetProperty(ref family, value); } public string ConfigId { get => configId; set => SetProperty(ref configId, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ApiMode { get => apiMode; set => SetProperty(ref apiMode, value); } public int ContextLength { get => contextLength; set => SetProperty(ref contextLength, value); } public int MaxTokens { get => maxTokens; set => SetProperty(ref maxTokens, value); } public bool Vision { get => vision; set => SetProperty(ref vision, value); } public double? Temperature { get => temperature; set => SetProperty(ref temperature, value); } public double? TopP { get => topP; set => SetProperty(ref topP, value); } public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public string ApiKey { get => apiKey; set => SetProperty(ref apiKey, value); } public bool ClearApiKey { get => clearApiKey; set => SetProperty(ref clearApiKey, value); } public string HeadersJson { get => headersJson; set => SetProperty(ref headersJson, value); } public string ExtraJson { get => extraJson; set => SetProperty(ref extraJson, value); } public bool HasApiKey { get; private set; }
    public string? OwnedBy { get => ownedBy; private set => SetProperty(ref ownedBy, value); }
    public string? RemoteFamily { get => remoteFamily; private set => SetProperty(ref remoteFamily, value); }
    public int? RemoteContextLength { get => remoteContextLength; private set { if (SetProperty(ref remoteContextLength, value)) OnPropertyChanged(nameof(ContextDisplay)); } }
    public int? RemoteMaxTokens { get => remoteMaxTokens; private set { if (SetProperty(ref remoteMaxTokens, value)) OnPropertyChanged(nameof(MaxTokensDisplay)); } }
    public bool? RemoteVision { get => remoteVision; private set { if (SetProperty(ref remoteVision, value)) OnPropertyChanged(nameof(CapabilitiesDisplay)); } }
    public int SortOrder { get => sortOrder; internal set => SetProperty(ref sortOrder, value); }
    public bool IsDragging { get => isDragging; set => SetProperty(ref isDragging, value); }
    public bool IsPlaceholder { get => isPlaceholder; private init => isPlaceholder = value; }
    public bool IsRealModel => !IsPlaceholder;
    public string ContextDisplay => RemoteContextLength is int value ? value.ToString("N0") : "未提供";
    public string MaxTokensDisplay => RemoteMaxTokens is int value ? value.ToString("N0") : "未提供";
    public string CapabilitiesDisplay => RemoteVision is true ? "视觉" : RemoteVision is false ? "文本" : "未提供";
    public static ModelEditorViewModel FromResponse(ModelResponse response)
    {
        var value = new ModelEditorViewModel { Id = response.Id, ProviderId = response.ProviderId, ModelId = response.ModelId, DisplayName = response.DisplayName, ConfigId = response.ConfigId ?? "", Family = response.Family, BaseUrl = response.BaseUrl ?? "", ApiMode = response.ApiMode ?? "", ContextLength = response.ContextLength, MaxTokens = response.MaxTokens, Vision = response.Vision, Temperature = response.Temperature, TopP = response.TopP, Enabled = response.Enabled, HasApiKey = response.HasApiKey, HeadersJson = response.HeadersJson, ExtraJson = response.ExtraJson, ownedBy = response.OwnedBy, remoteFamily = response.RemoteFamily, remoteContextLength = response.RemoteContextLength, remoteMaxTokens = response.RemoteMaxTokens, remoteVision = response.RemoteVision, sortOrder = response.SortOrder };
        value.isDirty = false;
        return value;
    }
    public static ModelEditorViewModel CreatePlaceholder() => new() { IsPlaceholder = true };
    public ModelInput ToInput() => new(ModelId, DisplayName, string.IsNullOrWhiteSpace(ConfigId) ? null : ConfigId, Family, string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl, string.IsNullOrWhiteSpace(ApiMode) ? null : ApiMode, ContextLength, MaxTokens, Vision, Temperature, TopP, Enabled, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey, ClearApiKey, ProviderEditorViewModel.ParseDictionary(HeadersJson), JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ExtraJson), OwnedBy, RemoteFamily, RemoteContextLength, RemoteMaxTokens, RemoteVision, SortOrder);
    internal static ModelInput CreateRemoteInput(string apiMode, ProvidersViewModel.RemoteModelDescriptor descriptor) => new(descriptor.ModelId, descriptor.ModelId, null, descriptor.Family ?? "unknown", null, apiMode, descriptor.ContextLength ?? 128000, descriptor.MaxTokens ?? 4096, descriptor.Vision ?? false, null, null, true, null, false, null, null, descriptor.OwnedBy, descriptor.Family, descriptor.ContextLength, descriptor.MaxTokens, descriptor.Vision);
    internal ModelInput ToRemoteInput(ProvidersViewModel.RemoteModelDescriptor descriptor) => new(ModelId, ModelId, null, descriptor.Family ?? Family, null, ApiMode, descriptor.ContextLength ?? ContextLength, descriptor.MaxTokens ?? MaxTokens, descriptor.Vision ?? Vision, Temperature, TopP, Enabled, null, false, ProviderEditorViewModel.ParseDictionary(HeadersJson), JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ExtraJson), descriptor.OwnedBy, descriptor.Family, descriptor.ContextLength, descriptor.MaxTokens, descriptor.Vision, SortOrder);
}

public sealed class PlaceholderViewModel
{
    public string Title { get; }
    public string Description { get; }
    public PlaceholderViewModel(string title, string description) => (Title, Description) = (title, description);
}

public abstract class NotifyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); return true;
    }
}

public sealed class DelegateCommand : ICommand
{
    private readonly Action action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public DelegateCommand(Action action) => this.action = action;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}

public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> action;
    private readonly Func<object?, bool> canExecute;
    public event EventHandler? CanExecuteChanged;
    public AsyncCommand(Func<Task> action, Func<bool>? canExecute = null)
    {
        this.action = _ => action();
        this.canExecute = _ => canExecute?.Invoke() ?? true;
    }
    public AsyncCommand(Func<object?, Task> action, Func<object?, bool>? canExecute = null)
    {
        this.action = action;
        this.canExecute = parameter => canExecute?.Invoke(parameter) ?? true;
    }
    public bool CanExecute(object? parameter) => canExecute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public async void Execute(object? parameter) => await action(parameter);
}
