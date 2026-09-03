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
using OllamaHub;
using OllamaHub.Configuration;
using OllamaHub.Activity;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class MainWindowViewModel : NotifyViewModel
{
    private readonly GatewayProcessService gatewayService;
    private readonly ToastService toastService;
    private readonly ILoggerFactory loggerFactory;
    private readonly ConfigSnapshotService configService;
    private readonly ConsoleViewModel consoleViewModel;
    private readonly SettingsViewModel settingsViewModel;
    private readonly Action<bool, int, int, string>? applyAppearance;
    private object currentView = new PlaceholderViewModel("加载中", "正在加载桌面控制中心。");
    private string pageTitle = "概览";
    private string pageDescription = "确认本地服务健康，快速查看网关与模型配置。";

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public object CurrentView { get => currentView; private set => SetProperty(ref currentView, value); }
    public string PageTitle { get => pageTitle; private set => SetProperty(ref pageTitle, value); }
    public string PageDescription { get => pageDescription; private set => SetProperty(ref pageDescription, value); }

    public MainWindowViewModel(GatewayProcessService gatewayService, ToastService? toastService = null, ILoggerFactory? loggerFactory = null, ConfigSnapshotService? configService = null, Action<bool, int, int, string>? applyAppearance = null)
    {
        this.gatewayService = gatewayService;
        this.toastService = toastService ?? new ToastService();
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        this.configService = configService ?? new ConfigSnapshotService(this.loggerFactory.CreateLogger<ConfigSnapshotService>());
        this.applyAppearance = applyAppearance;
        consoleViewModel = new ConsoleViewModel(toastService: this.toastService);
        settingsViewModel = new SettingsViewModel(configService: this.configService, logger: this.loggerFactory.CreateLogger<SettingsViewModel>(), toastService: this.toastService, applyAppearance: this.applyAppearance);
        NavigationItems = new([
            new("概览", "M 4,18 L 12,10 L 20,18 L 20,30 L 4,30 Z M 9,30 L 9,20 L 15,20 L 15,30", () => ShowOverview()),
            new("网关", "M 16,4 L 16,9 M 16,9 L 8,16 M 16,9 L 24,16 M 8,16 L 8,25 M 24,16 L 24,25 M 4,25 L 12,25 M 20,25 L 28,25", () => ShowGateway()),
            new("Provider", "M 7,8 L 25,8 M 7,16 L 25,16 M 7,24 L 25,24 M 4,8 L 4,8 M 4,16 L 4,16 M 4,24 L 4,24", () => ShowProviders()),
            new("活动", "M 7,28 L 7,5 M 8,6 C 13,4 18,8 25,6 L 25,18 C 18,20 13,16 8,18", () => ShowActivity()),
            new("控制台", "M 5,6 L 27,6 L 27,26 L 5,26 Z M 9,12 L 13,16 L 9,20 M 16,20 L 23,20", () => ShowConsole()),
            new("设置", "M 16,4 L 18,7 L 22,8 L 25,6 L 28,9 L 26,12 L 27,16 L 30,18 L 28,22 L 24,21 L 21,24 L 21,28 L 16,29 L 14,25 L 10,24 L 7,26 L 4,22 L 6,19 L 5,15 L 2,13 L 4,8 L 8,9 L 11,6 L 11,3 Z M 16,12 A 4,4 0 1,0 16,20 A 4,4 0 1,0 16,12 Z", () => ShowSettings())
        ]);
        ShowOverview();
    }

    private void SetActive(string title)
    {
        foreach (var item in NavigationItems) item.IsActive = item.Title == title;
    }

    private void ShowOverview() { SetActive("概览"); PageTitle = "概览"; PageDescription = "确认本地服务健康，快速查看网关与模型配置。"; CurrentView = new OverviewViewModel(gatewayService, configService, loggerFactory.CreateLogger<MainWindowViewModel>()); }
    private void ShowProviders() { SetActive("Provider"); PageTitle = "Provider"; PageDescription = "管理上游连接、请求协议、密钥与可用模型。"; CurrentView = new ProvidersViewModel(configService, toastService, loggerFactory.CreateLogger<ProvidersViewModel>()); }
    private void ShowGateway() { SetActive("网关"); PageTitle = "网关"; PageDescription = "组合对外 Endpoint 的模型路由，并按优先级自动故障转移。"; CurrentView = new GatewayViewModel(configService, toastService); }
    private void ShowConsole() { SetActive("控制台"); PageTitle = "控制台"; PageDescription = "查看本地网关、协议转换与上游请求的脱敏运行日志。"; CurrentView = consoleViewModel; }
    private void ShowActivity() { SetActive("活动"); PageTitle = "请求活动"; PageDescription = "定位协议转换、上游延迟与 HTTP 错误，保留可追溯的脱敏上下文。"; CurrentView = new ActivityViewModel(gatewayService, loggerFactory.CreateLogger<ActivityViewModel>()); }
    private void ShowSettings() { SetActive("设置"); PageTitle = "设置"; PageDescription = "调整 OllamaHub 的显示、连接、更新与隐私偏好。"; CurrentView = settingsViewModel; }
    private void ShowPlaceholder(string title, string description) { SetActive(title); PageTitle = title; PageDescription = description; CurrentView = new PlaceholderViewModel(title, description); }
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
    private readonly GatewayProcessService gatewayService;
    private readonly ConfigSnapshotService configService;
    private readonly ActivityQueryService activityQueryService = new();
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
    private readonly Dictionary<string, RequestTelemetryEvent> activeRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> activeEdgeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> requestEdges = new(StringComparer.Ordinal);
    private readonly List<RequestTelemetryEvent> completionWindow = [];

    public event EventHandler? TopologyChanged;
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
    public ObservableCollection<OverviewEndpointViewModel> Endpoints { get; } = [];
    public ObservableCollection<OverviewModelViewModel> Models { get; } = [];
    public ObservableCollection<OverviewRecentRequestViewModel> RecentRequests { get; } = [];
    public bool RecentRequestsEmpty => RecentRequests.Count == 0;
    public string TopologyJson => JsonSerializer.Serialize(new
    {
        endpoints = Endpoints.Select(item => new { key = item.Key, displayName = item.DisplayName, publicPath = item.PublicPath, enabled = item.Enabled }),
        models = Models.Select(item => new { displayName = item.DisplayName, modelId = item.ModelId, providerId = item.ProviderId }),
        edges = Endpoints.SelectMany(endpoint => endpoint.Routes.Select(route => new { endpointKey = endpoint.Key, modelId = route.ModelId, providerId = route.ProviderId, alias = route.Alias }))
    });
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RefreshCommand { get; }

    public OverviewViewModel(GatewayProcessService gatewayService, ConfigSnapshotService configService, ILogger<MainWindowViewModel>? logger = null)
    {
        this.gatewayService = gatewayService;
        this.configService = configService;
        this.logger = logger;
        StartCommand = new AsyncCommand(StartAsync);
        StopCommand = new AsyncCommand(StopAsync);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        gatewayService.StateChanged += OnGatewayStateChanged;
        gatewayService.TelemetryPublished += OnTelemetryPublished;
        _ = RefreshAsync();
    }

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

    private async Task RefreshAsync()
    {
        try
        {
            var config = configService.Load();
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
            Version = gatewayService.State == GatewayState.Running ? "OllamaHub API 在线" : "未连接";
            GraphStatus = gatewayService.State == GatewayState.Running ? "实时拓扑已连接" : "等待网关启动";
            logger?.LogInformation("概览刷新完成 {ProviderCount} 个 Provider、{ModelCount} 个模型、{EndpointCount} 个 Endpoint、{RouteCount} 条路由，网关状态 {GatewayState}，配置库 {DatabasePath}，进程 {ProcessId}", ProviderCount, ModelCount, Endpoints.Count, Endpoints.Sum(item => item.Routes.Count), gatewayService.State, AppDataPaths.DatabasePath, Environment.ProcessId);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "概览刷新失败");
            GraphStatus = "概览加载失败";
        }
        await Task.CompletedTask;
    }

    private async Task RefreshRecentRequestsAsync()
    {
        try
        {
            var records = await activityQueryService.QueryAsync(new ActivityQuery(Limit: 8));
            await Dispatcher.UIThread.InvokeAsync(() => MergeRecentRequests(records.Select(OverviewRecentRequestViewModel.From)));
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
        Endpoints.Clear();
        Models.Clear();
        foreach (var model in config.Models.GroupBy(item => $"{item.ProviderId}:{item.ModelId}", StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
            Models.Add(new OverviewModelViewModel(model.DisplayName, model.ModelId, model.ProviderId));
        foreach (var endpoint in config.GatewayEndpoints.OrderBy(item => item.Key))
        {
            var endpointVm = new OverviewEndpointViewModel(endpoint.Key, EndpointLabel(endpoint.Key), endpoint.PublicPath, endpoint.Enabled);
            foreach (var combo in endpoint.Combos.Where(item => item.Enabled))
                foreach (var route in combo.Routes.Where(item => item.Enabled))
                    endpointVm.Routes.Add(new OverviewRouteViewModel(combo.Name, route.Model.DisplayName, route.Model.ModelId, route.Model.ProviderId));
            Endpoints.Add(endpointVm);
        }
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    private string LoadEndpoint()
    {
        var config = configService.Load();
        return config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
    }

    private void OnGatewayStateChanged(object? sender, EventArgs args) => _ = RefreshAsync();

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
    public string Key { get; }
    public string DisplayName { get; }
    public string PublicPath { get; }
    public bool Enabled { get; }
    public ObservableCollection<OverviewRouteViewModel> Routes { get; } = [];
    public int ActiveCount => Routes.Count(item => item.IsActive);
    public string Status => !Enabled ? "已停用" : Routes.Count == 0 ? "无路由" : ActiveCount > 0 ? "有请求" : "已就绪";
    public OverviewEndpointViewModel(string key, string displayName, string publicPath, bool enabled) => (Key, DisplayName, PublicPath, Enabled) = (key, displayName, publicPath, enabled);
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

public sealed class ProvidersViewModel : NotifyViewModel
{
    private readonly ConfigSnapshotService configService;
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
    private CancellationTokenSource? autoSaveCancellation;
    private CancellationTokenSource? connectionCancellation;
    private CancellationTokenSource? modelSyncCancellation;
    private DispatcherTimer? modelSyncAnimationTimer;
    private bool isModelSyncing;
    private double syncIconAngle;
    private bool suppressAutoSave;
    private bool suppressSelectionInvariant;
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
    public ICommand TestConnectionCommand { get; }
    public ICommand SyncModelsCommand { get; }

    public ProvidersViewModel(ConfigSnapshotService configService, ToastService? toastService = null, ILogger<ProvidersViewModel>? logger = null)
    {
        this.configService = configService;
        this.toastService = toastService ?? new ToastService();
        this.logger = logger;
        RefreshCommand = new AsyncCommand(RefreshAsync); NewProviderCommand = new DelegateCommand(NewProvider); SaveProviderCommand = new AsyncCommand(SaveProviderAsync); DeleteProviderCommand = new AsyncCommand(parameter => DeleteProviderAsync(parameter as ProviderEditorViewModel)); NewModelCommand = new DelegateCommand(NewModel); SaveModelCommand = new AsyncCommand(SaveModelAsync); DeleteModelCommand = new AsyncCommand(DeleteModelAsync); TestConnectionCommand = new AsyncCommand(TestConnectionAsync); SyncModelsCommand = new AsyncCommand(SyncModelsAsync); _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            logger?.LogInformation("Provider 页面刷新开始，进程 {ProcessId}", Environment.ProcessId);
            var selectedId = SelectedProvider?.Id;
            suppressSelectionInvariant = true;
            Providers.Clear();
            foreach (var provider in await configService.ListProvidersAsync()) Providers.Add(ProviderEditorViewModel.FromResponse(provider));
            suppressSelectionInvariant = false;
            SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == selectedId) ?? Providers.FirstOrDefault();
            OnPropertyChanged(nameof(HasNoSelectedProvider));
            UpdateSummary();
            Status = $"已加载 {Providers.Count} 个 Provider";
            logger?.LogInformation("Provider 页面刷新完成，Provider {ProviderCount}，模型 {ModelCount}，启用 Provider {EnabledProviderCount}，健康 Provider {HealthyProviderCount}", Providers.Count, TotalModelCount, EnabledProviderCount, HealthyProviderCount);
        }
        catch (Exception exception) { suppressSelectionInvariant = false; logger?.LogError(exception, "Provider 页面刷新失败"); Status = $"加载失败：{exception.Message}"; }
    }

    private void NewProvider() { var provider = new ProviderEditorViewModel { DisplayName = "新 Provider", ApiMode = "openai", EndpointFormat = "responses", Enabled = true }; Providers.Add(provider); SelectedProvider = provider; UpdateSummary(); Status = "正在编辑新 Provider"; }

    private async Task SaveProviderAsync()
    {
        var provider = SelectedProvider;
        if (provider is null) return;
        try
        {
            var input = provider.ToInput();
            var response = provider.Id == Guid.Empty ? await configService.CreateProviderAsync(input) : await configService.UpdateProviderAsync(provider.Id, input);
            suppressAutoSave = true;
            provider.ApplyResponse(response);
            suppressAutoSave = false;
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
        catch (Exception exception) { suppressAutoSave = false; logger?.LogError(exception, "Provider 保存失败 {ProviderId}", provider.BusinessId); Status = $"保存失败：{exception.Message}"; }
    }

    private async Task DeleteProviderAsync(ProviderEditorViewModel? provider = null)
    {
        provider ??= SelectedProvider;
        if (provider is null) return;
        try { if (provider.Id != Guid.Empty) await configService.DeleteProviderAsync(provider.Id); Providers.Remove(provider); if (ReferenceEquals(SelectedProvider, provider)) SelectedProvider = Providers.FirstOrDefault(); UpdateSummary(); Status = "Provider 已删除"; }
        catch (Exception exception) { Status = $"删除失败：{exception.Message}"; }
    }

    private void NewModel() { if (SelectedProvider is null || SelectedProvider.Id == Guid.Empty) { Status = "请先保存 Provider，再添加模型"; return; } SelectedModel = new ModelEditorViewModel { ProviderId = SelectedProvider.BusinessId }; Status = "正在编辑新模型"; }

    private async Task SaveModelAsync()
    {
        if (SelectedProvider is null || SelectedModel is null) return;
        try { var input = SelectedModel.ToInput(); var response = SelectedModel.Id == Guid.Empty ? await configService.CreateModelAsync(SelectedProvider.Id, input) : await configService.UpdateModelAsync(SelectedModel.Id, input); var index = SelectedProvider.Models.IndexOf(SelectedModel); var updated = ModelEditorViewModel.FromResponse(response); if (index < 0) SelectedProvider.Models.Add(updated); else SelectedProvider.Models[index] = updated; SelectedModel = updated; Status = "模型已保存"; }
        catch (Exception exception) { Status = $"模型保存失败：{exception.Message}"; }
    }

    private async Task DeleteModelAsync()
    {
        if (SelectedModel is null) return;
        try { if (SelectedModel.Id != Guid.Empty) await configService.DeleteModelAsync(SelectedModel.Id); SelectedProvider?.Models.Remove(SelectedModel); SelectedModel = null; Status = "模型已删除"; }
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
            var names = ExtractModelNames(document.RootElement).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length == 0)
            {
                Status = "模型同步失败 · 响应中没有可用模型";
                toastService.Show(Status, ToastLevel.Error);
                logger?.LogWarning("模型同步失败，响应中没有可用模型 {ProviderId}", provider.BusinessId);
                return;
            }

            var existing = provider.Models.ToDictionary(model => model.ModelId, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var name in names)
            {
                if (existing.ContainsKey(name)) continue;
                var created = await configService.CreateModelAsync(provider.Id, new ModelInput(name, name, null, "unknown", null, provider.ApiMode, 128000, 4096, false, null, null, true, null, false, null, null), token);
                provider.Models.Add(ModelEditorViewModel.FromResponse(created));
                added++;
            }
            Status = $"模型同步完成 · 发现 {names.Length} 个，新增 {added} 个";
            toastService.Show(Status, ToastLevel.Success);
            logger?.LogInformation("模型同步完成 {ProviderId} {DiscoveredCount} {AddedCount}", provider.BusinessId, names.Length, added);
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

    private static string BuildModelListEndpoint(ProviderEditorViewModel provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.ModelListUrl)) return provider.ModelListUrl.TrimEnd('/');
        var baseUrl = provider.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/models";
    }

    private static IEnumerable<string> ExtractModelNames(JsonElement root)
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
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())) yield return id.GetString()!;
            else if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())) yield return name.GetString()!;
            else if (item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(model.GetString())) yield return model.GetString()!;
        }
    }

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
        if (!suppressAutoSave && args.PropertyName is not (nameof(ProviderEditorViewModel.IncompleteHeaderCount) or nameof(ProviderEditorViewModel.HasIncompleteHeaders)))
            ScheduleAutoSave();
    }
    private void ModelsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null) foreach (ModelEditorViewModel model in args.NewItems) model.PropertyChanged += ModelChanged;
        if (args.OldItems is not null) foreach (ModelEditorViewModel model in args.OldItems) model.PropertyChanged -= ModelChanged;
        UpdateSummary();
    }

    private void AttachModel(ModelEditorViewModel? model) { if (model is not null) model.PropertyChanged += ModelChanged; }
    private void DetachModel(ModelEditorViewModel? model) { if (model is not null) model.PropertyChanged -= ModelChanged; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is ModelEditorViewModel model && !ReferenceEquals(SelectedModel, model))
            SelectedModel = model;
        ScheduleModelAutoSave();
    }

    private void UpdateSummary()
    {
        TotalModelCount = Providers.Sum(provider => provider.Models.Count);
        ProtectedKeyCount = Providers.Count(provider => provider.HasApiKey);
        EnabledProviderCount = Providers.Count(provider => provider.Enabled);
        HealthyProviderCount = Providers.Count(provider => provider.Enabled && !string.IsNullOrWhiteSpace(provider.BaseUrl));
    }

    private void ScheduleAutoSave()
    {
        autoSaveCancellation?.Cancel();
        autoSaveCancellation = new CancellationTokenSource();
        var token = autoSaveCancellation.Token;
        _ = SaveProviderAfterDelayAsync(token);
    }

    private void ScheduleModelAutoSave()
    {
        autoSaveCancellation?.Cancel();
        autoSaveCancellation = new CancellationTokenSource();
        var token = autoSaveCancellation.Token;
        _ = SaveModelAfterDelayAsync(token);
    }

    private async Task SaveProviderAfterDelayAsync(CancellationToken token)
    {
        try { await Task.Delay(500, token); if (!token.IsCancellationRequested) await SaveProviderAsync(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task SaveModelAfterDelayAsync(CancellationToken token)
    {
        try { await Task.Delay(500, token); if (!token.IsCancellationRequested) await SaveModelAsync(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

public sealed class ProviderEditorViewModel : NotifyViewModel
{
    public Guid Id { get; set; }
    private string businessId = ""; private string displayName = ""; private string baseUrl = ""; private string modelListUrl = ""; private string apiMode = "openai"; private string endpointFormat = "responses"; private bool enabled; private bool useProxy; private string apiKey = ""; private bool apiKeyEdited; private bool isApiKeyVisible; private string headersJson = "{}";
    public string BusinessId { get => businessId; set => SetProperty(ref businessId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ModelListUrl { get => modelListUrl; set => SetProperty(ref modelListUrl, value); }
    public string ApiMode { get => apiMode; set { if (!SetProperty(ref apiMode, value)) return; OnPropertyChanged(nameof(IsEndpointFormatVisible)); } }
    public string EndpointFormat { get => endpointFormat; set { var normalized = EndpointFormatOption.Normalize(value); if (!SetProperty(ref endpointFormat, normalized)) return; OnPropertyChanged(nameof(SelectedEndpointFormat)); } }
    public IReadOnlyList<EndpointFormatOption> EndpointFormatOptions { get; } = EndpointFormatOption.All;
    public EndpointFormatOption SelectedEndpointFormat { get => EndpointFormatOption.FromValue(EndpointFormat); set { if (value is not null) EndpointFormat = value.Value; } }
    public bool IsEndpointFormatVisible => string.Equals(ApiMode, "openai", StringComparison.OrdinalIgnoreCase);
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public bool UseProxy { get => useProxy; set => SetProperty(ref useProxy, value); } public string ApiKey { get => apiKey; set { if (SetProperty(ref apiKey, value)) apiKeyEdited = true; } } public bool IsApiKeyVisible { get => isApiKeyVisible; private set { if (SetProperty(ref isApiKeyVisible, value)) { OnPropertyChanged(nameof(IsApiKeyHidden)); OnPropertyChanged(nameof(ApiKeyPasswordChar)); OnPropertyChanged(nameof(ApiKeyVisibilityToolTip)); } } } public bool IsApiKeyHidden => !IsApiKeyVisible; public char ApiKeyPasswordChar => IsApiKeyVisible ? '\0' : '●'; public string ApiKeyVisibilityToolTip => IsApiKeyVisible ? "隐藏 API Key" : "显示 API Key"; public string HeadersJson { get => headersJson; private set => SetProperty(ref headersJson, value); } public bool HasApiKey { get; private set; } public string ApiKeyWatermark => HasApiKey ? "已配置 API Key" : "请输入 API Key";
    public ObservableCollection<ModelEditorViewModel> Models { get; } = [];
    public ObservableCollection<HeaderEditorViewModel> Headers { get; } = [];
    public bool HasNoHeaders => Headers.Count == 0;
    public int IncompleteHeaderCount => Headers.Count(IsIncomplete);
    public bool HasIncompleteHeaders => IncompleteHeaderCount > 0;
    public ProviderEditorViewModel() => Headers.CollectionChanged += HeadersChanged;
    public static ProviderEditorViewModel FromResponse(ProviderResponse response) { var value = new ProviderEditorViewModel(); value.ApplyResponse(response); foreach (var model in response.Models) value.Models.Add(ModelEditorViewModel.FromResponse(model)); return value; }
    public ProviderInput ToInput() => new(BusinessId, DisplayName, BaseUrl, ApiMode, Enabled, apiKeyEdited ? ApiKey : null, false, ToHeaderDictionary(), UseProxy, string.IsNullOrWhiteSpace(ModelListUrl) ? null : ModelListUrl, EndpointFormat);
    public void ApplyResponse(ProviderResponse response)
    {
        Id = response.Id; BusinessId = response.BusinessId; DisplayName = response.DisplayName; BaseUrl = response.BaseUrl; ModelListUrl = response.ModelListUrl ?? ""; ApiMode = response.ApiMode; EndpointFormat = response.EndpointFormat; Enabled = response.Enabled; UseProxy = response.UseProxy; HasApiKey = response.HasApiKey; OnPropertyChanged(nameof(ApiKeyWatermark));
        if (response.ApiKey is not null || !response.HasApiKey)
            SetApiKeyFromResponse(response.ApiKey ?? "");
        else
            apiKeyEdited = false;
        SetHeadersFromJson(response.HeadersJson);
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
    private string modelId = ""; private string displayName = ""; private string family = "claude"; private string configId = ""; private string baseUrl = ""; private string apiMode = ""; private int contextLength = 128000; private int maxTokens = 4096; private bool vision; private double? temperature; private double? topP; private bool enabled = true; private string apiKey = ""; private bool clearApiKey; private string headersJson = "{}"; private string extraJson = "{}";
    public string ModelId { get => modelId; set => SetProperty(ref modelId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string Family { get => family; set => SetProperty(ref family, value); } public string ConfigId { get => configId; set => SetProperty(ref configId, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ApiMode { get => apiMode; set => SetProperty(ref apiMode, value); } public int ContextLength { get => contextLength; set => SetProperty(ref contextLength, value); } public int MaxTokens { get => maxTokens; set => SetProperty(ref maxTokens, value); } public bool Vision { get => vision; set => SetProperty(ref vision, value); } public double? Temperature { get => temperature; set => SetProperty(ref temperature, value); } public double? TopP { get => topP; set => SetProperty(ref topP, value); } public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public string ApiKey { get => apiKey; set => SetProperty(ref apiKey, value); } public bool ClearApiKey { get => clearApiKey; set => SetProperty(ref clearApiKey, value); } public string HeadersJson { get => headersJson; set => SetProperty(ref headersJson, value); } public string ExtraJson { get => extraJson; set => SetProperty(ref extraJson, value); } public bool HasApiKey { get; private set; }
    public static ModelEditorViewModel FromResponse(ModelResponse response) => new() { Id = response.Id, ProviderId = response.ProviderId, ModelId = response.ModelId, DisplayName = response.DisplayName, ConfigId = response.ConfigId ?? "", Family = response.Family, BaseUrl = response.BaseUrl ?? "", ApiMode = response.ApiMode ?? "", ContextLength = response.ContextLength, MaxTokens = response.MaxTokens, Vision = response.Vision, Temperature = response.Temperature, TopP = response.TopP, Enabled = response.Enabled, HasApiKey = response.HasApiKey, HeadersJson = response.HeadersJson, ExtraJson = response.ExtraJson };
    public ModelInput ToInput() => new(ModelId, DisplayName, string.IsNullOrWhiteSpace(ConfigId) ? null : ConfigId, Family, string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl, string.IsNullOrWhiteSpace(ApiMode) ? null : ApiMode, ContextLength, MaxTokens, Vision, Temperature, TopP, Enabled, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey, ClearApiKey, ProviderEditorViewModel.ParseDictionary(HeadersJson), JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ExtraJson));
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
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public AsyncCommand(Func<Task> action) => this.action = _ => action();
    public AsyncCommand(Func<object?, Task> action) => this.action = action;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await action(parameter);
}
