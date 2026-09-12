using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX;
using LoomX.Configuration;
using LoomX.Activity;
using LoomX.Localization;
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
    private readonly AssistantViewModel assistantViewModel;
    private readonly UpdateCoordinator updateCoordinator;
    private readonly Action<bool, int, int, string>? applyAppearance;
    private readonly IStringLocalizer<MainWindowViewModel> _loc;
    private object currentView;
    private string currentViewKey = "nav.overview";
    private PlaceholderViewModel? currentError;
    private double selectedNavigationOffset;
    private bool hasActiveNavigationItem;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public object CurrentView => currentView;
    public string PageTitle => currentError?.Title ?? Loc(currentViewKey);
    public string PageDescription => currentError?.Description ?? Loc(currentViewKey + ".description");
    public double SelectedNavigationOffset => selectedNavigationOffset;
    public bool HasActiveNavigationItem => hasActiveNavigationItem;
    public UpdateCoordinator Update => updateCoordinator;

    public MainWindowViewModel(GatewayProcessService gatewayService, ToastService? toastService = null, ILoggerFactory? loggerFactory = null, ConfigSnapshotService? configService = null, Action<bool, int, int, string>? applyAppearance = null, AppDataStore? dataStore = null, IStringLocalizer<MainWindowViewModel>? localizer = null)
    {
        this.gatewayService = gatewayService;
        this.toastService = toastService ?? new ToastService();
        this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var ownedConfigService = configService ?? new ConfigSnapshotService(this.loggerFactory.CreateLogger<ConfigSnapshotService>());
        this.dataStore = dataStore ?? new AppDataStore(ownedConfigService, gatewayService, this.loggerFactory.CreateLogger<AppDataStore>());
        this.applyAppearance = applyAppearance;
        _loc = localizer ?? LocalizerFactory.Create<MainWindowViewModel>();
        consoleViewModel = new ConsoleViewModel(toastService: this.toastService);
        overviewViewModel = new OverviewViewModel(gatewayService, this.dataStore, this.loggerFactory.CreateLogger<MainWindowViewModel>());
        providersViewModel = new ProvidersViewModel(this.dataStore, this.toastService, this.loggerFactory.CreateLogger<ProvidersViewModel>());
        gatewayViewModel = new GatewayViewModel(this.dataStore, this.toastService);
        activityViewModel = new ActivityViewModel(this.dataStore, this.loggerFactory.CreateLogger<ActivityViewModel>());
        assistantViewModel = new AssistantViewModel(gatewayService, this.loggerFactory);
        updateCoordinator = new UpdateCoordinator(this.dataStore, logger: this.loggerFactory.CreateLogger<UpdateCoordinator>());
        settingsViewModel = new SettingsViewModel(dataStore: this.dataStore, logger: this.loggerFactory.CreateLogger<SettingsViewModel>(), toastService: this.toastService, applyAppearance: this.applyAppearance, updateCoordinator: updateCoordinator, localizer: LocalizerFactory.Create<SettingsViewModel>());
        currentView = new PlaceholderViewModel(Loc("app.loading.title"), Loc("app.loading.description"));
        NavigationItems = new([
            new("nav.overview", "M 4,18 L 12,10 L 20,18 L 20,30 L 4,30 Z M 9,30 L 9,20 L 15,20 L 15,30", () => ShowOverview()),
            new("nav.assistant", "M 6,4 L 26,4 L 26,20 L 18,20 L 12,27 L 12,20 L 6,20 Z M 11,10 L 13,10 M 16,10 L 18,10 M 21,10 L 23,10", () => ShowAssistant()),
            new("nav.gateway", "M 16,4 L 16,9 M 16,9 L 8,16 M 16,9 L 24,16 M 8,16 L 8,25 M 24,16 L 24,25 M 4,25 L 12,25 M 20,25 L 28,25", () => ShowGateway()),
            new("nav.providers", "M 7,8 L 25,8 M 7,16 L 25,16 M 7,24 L 25,24 M 4,8 L 4,8 M 4,16 L 4,16 M 4,24 L 4,24", () => ShowProviders()),
            new("nav.activity", "M 7,28 L 7,5 M 8,6 C 13,4 18,8 25,6 L 25,18 C 18,20 13,16 8,18", () => ShowActivity()),
            new("nav.console", "M 5,6 L 27,6 L 27,26 L 5,26 Z M 9,12 L 13,16 L 9,20 M 16,20 L 23,20", () => ShowConsole()),
            new("nav.settings", "M 16,4 L 18,7 L 22,8 L 25,6 L 28,9 L 26,12 L 27,16 L 30,18 L 28,22 L 24,21 L 21,24 L 21,28 L 16,29 L 14,25 L 10,24 L 7,26 L 4,22 L 6,19 L 5,15 L 2,13 L 4,8 L 8,9 L 11,6 L 11,3 Z M 16,12 A 4,4 0 1,0 16,20 A 4,4 0 1,0 16,12 Z", () => ShowSettings())
        ]);
        SetActive("nav.overview");
        this.dataStore.ConfigurationReady += OnConfigurationReady;
        this.dataStore.ConfigurationChanged += OnConfigurationChanged;
        LocaleService.CultureChanged += OnCultureChanged;
        _ = InitializeDataStoreAsync();
    }

    private string Loc(string key) => _loc[key]?.Value ?? key;

    private void SetActive(string titleKey)
    {
        var activeIndex = -1;
        for (var index = 0; index < NavigationItems.Count; index++)
        {
            var isActive = NavigationItems[index].TitleKey == titleKey;
            NavigationItems[index].IsActive = isActive;
            if (isActive) activeIndex = index;
        }

        var hasActiveItem = activeIndex >= 0;
        if (hasActiveNavigationItem != hasActiveItem)
        {
            hasActiveNavigationItem = hasActiveItem;
            OnPropertyChanged(nameof(HasActiveNavigationItem));
        }

        if (hasActiveItem)
        {
            var offset = activeIndex * NavigationItemViewModel.LayoutStep;
            if (!EqualityComparer<double>.Default.Equals(selectedNavigationOffset, offset))
            {
                selectedNavigationOffset = offset;
                OnPropertyChanged(nameof(SelectedNavigationOffset));
            }
        }
    }

    private void ShowView(string key, object view)
    {
        SetActive(key);
        currentViewKey = key;
        currentError = null;
        currentView = view;
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
    }

    private void ShowOverview() => ShowView("nav.overview", overviewViewModel);
    private void ShowAssistant() => ShowView("nav.assistant", assistantViewModel);
    private void ShowProviders() => ShowView("nav.providers", providersViewModel);
    private void ShowGateway() => ShowView("nav.gateway", gatewayViewModel);
    private void ShowConsole() => ShowView("nav.console", consoleViewModel);
    private void ShowActivity() => ShowView("nav.activity", activityViewModel);
    private void ShowSettings() => ShowView("nav.settings", settingsViewModel);
    private void ShowPlaceholder(string title, string description)
    {
        var placeholder = new PlaceholderViewModel(title, description);
        currentError = placeholder;
        currentView = placeholder;
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
    }

    private void OnConfigurationReady(object? sender, EventArgs args)
    {
        void Apply()
        {
            if (dataStore.Settings is { } settings)
            {
                LocaleService.SetCulture(settings.Language);
                applyAppearance?.Invoke(settings.TransparencyEnabled, settings.TransparencyOpacity, settings.BlurAmount, settings.TransparencyAlgorithm);
            }
            ShowOverview();
            updateCoordinator.Start();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    private async Task InitializeDataStoreAsync()
    {
        try { await dataStore.InitializeAsync(); }
        catch (Exception exception)
        {
            var title = Loc("app.loading.failed");
            var description = string.Format(Loc("app.loading.failed.description"), exception.Message);
            if (Dispatcher.UIThread.CheckAccess()) ShowPlaceholder(title, description);
            else Dispatcher.UIThread.Post(() => ShowPlaceholder(title, description));
        }
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs args)
    {
        void Apply()
        {
            if (dataStore.Settings is { } settings)
                applyAppearance?.Invoke(settings.TransparencyEnabled, settings.TransparencyOpacity, settings.BlurAmount, settings.TransparencyAlgorithm);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
    }

    public void Dispose()
    {
        dataStore.ConfigurationReady -= OnConfigurationReady;
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        LocaleService.CultureChanged -= OnCultureChanged;
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
    public const double LayoutStep = 46;
    private readonly string _titleKey;
    private string title;
    private bool isActive;

    public string TitleKey => _titleKey;
    public string Title { get => title; private set => SetProperty(ref title, value); }
    public string Icon { get; }
    public Geometry IconData { get; }
    public bool IsActive { get => isActive; set => SetProperty(ref isActive, value); }
    public ICommand NavigateCommand { get; }

    public NavigationItemViewModel(string titleKey, string icon, Action action)
    {
        _titleKey = titleKey;
        title = ResourceLookup.Resolve(titleKey);
        Icon = icon;
        IconData = Geometry.Parse(icon);
        NavigateCommand = new DelegateCommand(action);
        LocaleService.CultureChanged += OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        Title = ResourceLookup.Resolve(_titleKey);
    }

    public void Dispose()
    {
        LocaleService.CultureChanged -= OnCultureChanged;
    }
}

public sealed class OverviewViewModel : NotifyViewModel, IDisposable
{
    // ActivityQueryService 由 AppDataStore 统一持有，概览只读取数据中心提供的最近活动结果。
    private readonly GatewayProcessService gatewayService;
    private readonly AppDataStore dataStore;
    private readonly ILogger<MainWindowViewModel>? logger;
    private readonly IStringLocalizer<OverviewViewModel> _loc;
    private string gatewayStatus = ResourceLookup.Resolve("overview.gateway.status.not_running");
    private string endpoint = ResourceLookup.Resolve("overview.endpoint.unconfigured");
    private string version = ResourceLookup.Resolve("overview.version.unknown");
    private string lastChecked = ResourceLookup.Resolve("overview.lastchecked.none");
    private int providerCount;
    private int modelCount;
    private int activeRequestCount;
    private int throughput;
    private string p95Latency = "—";
    private string graphStatus = ResourceLookup.Resolve("overview.graph.waiting");
    private string gatewayActionLabel = ResourceLookup.Resolve("overview.gateway.action.start");
    private bool graphLoadFailed;
    private RuntimeGraphSnapshot? graphSnapshot;
    private OverviewEndpointViewModel? selectedEndpoint;
    private bool gatewayToggleInProgress;
    private bool refreshInProgress;
    private readonly Dictionary<string, RequestTelemetryEvent> activeRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> activeEdgeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> requestEdges = new(StringComparer.Ordinal);
    private readonly List<RequestTelemetryEvent> completionWindow = [];


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
    public string RecentRequestsCountLabel => LocFormat("overview.recent.count", RecentRequests.Count);
    public RuntimeGraphSnapshot? GraphSnapshot { get => graphSnapshot; private set => SetProperty(ref graphSnapshot, value); }
    public OverviewEndpointViewModel? SelectedEndpoint { get => selectedEndpoint; private set => SetProperty(ref selectedEndpoint, value); }
    public ObservableCollection<OverviewEndpointViewModel> Endpoints { get; } = [];
    public ObservableCollection<OverviewComboViewModel> Combos { get; } = [];
    public ObservableCollection<OverviewProviderViewModel> Providers { get; } = [];
    public ObservableCollection<OverviewModelViewModel> Models { get; } = [];
    public ObservableCollection<OverviewRecentRequestViewModel> RecentRequests { get; } = [];
    public bool RecentRequestsEmpty => RecentRequests.Count == 0;
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleGatewayCommand { get; }

    public OverviewViewModel(GatewayProcessService gatewayService, AppDataStore dataStore, ILogger<MainWindowViewModel>? logger = null, IStringLocalizer<OverviewViewModel>? localizer = null)
    {
        this.gatewayService = gatewayService;
        this.dataStore = dataStore;
        this.logger = logger;
        _loc = localizer ?? LocalizerFactory.Create<OverviewViewModel>();
        StartCommand = new AsyncCommand(StartAsync);
        StopCommand = new AsyncCommand(StopAsync);
        ToggleGatewayCommand = new AsyncCommand(ToggleGatewayAsync, CanToggleGateway);
        gatewayService.StateChanged += OnGatewayStateChanged;
        gatewayService.TelemetryPublished += OnTelemetryPublished;
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        LocaleService.CultureChanged += OnCultureChanged;
        _ = RefreshAsync();
    }

    public OverviewViewModel(GatewayProcessService gatewayService, ConfigSnapshotService configService, ILogger<MainWindowViewModel>? logger = null)
        : this(gatewayService, new AppDataStore(configService, gatewayService), logger) { }

    private string Loc(string key) => _loc[key]?.Value ?? key;
    private string LocFormat(string key, params object[] args)
    {
        var value = Loc(key);
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        void Apply()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            RefreshLocalizedStatuses();
            foreach (var endpoint in Endpoints)
            {
                endpoint.RefreshLocalization();
                foreach (var route in endpoint.Routes) route.RefreshLocalization();
            }
            foreach (var request in RecentRequests) request.RefreshLocalization();
            OnPropertyChanged(nameof(RecentRequestsCountLabel));
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    private void RefreshLocalizedStatuses()
    {
        GatewayStatus = gatewayService.State switch
        {
            GatewayState.Running => Loc("overview.gateway.status.running"),
            GatewayState.Starting => Loc("overview.gateway.status.starting"),
            GatewayState.Stopping => Loc("overview.gateway.status.stopping"),
            GatewayState.Failed => LocFormat("overview.gateway.status.failed", gatewayService.Error ?? ""),
            _ => Loc("overview.gateway.status.not_running")
        };
        LastChecked = gatewayService.LastCheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? Loc("overview.lastchecked.none");
        Version = gatewayService.State == GatewayState.Running ? Loc("overview.version.online") : Loc("overview.version.disconnected");
        GraphStatus = graphLoadFailed
            ? Loc("overview.graph.failed")
            : gatewayService.State == GatewayState.Running ? Loc("overview.graph.connected") : Loc("overview.graph.waiting");
        UpdateGatewayControls();
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
            ? gatewayService.State == GatewayState.Running ? Loc("overview.gateway.action.stopping") : Loc("overview.gateway.action.starting")
            : gatewayService.State == GatewayState.Running ? Loc("overview.gateway.action.stop") : gatewayService.State switch
            {
                GatewayState.Starting => Loc("overview.gateway.action.starting"),
                GatewayState.Stopping => Loc("overview.gateway.action.stopping"),
                _ => Loc("overview.gateway.action.start")
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
            ApplyConfigSnapshot();
            await RefreshRecentRequestsAsync();
            logger?.LogInformation("概览刷新完成 {ProviderCount} 个 Provider、{ModelCount} 个模型、{EndpointCount} 个 Endpoint、{RouteCount} 条路由，网关状态 {GatewayState}，配置库 {DatabasePath}，进程 {ProcessId}", ProviderCount, ModelCount, Endpoints.Count, Endpoints.Sum(item => item.Routes.Count), gatewayService.State, AppDataPaths.DatabasePath, Environment.ProcessId);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "概览刷新失败");
            graphLoadFailed = true;
            GraphStatus = Loc("overview.graph.failed");
        }
        finally
        {
            refreshInProgress = false;
        }
    }

    /// <summary>直接应用数据中心当前快照（统计与拓扑），不再触发 dataStore.RefreshAsync，避免配置事件回声。</summary>
    private void ApplyConfigSnapshot()
    {
        graphLoadFailed = false;
        var config = dataStore.CurrentConfig;
        Endpoint = config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
        ProviderCount = config.Providers.Count;
        ModelCount = config.Models.Count;
        BuildTopology(config);
        GatewayStatus = gatewayService.State switch
        {
            GatewayState.Running => Loc("overview.gateway.status.running"),
            GatewayState.Starting => Loc("overview.gateway.status.starting"),
            GatewayState.Stopping => Loc("overview.gateway.status.stopping"),
            GatewayState.Failed => LocFormat("overview.gateway.status.failed", gatewayService.Error ?? ""),
            _ => Loc("overview.gateway.status.not_running")
        };
        LastChecked = gatewayService.LastCheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? Loc("overview.lastchecked.none");
        Version = gatewayService.State == GatewayState.Running ? Loc("overview.version.online") : Loc("overview.version.disconnected");
        GraphStatus = gatewayService.State == GatewayState.Running ? Loc("overview.graph.connected") : Loc("overview.graph.waiting");
        UpdateGatewayControls();
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
        var comboById = config.GatewayCombos.ToDictionary(item => item.Id);
        foreach (var combo in config.GatewayCombos.OrderBy(item => item.SortOrder))
            Combos.Add(new OverviewComboViewModel(RuntimeGraphIds.Combo(combo.Id), "global", combo.Name, combo.Enabled));
        foreach (var endpoint in config.GatewayEndpoints.OrderBy(item => item.Key))
        {
            var endpointVm = new OverviewEndpointViewModel(endpoint.Key, EndpointLabel(endpoint.Key), endpoint.PublicPath, endpoint.Enabled);
            endpointVm.GraphSnapshot = GraphSnapshot.ForEndpoint(endpoint.Key);
            foreach (var binding in endpoint.ComboBindings.OrderBy(item => item.SortOrder))
            {
                if (!comboById.TryGetValue(binding.ComboId, out var combo)) continue;
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
    }

    public void SelectEndpoint(OverviewEndpointViewModel? endpoint)
    {
        if (endpoint is null || !Endpoints.Contains(endpoint)) return;
        SelectedEndpoint = endpoint;
        foreach (var item in Endpoints) item.IsGraphVisible = ReferenceEquals(item, endpoint);
    }

    private string LoadEndpoint()
    {
        var config = dataStore.CurrentConfig;
        return config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs args)
    {
        if (refreshInProgress) return;
        if (args.Source == ConfigurationChangeSource.LocalSave)
        {
            // 本机编辑保存已由数据中心更新快照，直接应用即可，避免再次全量刷新形成事件回声。
            if (Dispatcher.UIThread.CheckAccess()) ApplyConfigSnapshot();
            else Dispatcher.UIThread.Post(ApplyConfigSnapshot);
            return;
        }
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => { if (!refreshInProgress) _ = RefreshAsync(); });
    }

    private void OnGatewayStateChanged(object? sender, EventArgs args)
    {
        if (refreshInProgress) return;
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => { if (!refreshInProgress) _ = RefreshAsync(); });
    }

    private void OnTelemetryPublished(object? sender, RequestTelemetryEvent telemetryEvent) => Dispatcher.UIThread.Post(() => ApplyTelemetry(telemetryEvent));

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
        P95Latency = completionWindow.Count == 0 ? "—" : LocFormat("overview.latency.format", completionWindow.Select(item => item.ElapsedMs).OrderBy(item => item).ElementAt(Math.Max(0, (int)Math.Ceiling(completionWindow.Count * .95) - 1)));
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
        LocaleService.CultureChanged -= OnCultureChanged;
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
    public bool IsGraphVisible { get => isGraphVisible; set => SetProperty(ref isGraphVisible, value); }
    public RuntimeGraphSnapshot? GraphSnapshot { get => graphSnapshot; internal set => SetProperty(ref graphSnapshot, value); }
    public int ActiveCount => Routes.Count(item => item.IsActive);
    public string Status => !Enabled ? ResourceLookup.Resolve("overview.endpoint.status.disabled") : Routes.Count == 0 ? ResourceLookup.Resolve("overview.endpoint.status.no_routes") : ActiveCount > 0 ? ResourceLookup.Resolve("overview.endpoint.status.active") : ResourceLookup.Resolve("overview.endpoint.status.ready");
    internal void RefreshLocalization() => OnPropertyChanged(nameof(Status));
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
    public string Status => IsActive ? ResourceLookup.Resolve("overview.route.status.active") : ResourceLookup.Resolve("overview.route.status.idle");
    internal void RefreshLocalization() => OnPropertyChanged(nameof(Status));
    public OverviewRouteViewModel(string alias, string modelName, string modelId, string providerId) => (Alias, ModelName, ModelId, ProviderId) = (alias, modelName, modelId, providerId);
}

public sealed class OverviewModelViewModel
{
    public string DisplayName { get; }
    public string ModelId { get; }
    public string ProviderId { get; }
    public OverviewModelViewModel(string displayName, string modelId, string providerId) => (DisplayName, ModelId, ProviderId) = (displayName, modelId, providerId);
}

public sealed class OverviewRecentRequestViewModel : NotifyViewModel
{
    public string RequestId { get; }
    public DateTimeOffset CreatedAt { get; }
    public string Time { get; }
    public string Endpoint { get; }
    public string Model { get; }
    private readonly int statusCode;
    public string Status => statusCode is >= 200 and < 300 ? ResourceLookup.Resolve("overview.request.status.success") : ResourceLookup.Resolve("overview.request.status.failed");
    public long ElapsedMs { get; }
    public string Latency => $"{ElapsedMs} ms";
    private OverviewRecentRequestViewModel(string requestId, DateTimeOffset createdAt, string endpoint, string model, int statusCode, long elapsedMs)
    {
        RequestId = requestId;
        CreatedAt = createdAt;
        Time = createdAt.ToLocalTime().ToString("HH:mm:ss");
        Endpoint = endpoint;
        Model = model;
        this.statusCode = statusCode;
        ElapsedMs = elapsedMs;
    }

    private OverviewRecentRequestViewModel(RequestTelemetryEvent item)
        : this(item.RequestId, item.Timestamp, item.EndpointKey, item.ModelId ?? item.ModelAlias ?? ResourceLookup.Resolve("overview.request.model.unknown"), item.StatusCode ?? 0, item.ElapsedMs) { }

    private OverviewRecentRequestViewModel(ActivityEventRecord item)
        : this(item.RequestId, item.CreatedAt, item.Protocol, string.IsNullOrWhiteSpace(item.ModelId) ? ResourceLookup.Resolve("overview.request.model.unknown") : item.ModelId, item.StatusCode, item.ElapsedMs) { }

    public static OverviewRecentRequestViewModel From(RequestTelemetryEvent item) => new(item);
    public static OverviewRecentRequestViewModel From(ActivityEventRecord item) => new(item);

    internal void RefreshLocalization() => OnPropertyChanged(nameof(Status));

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
    private readonly IStringLocalizer<ProvidersViewModel> _loc;
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly IProviderHealthService healthService;
    private ProviderEditorViewModel? selectedProvider;
    private ModelEditorViewModel? selectedModel;
    private string status = "";
    private string connectionStatus = ResourceLookup.Resolve("providers.connection.status.pending");
    private string? statusKey;
    private object[] statusArguments = [];
    private string connectionStatusKey = "providers.connection.status.pending";
    private object[] connectionStatusArguments = [];
    private int totalModelCount;
    private int protectedKeyCount;
    private int healthyProviderCount;
    private int enabledProviderCount;
    private int checkingProviderCount;
    private int pendingProviderCount;
    private int warningProviderCount;
    private int errorProviderCount;
    private bool isProviderHealthChecking;
    private int activeTabIndex;
    private bool isCliMenuOpen;
    private CancellationTokenSource? connectionCancellation;
    private CancellationTokenSource? healthVerificationCancellation;
    private CancellationTokenSource? modelSyncCancellation;
    private DispatcherTimer? modelSyncAnimationTimer;
    private bool isModelSyncing;
    private double syncIconAngle;
    private string providerSearchQuery = "";
    private string modelSearchQuery = "";
    private readonly SemaphoreSlim providerSaveLock = new(1, 1);
    private readonly SemaphoreSlim modelSaveLock = new(1, 1);
    private ModelEditorViewModel? draggingModel;
    private ModelEditorViewModel? modelDragPlaceholder;
    private ProviderEditorViewModel? modelDragOwnerProvider;
    private int draggingModelOriginIndex = -1;
    private bool suppressConfigurationRefresh;
    private bool suppressSelectionInvariant;
    private readonly object refreshSync = new();
    private bool refreshRequested;
    private Task? refreshTask;
    private int lastIncompleteHeaderWarningCount;
    public ObservableCollection<ProviderEditorViewModel> Providers { get; } = [];
    public IReadOnlyList<string> ProviderTypeOptions { get; } = ["openai", "anthropic", "ollama"];
    public ProviderEditorViewModel? SelectedProvider
    {
        get => selectedProvider;
        set
        {
            if (value is null && !suppressSelectionInvariant)
                value = FilteredProviders.FirstOrDefault();
            if (ReferenceEquals(selectedProvider, value)) return;
            DetachProvider(selectedProvider);
            SetProperty(ref selectedProvider, value);
            AttachProvider(selectedProvider);
            SelectedModel = null;
            OnPropertyChanged(nameof(HasSelectedProvider));
            OnPropertyChanged(nameof(HasNoSelectedProvider));
            OnPropertyChanged(nameof(FilteredModels));
            OnPropertyChanged(nameof(HasFilteredModels));
            OnPropertyChanged(nameof(HasNoFilteredModels));
            OnPropertyChanged(nameof(EnabledModelCount));
            OnPropertyChanged(nameof(AllModelsEnabled));
            OnPropertyChanged(nameof(EnabledModelSummary));
        }
    }
    public bool HasSelectedProvider => SelectedProvider is not null;
    public bool HasNoSelectedProvider => Providers.Count == 0;

    public string ProviderSearchQuery
    {
        get => providerSearchQuery;
        set
        {
            if (!SetProperty(ref providerSearchQuery, value ?? "")) return;
            OnPropertyChanged(nameof(HasProviderSearchQuery));
            OnPropertyChanged(nameof(FilteredProviders));
        }
    }
    public bool HasProviderSearchQuery => !string.IsNullOrWhiteSpace(ProviderSearchQuery);
    public IReadOnlyList<ProviderEditorViewModel> FilteredProviders
    {
        get
        {
            var query = ProviderSearchQuery.Trim();
            return string.IsNullOrEmpty(query)
                ? Providers.ToArray()
                : Providers.Where(provider => MatchesProviderSearch(provider, query)).ToArray();
        }
    }

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
    public string ModelSearchQuery
    {
        get => modelSearchQuery;
        set
        {
            if (!SetProperty(ref modelSearchQuery, value ?? "")) return;
            OnPropertyChanged(nameof(HasModelSearchQuery));
            OnPropertyChanged(nameof(FilteredModels));
            OnPropertyChanged(nameof(HasFilteredModels));
            OnPropertyChanged(nameof(HasNoFilteredModels));
        }
    }
    public bool HasModelSearchQuery => !string.IsNullOrWhiteSpace(ModelSearchQuery);
    public IReadOnlyList<ModelEditorViewModel> FilteredModels
    {
        get
        {
            if (SelectedProvider is null) return [];
            var query = ModelSearchQuery.Trim();
            return string.IsNullOrEmpty(query)
                ? SelectedProvider.Models.ToArray()
                : SelectedProvider.Models.Where(model => model.IsRealModel && model.ModelId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }
    public bool HasFilteredModels => FilteredModels.Count > 0;
    public bool HasNoFilteredModels => SelectedProvider?.HasModels == true && !HasFilteredModels;
    public ModelEditorViewModel? DraggingModel { get => draggingModel; private set { if (ReferenceEquals(draggingModel, value)) return; draggingModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsModelDragActive)); OnPropertyChanged(nameof(EnabledModelCount)); OnPropertyChanged(nameof(EnabledModelSummary)); OnPropertyChanged(nameof(AllModelsEnabled)); } }
    public bool IsModelDragActive => DraggingModel is not null;
    public int EnabledModelCount => GetSelectedProviderModelsForSummary().Count(model => model.Enabled);
    public string EnabledModelSummary => LocFormat("providers.model.summary", EnabledModelCount, GetSelectedProviderModelsForSummary().Count);
    public bool AllModelsEnabled
    {
        get
        {
            var models = GetSelectedProviderModelsForSummary();
            return models.Count > 0 && models.All(model => model.Enabled);
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
    public int CheckingProviderCount { get => checkingProviderCount; private set => SetProperty(ref checkingProviderCount, value); }
    public int PendingProviderCount { get => pendingProviderCount; private set => SetProperty(ref pendingProviderCount, value); }
    public int WarningProviderCount { get => warningProviderCount; private set => SetProperty(ref warningProviderCount, value); }
    public int ErrorProviderCount { get => errorProviderCount; private set => SetProperty(ref errorProviderCount, value); }
    public bool IsProviderHealthChecking
    {
        get => isProviderHealthChecking;
        private set
        {
            if (!SetProperty(ref isProviderHealthChecking, value)) return;
            OnPropertyChanged(nameof(IsProviderHealthIdle));
            OnPropertyChanged(nameof(ProviderHealthSummary));
            if (VerifyAllProvidersCommand is AsyncCommand command) command.RaiseCanExecuteChanged();
        }
    }
    public bool IsProviderHealthIdle => !IsProviderHealthChecking && CheckingProviderCount == 0;
    public string ProviderHealthSummary
    {
        get
        {
            if (EnabledProviderCount == 0) return Loc("providers.health.summary.none");
            if (CheckingProviderCount > 0) return LocFormat("providers.health.summary.checking", CheckingProviderCount, EnabledProviderCount);
            var parts = new List<string>();
            if (ErrorProviderCount > 0) parts.Add(LocFormat("providers.health.summary.error", ErrorProviderCount));
            if (WarningProviderCount > 0) parts.Add(LocFormat("providers.health.summary.warning", WarningProviderCount));
            if (PendingProviderCount > 0) parts.Add(LocFormat("providers.health.summary.pending", PendingProviderCount));
            if (parts.Count == 0) parts.Add(LocFormat("providers.health.summary.healthy", HealthyProviderCount));
            return string.Join(Loc("providers.health.summary.separator"), parts);
        }
    }
    public int ActiveTabIndex { get => activeTabIndex; set => SetProperty(ref activeTabIndex, value); }
    /// <summary>CLI 身份菜单是否展开（供 View 层 Popup 的 IsOpen 绑定）。</summary>
    public bool IsCliMenuOpen { get => isCliMenuOpen; set => SetProperty(ref isCliMenuOpen, value); }
    public ICommand RefreshCommand { get; }
    public ICommand NewProviderCommand { get; }
    public ICommand SaveProviderCommand { get; }
    public ICommand DeleteProviderCommand { get; }
    public ICommand NewModelCommand { get; }
    public ICommand SaveModelCommand { get; }
    public ICommand DeleteModelCommand { get; }
    public ICommand ToggleAllModelsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand VerifyAllProvidersCommand { get; }
    public ICommand SyncModelsCommand { get; }

    private IReadOnlyList<ModelEditorViewModel> GetSelectedProviderModelsForSummary()
    {
        if (SelectedProvider is null) return [];

        var models = SelectedProvider.Models.Where(model => model.IsRealModel).ToList();
        if (ReferenceEquals(modelDragOwnerProvider, SelectedProvider) && DraggingModel is not null && !models.Contains(DraggingModel))
            models.Add(DraggingModel);
        return models;
    }

    public ProvidersViewModel(AppDataStore dataStore, ToastService? toastService = null, ILogger<ProvidersViewModel>? logger = null, IStringLocalizer<ProvidersViewModel>? localizer = null, IProviderHealthService? healthService = null)
    {
        this.dataStore = dataStore;
        this.toastService = toastService ?? new ToastService();
        this.logger = logger;
        _loc = localizer ?? LocalizerFactory.Create<ProvidersViewModel>();
        this.healthService = healthService ?? new ProviderHealthService(httpClient);
        Providers.CollectionChanged += ProvidersChanged;
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        LocaleService.CultureChanged += OnCultureChanged;
        RefreshCommand = new AsyncCommand(RefreshAsync); NewProviderCommand = new DelegateCommand(NewProvider); SaveProviderCommand = new AsyncCommand(SaveProviderAsync); DeleteProviderCommand = new AsyncCommand(parameter => DeleteProviderAsync(parameter as ProviderEditorViewModel)); NewModelCommand = new DelegateCommand(NewModel); SaveModelCommand = new AsyncCommand(SaveModelAsync); DeleteModelCommand = new AsyncCommand(parameter => DeleteModelAsync(parameter as ModelEditorViewModel)); ToggleAllModelsCommand = new AsyncCommand(ToggleAllModelsAsync); TestConnectionCommand = new AsyncCommand(TestConnectionAsync); VerifyAllProvidersCommand = new AsyncCommand(VerifyAllProvidersAsync, () => IsProviderHealthIdle); SyncModelsCommand = new AsyncCommand(SyncModelsAsync); _ = RefreshAsync();
    }

    public ProvidersViewModel(ConfigSnapshotService configService, ToastService? toastService = null, ILogger<ProvidersViewModel>? logger = null, IStringLocalizer<ProvidersViewModel>? localizer = null)
        : this(new AppDataStore(configService, new GatewayProcessService()), toastService, logger, localizer) { }

    private string Loc(string key) => _loc[key]?.Value ?? key;
    private string LocFormat(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Loc(key), args);
    private void SetStatus(string key, params object[] args)
    {
        statusKey = key;
        statusArguments = args;
        Status = LocFormat(key, args);
    }

    private void SetConnectionStatus(string key, params object[] args)
    {
        connectionStatusKey = key;
        connectionStatusArguments = args;
        ConnectionStatus = LocFormat(key, args);
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        if (statusKey is not null) Status = LocFormat(statusKey, statusArguments);
        ConnectionStatus = LocFormat(connectionStatusKey, connectionStatusArguments);
        OnPropertyChanged(nameof(ProviderHealthSummary));
        OnPropertyChanged(nameof(EnabledModelSummary));
        OnPropertyChanged(nameof(ConnectionStatus));
        foreach (var provider in Providers)
        {
            provider.RefreshLocalization();
            foreach (var model in provider.Models) model.RefreshLocalization();
        }
    }

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
            await dataStore.InitializeAsync();
            suppressSelectionInvariant = true;
            MergeProviders(dataStore.Providers);
            suppressSelectionInvariant = false;
            var selectedId = SelectedProvider?.Id;
            SelectedProvider = selectedId is { } id && Providers.Any(provider => provider.Id == id)
                ? Providers.First(provider => provider.Id == id)
                : Providers.FirstOrDefault();
            OnPropertyChanged(nameof(HasNoSelectedProvider));
            UpdateSummary();
            SetStatus("providers.status.loaded", Providers.Count);
            logger?.LogInformation("Provider 页面刷新完成，Provider {ProviderCount}，模型 {ModelCount}，启用 Provider {EnabledProviderCount}，健康 Provider {HealthyProviderCount}", Providers.Count, TotalModelCount, EnabledProviderCount, HealthyProviderCount);
        }
        catch (Exception exception) { suppressSelectionInvariant = false; logger?.LogError(exception, "Provider 页面刷新失败"); SetStatus("providers.loading.failure", exception.Message); }
    }

    /// <summary>按 Id 原地合并数据库快照：保留实例身份与用户未保存的编辑，只增删真正变化的行，避免整表重建导致输入丢失与焦点销毁。</summary>
    private void MergeProviders(IReadOnlyList<ProviderResponse> responses)
    {
        var responsesById = new Dictionary<Guid, ProviderResponse>();
        foreach (var response in responses)
            if (response.Id != Guid.Empty) responsesById[response.Id] = response;

        for (var index = Providers.Count - 1; index >= 0; index--)
        {
            var provider = Providers[index];
            // 新建未落库的 Provider 原样保留
            if (provider.Id == Guid.Empty) continue;
            if (responsesById.Remove(provider.Id, out var response))
            {
                if (!provider.HasUnsavedChanges) provider.ApplyResponse(response);
            }
            else
            {
                DetachProvider(provider);
                Providers.RemoveAt(index);
            }
        }

        foreach (var response in responsesById.Values)
            Providers.Add(ProviderEditorViewModel.FromResponse(response));
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs args)
    {
        // 本机编辑保存不重建列表：编辑中的实例与选中态已在本地维护，原地保留。
        if (args.Source == ConfigurationChangeSource.LocalSave) return;
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }

    public void Dispose()
    {
        Providers.CollectionChanged -= ProvidersChanged;
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        // 尽力把待存的编辑在退出前落库；保存锁由未完成的异步操作自行释放。
        _ = SavePendingChangesAsync();
        connectionCancellation?.Cancel();
        healthVerificationCancellation?.Cancel();
        healthVerificationCancellation?.Dispose();
        modelSyncCancellation?.Cancel();
        modelSyncAnimationTimer?.Stop();
        modelSyncAnimationTimer = null;
        httpClient.Dispose();
        LocaleService.CultureChanged -= OnCultureChanged;
    }

    private void NewProvider() { var provider = new ProviderEditorViewModel { DisplayName = Loc("providers.edit.new.displayname"), ApiMode = "openai", EndpointFormat = "responses", Enabled = true }; Providers.Add(provider); SelectedProvider = provider; UpdateSummary(); SetStatus("providers.status.edit.new"); }

    private async Task SavePendingChangesAsync()
    {
        foreach (var provider in Providers.ToArray())
            if (provider.HasUnsavedChanges) await SaveProviderAsync(provider);
        foreach (var provider in Providers.ToArray())
            foreach (var model in provider.Models.ToArray())
                if (model.HasUnsavedChanges) await SaveModelAsync(provider, model);
    }

    private Task SaveProviderAsync() => SaveProviderAsync(SelectedProvider);

    private async Task SaveProviderAsync(ProviderEditorViewModel? target)
    {
        var provider = target;
        if (provider is null) return;
        await providerSaveLock.WaitAsync();
        try
        {
            if (!provider.HasUnsavedChanges) return;
            var editRevision = provider.EditRevision;
            suppressConfigurationRefresh = true;
            try
            {
                var input = provider.ToInput();
                var response = provider.Id == Guid.Empty ? await dataStore.CreateProviderAsync(input) : await dataStore.UpdateProviderAsync(provider.Id, input);
                provider.ApplySaveResult(response, editRevision);
                UpdateSummary();
                if (provider.IncompleteHeaderCount > 0)
                {
                    SetStatus("providers.save.success.pendingHeaders", provider.IncompleteHeaderCount);
                    if (provider.IncompleteHeaderCount > lastIncompleteHeaderWarningCount)
                        toastService.Show(LocFormat("providers.headers.pending", provider.IncompleteHeaderCount), ToastLevel.Warning);
                    lastIncompleteHeaderWarningCount = provider.IncompleteHeaderCount;
                    logger?.LogWarning("Provider 保存完成但存在未完成请求头 {ProviderId} {IncompleteHeaderCount}", provider.BusinessId, provider.IncompleteHeaderCount);
                }
                else
                {
                    lastIncompleteHeaderWarningCount = 0;
                    SetStatus("providers.save.success");
                    logger?.LogInformation("Provider 保存完成 {ProviderId}", provider.BusinessId);
                }
            }
            catch (Exception exception) { logger?.LogError(exception, "Provider 保存失败 {ProviderId}", provider.BusinessId); SetStatus("providers.save.failure", exception.Message); }
            finally
            {
                suppressConfigurationRefresh = false;
            }
        }
        finally { providerSaveLock.Release(); }
    }

    private async Task DeleteProviderAsync(ProviderEditorViewModel? provider = null)
    {
        provider ??= SelectedProvider;
        if (provider is null) return;
        try { if (provider.Id != Guid.Empty) await dataStore.DeleteProviderAsync(provider.Id); Providers.Remove(provider); if (ReferenceEquals(SelectedProvider, provider)) SelectedProvider = Providers.FirstOrDefault(); UpdateSummary(); SetStatus("providers.delete.success"); }
        catch (Exception exception) { SetStatus("providers.delete.failure", exception.Message); }
    }

    private void NewModel() { if (SelectedProvider is null || SelectedProvider.Id == Guid.Empty) { SetStatus("providers.sync.beforeProvider"); return; } SelectedModel = new ModelEditorViewModel { ProviderId = SelectedProvider.BusinessId }; SetStatus("providers.model.edit.new"); }

    private Task SaveModelAsync() => SaveModelAsync(SelectedProvider, SelectedModel);

    private async Task SaveModelAsync(ProviderEditorViewModel? provider, ModelEditorViewModel? model)
    {
        if (provider is null || model is null) return;
        await modelSaveLock.WaitAsync();
        try
        {
            if (!model.HasUnsavedChanges) return;
            var editRevision = model.EditRevision;
            suppressConfigurationRefresh = true;
            try
            {
                var input = model.ToInput();
                var response = model.Id == Guid.Empty ? await dataStore.CreateModelAsync(provider.Id, input) : await dataStore.UpdateModelAsync(model.Id, input);
                if (!provider.Models.Contains(model)) provider.Models.Add(model);
                model.ApplySaveResult(response, editRevision);
                SetStatus("providers.model.save.success");
            }
            catch (Exception exception) { SetStatus("providers.model.save.failure", exception.Message); }
            finally { suppressConfigurationRefresh = false; }
        }
        finally { modelSaveLock.Release(); }
    }

    private Task DeleteModelAsync() => DeleteModelAsync(SelectedModel);

    private async Task DeleteModelAsync(ModelEditorViewModel? model)
    {
        if (model is null) return;
        try { if (model.Id != Guid.Empty) await dataStore.DeleteModelAsync(model.Id); SelectedProvider?.Models.Remove(model); if (ReferenceEquals(SelectedModel, model)) SelectedModel = null; SetStatus("providers.model.delete.success"); }
        catch (Exception exception) { SetStatus("providers.model.delete.failure", exception.Message); }
    }

    private async Task TestConnectionAsync()
    {
        var provider = SelectedProvider;
        if (provider is null) return;

        connectionCancellation?.Cancel();
        connectionCancellation?.Dispose();
        var requestCancellation = new CancellationTokenSource();
        connectionCancellation = requestCancellation;
        try
        {
            await VerifyProviderAsync(provider, requestCancellation.Token);
            toastService.Show(
                provider.IsHealthPassed ? Loc("providers.test.toast.success") : Loc("providers.test.toast.failure"),
                provider.IsHealthPassed ? ToastLevel.Success : ToastLevel.Error);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(connectionCancellation, requestCancellation)) connectionCancellation = null;
            requestCancellation.Dispose();
        }
    }

    private async Task VerifyAllProvidersAsync()
    {
        var targets = Providers.Where(provider => provider.Enabled).ToArray();
        if (targets.Length == 0)
        {
            SetStatus("providers.health.summary.none");
            return;
        }

        healthVerificationCancellation?.Cancel();
        healthVerificationCancellation?.Dispose();
        healthVerificationCancellation = new CancellationTokenSource();
        var token = healthVerificationCancellation.Token;
        IsProviderHealthChecking = true;
        logger?.LogInformation("Provider 批量验证开始 {ProviderCount}", targets.Length);
        using var gate = new SemaphoreSlim(3, 3);
        try
        {
            await Task.WhenAll(targets.Select(async provider =>
            {
                await gate.WaitAsync(token);
                try { await VerifyProviderAsync(provider, token); }
                finally { gate.Release(); }
            }));
            toastService.Show(ProviderHealthSummary, ErrorProviderCount == 0 ? ToastLevel.Success : ToastLevel.Warning);
            logger?.LogInformation("Provider 批量验证完成 {ProviderCount} {HealthyCount} {WarningCount} {ErrorCount} {PendingCount}", targets.Length, HealthyProviderCount, WarningProviderCount, ErrorProviderCount, PendingProviderCount);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            IsProviderHealthChecking = false;
            healthVerificationCancellation = null;
        }
    }

    private async Task VerifyProviderAsync(ProviderEditorViewModel provider, CancellationToken cancellationToken)
    {
        provider.ApplyHealthResult(new ProviderHealthResult(ProviderHealthState.Checking));
        UpdateSummary();
        try
        {
            var result = await healthService.CheckAsync(provider.ToHealthCheckRequest(), cancellationToken);
            if (provider.HealthState != ProviderHealthState.Checking)
            {
                UpdateSummary();
                return;
            }
            if (!provider.Enabled) result = new ProviderHealthResult(ProviderHealthState.Disabled);
            provider.ApplyHealthResult(result);
            ConnectionStatus = provider.HealthDetailText;
            UpdateSummary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            provider.ResetHealthForCancellation();
            UpdateSummary();
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Provider 验证流程失败 {ProviderId}", provider.BusinessId);
            provider.ApplyHealthResult(new ProviderHealthResult(ProviderHealthState.Unavailable, ProviderHealthFailureKind.Unavailable, FailureCode: "unexpected"));
            ConnectionStatus = provider.HealthDetailText;
            UpdateSummary();
        }
    }

    private async Task SyncModelsAsync()
    {
        var provider = SelectedProvider;
        if (provider is null) return;
        if (provider.Id == Guid.Empty)
        {
            SetStatus("providers.sync.beforeModel");
            toastService.Show(Status, ToastLevel.Warning);
            return;
        }

        if (!Uri.TryCreate(BuildModelListEndpoint(provider), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            SetStatus("providers.sync.invalidUrl");
            toastService.Show(Status, ToastLevel.Warning);
            return;
        }

        modelSyncCancellation?.Cancel();
        var requestCancellation = new CancellationTokenSource();
        modelSyncCancellation = requestCancellation;
        var token = requestCancellation.Token;
        // 同步会按响应重建模型行，先落库待存编辑
        await SavePendingChangesAsync();
        SetStatus("providers.sync.running");
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
                SetStatus("providers.sync.failure.http", (int)response.StatusCode, response.ReasonPhrase ?? "");
                toastService.Show(LocFormat("providers.sync.failure.toast.http", (int)response.StatusCode), ToastLevel.Error);
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
                SetStatus("providers.sync.failure.empty");
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
            SetStatus("providers.sync.success", descriptors.Length, added, updated);
            toastService.Show(Status, ToastLevel.Success);
            logger?.LogInformation("模型同步完成 {ProviderId} {DiscoveredCount} {AddedCount} {UpdatedCount}", provider.BusinessId, descriptors.Length, added, updated);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (JsonException exception)
        {
            SetStatus("providers.sync.failure.parse");
            toastService.Show(Status, ToastLevel.Error);
            logger?.LogWarning(exception, "模型同步响应格式无法解析 {ProviderId}", provider.BusinessId);
        }
        catch (Exception exception)
        {
            SetStatus("providers.sync.failure.exception", exception.Message);
            toastService.Show(Loc("providers.sync.failure.toast.exception"), ToastLevel.Error);
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
        if (model is null || model.IsPlaceholder || SelectedProvider is null || HasModelSearchQuery || DraggingModel is not null) return false;
        var provider = SelectedProvider;
        var index = provider.Models.IndexOf(model);
        if (index < 0 || provider.Models.Count(item => item.IsRealModel) < 2) return false;
        modelDragOwnerProvider = provider;
        draggingModelOriginIndex = index;
        modelDragPlaceholder = ModelEditorViewModel.CreatePlaceholder();
        DraggingModel = model;
        provider.Models.RemoveAt(index);
        provider.Models.Insert(index, modelDragPlaceholder);
        provider.IsModelDragPreviewOwner = true;
        model.IsDragging = true;
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
        // 排序保存前先落库待存的模型编辑，避免旧 SortOrder 回写
        await SavePendingChangesAsync();
        suppressConfigurationRefresh = true;
        try
        {
            await dataStore.UpdateModelOrderAsync(provider.Id, new ModelOrderInput(provider.Models.Select(model => model.Id).ToArray()));
            SetStatus("providers.model.order.saved");
            toastService.Show(Status, ToastLevel.Success);
        }
        catch (Exception exception)
        {
            SetStatus("providers.model.order.save.failure", exception.Message);
            toastService.Show(Loc("providers.model.order.save.failure.toast"), ToastLevel.Error);
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
        // 先落库待存的编辑，再整体切换，避免覆盖
        await SavePendingChangesAsync();
        suppressConfigurationRefresh = true;
        try
        {
            foreach (var model in models)
            {
                model.Enabled = enabled;
                await SaveModelAsync(provider, model);
            }
            SetStatus(enabled ? "providers.model.enable.all" : "providers.model.disable.all");
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
        if (sender is ProviderEditorViewModel changedProvider && args.PropertyName is nameof(ProviderEditorViewModel.BaseUrl) or nameof(ProviderEditorViewModel.ModelListUrl) or nameof(ProviderEditorViewModel.ApiMode) or nameof(ProviderEditorViewModel.Enabled) or nameof(ProviderEditorViewModel.UseProxy) or nameof(ProviderEditorViewModel.ApiKey) or nameof(ProviderEditorViewModel.HeadersJson) or nameof(ProviderEditorViewModel.Headers))
            changedProvider.ResetHealthForConfigurationChange();
        UpdateSummary();
        // 行内编辑不改变列表成员，不再逐键触发 FilteredProviders 重算，避免 ListBox 每键重置。
        if (!suppressConfigurationRefresh
            && sender is ProviderEditorViewModel provider
            && ProviderEditorViewModel.IsPersistedProperty(args.PropertyName)
            && args.PropertyName is not nameof(ProviderEditorViewModel.Headers)
            && provider.HasUnsavedChanges)
            _ = SaveProviderAsync(provider);
    }
    private void ProvidersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(FilteredProviders));
        OnPropertyChanged(nameof(HasNoSelectedProvider));
    }
    private void ModelsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null) foreach (ModelEditorViewModel model in args.NewItems) model.PropertyChanged += ModelChanged;
        if (args.OldItems is not null) foreach (ModelEditorViewModel model in args.OldItems) model.PropertyChanged -= ModelChanged;
        UpdateSummary();
        OnPropertyChanged(nameof(FilteredModels));
        OnPropertyChanged(nameof(HasFilteredModels));
        OnPropertyChanged(nameof(HasNoFilteredModels));
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
        if (!suppressConfigurationRefresh
            && SelectedProvider is { } provider
            && ModelEditorViewModel.IsPersistedProperty(args.PropertyName)
            && model.HasUnsavedChanges)
            _ = SaveModelAsync(provider, model);
    }

    private void UpdateSummary()
    {
        TotalModelCount = Providers.Sum(provider => provider.Models.Count(model => model.IsRealModel));
        ProtectedKeyCount = Providers.Count(provider => provider.HasApiKey);
        EnabledProviderCount = Providers.Count(provider => provider.Enabled);
        var enabledProviders = Providers.Where(provider => provider.Enabled).ToArray();
        HealthyProviderCount = enabledProviders.Count(provider => provider.HealthState == ProviderHealthState.Healthy);
        CheckingProviderCount = enabledProviders.Count(provider => provider.HealthState == ProviderHealthState.Checking);
        PendingProviderCount = enabledProviders.Count(provider => provider.HealthState == ProviderHealthState.Unknown);
        WarningProviderCount = enabledProviders.Count(provider => provider.IsHealthWarning);
        ErrorProviderCount = enabledProviders.Count(provider => provider.IsHealthError);
        OnPropertyChanged(nameof(IsProviderHealthIdle));
        if (VerifyAllProvidersCommand is AsyncCommand command) command.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ProviderHealthSummary));
    }

    internal static bool MatchesProviderSearch(ProviderEditorViewModel provider, string query)
    {
        query = query.Trim();
        if (string.IsNullOrEmpty(query)) return true;
        return provider.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || provider.BusinessId.Contains(query, StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains(query, StringComparison.OrdinalIgnoreCase)
            || provider.ApiMode.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

}

public sealed class ProviderEditorViewModel : NotifyViewModel
{
    public Guid Id { get; set; }
    private string businessId = ""; private string displayName = ""; private string baseUrl = ""; private string modelListUrl = ""; private string apiMode = "openai"; private string endpointFormat = "responses"; private bool enabled; private bool useProxy; private string apiKey = ""; private bool apiKeyEdited; private bool isApiKeyVisible; private string headersJson = "{}";
    private ProviderHealthState healthState = ProviderHealthState.Unknown;
    private ProviderHealthFailureKind healthFailureKind;
    private int? healthStatusCode;
    private long? healthLatencyMs;
    private int? healthModelCount;
    private DateTimeOffset? healthLastCheckedAt;
    private string? healthFailureCode;
    private bool isDirty;
    private long editRevision;
    private long savedEditRevision;
    private bool isModelDragPreviewOwner;
    private bool suppressDirtyTracking;
    private bool suppressCliIdentityVersionChange;
    public string BusinessId { get => businessId; set => SetProperty(ref businessId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ModelListUrl { get => modelListUrl; set => SetProperty(ref modelListUrl, value); }
    public string ApiMode { get => apiMode; set { if (!SetProperty(ref apiMode, value)) return; OnPropertyChanged(nameof(IsEndpointFormatVisible)); UpdateCliIdentityRecommendations(); } }
    public string EndpointFormat { get => endpointFormat; set { var normalized = EndpointFormatOption.Normalize(value); if (!SetProperty(ref endpointFormat, normalized)) return; OnPropertyChanged(nameof(SelectedEndpointFormat)); } }
    public IReadOnlyList<EndpointFormatOption> EndpointFormatOptions { get; } = EndpointFormatOption.All;
    public EndpointFormatOption SelectedEndpointFormat { get => EndpointFormatOption.FromValue(EndpointFormat); set { if (value is not null) EndpointFormat = value.Value; } }
    public bool IsEndpointFormatVisible => string.Equals(ApiMode, "openai", StringComparison.OrdinalIgnoreCase);
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public bool UseProxy { get => useProxy; set => SetProperty(ref useProxy, value); } public string ApiKey { get => apiKey; set { if (string.Equals(apiKey, value, StringComparison.Ordinal)) return; apiKeyEdited = true; SetProperty(ref apiKey, value); } } public bool IsApiKeyVisible { get => isApiKeyVisible; private set { if (SetProperty(ref isApiKeyVisible, value)) { OnPropertyChanged(nameof(IsApiKeyHidden)); OnPropertyChanged(nameof(ApiKeyPasswordChar)); OnPropertyChanged(nameof(ApiKeyVisibilityToolTip)); } } } public bool IsApiKeyHidden => !IsApiKeyVisible; public char ApiKeyPasswordChar => IsApiKeyVisible ? '\0' : '●'; public string ApiKeyVisibilityToolTip => IsApiKeyVisible ? ResourceLookup.Resolve("providers.apikey.visibility.hide") : ResourceLookup.Resolve("providers.apikey.visibility.show"); public string HeadersJson { get => headersJson; private set => SetProperty(ref headersJson, value); } public bool HasApiKey { get; private set; } public string ApiKeyWatermark => HasApiKey ? ResourceLookup.Resolve("providers.apikey.configured") : ResourceLookup.Resolve("providers.apikey.watermark");
    public ProviderHealthState HealthState => healthState;
    public ProviderHealthFailureKind HealthFailureKind => healthFailureKind;
    public int? HealthStatusCode => healthStatusCode;
    public long? HealthLatencyMs => healthLatencyMs;
    public int? HealthModelCount => healthModelCount;
    public DateTimeOffset? HealthLastCheckedAt => healthLastCheckedAt;
    public bool IsHealthUnknown => healthState == ProviderHealthState.Unknown;
    public bool IsHealthChecking => healthState == ProviderHealthState.Checking;
    public bool IsHealthDisabled => healthState == ProviderHealthState.Disabled;
    public bool IsHealthSuccess => healthState == ProviderHealthState.Healthy;
    public bool IsHealthPassed => healthState is ProviderHealthState.Healthy or ProviderHealthState.HealthyEmptyModels;
    public bool IsHealthWarning => healthState is ProviderHealthState.HealthyEmptyModels or ProviderHealthState.RateLimited;
    public bool IsHealthError => healthState is ProviderHealthState.AuthFailed or ProviderHealthState.ConfigurationError or ProviderHealthState.EndpointError or ProviderHealthState.Unavailable or ProviderHealthState.RequestRejected or ProviderHealthState.UpstreamError or ProviderHealthState.ProtocolError;
    public string HealthStatusText => ResourceLookup.Resolve(HealthStatusKey(healthState));
    public string HealthDetailText
    {
        get
        {
            var statusCode = healthStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-";
            var latency = healthLatencyMs?.ToString(CultureInfo.InvariantCulture) ?? "-";
            return healthState switch
            {
                ProviderHealthState.Unknown => ResourceLookup.Resolve("providers.health.detail.pending"),
                ProviderHealthState.Checking => ResourceLookup.Resolve("providers.health.detail.checking"),
                ProviderHealthState.Disabled => ResourceLookup.Resolve("providers.health.detail.disabled"),
                ProviderHealthState.Healthy => string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.health.detail.healthy"), statusCode, latency, healthModelCount ?? 0),
                ProviderHealthState.HealthyEmptyModels => string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.health.detail.empty"), statusCode, latency),
                ProviderHealthState.AuthFailed => string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.health.detail.auth"), statusCode),
                ProviderHealthState.ConfigurationError => ResourceLookup.Resolve(healthFailureCode == "incomplete_headers" ? "providers.health.detail.incomplete.headers" : "providers.health.detail.config"),
                ProviderHealthState.EndpointError => string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.health.detail.endpoint"), statusCode),
                ProviderHealthState.Unavailable => ResourceLookup.Resolve(healthFailureCode == "timeout" ? "providers.health.detail.timeout" : "providers.health.detail.unavailable"),
                ProviderHealthState.RateLimited => ResourceLookup.Resolve("providers.health.detail.rate"),
                ProviderHealthState.RequestRejected => string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.health.detail.request"), statusCode),
                ProviderHealthState.UpstreamError => string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.health.detail.upstream"), statusCode),
                ProviderHealthState.ProtocolError => ResourceLookup.Resolve("providers.health.detail.protocol"),
                _ => ResourceLookup.Resolve("providers.health.detail.pending")
            };
        }
    }
    public string HealthTooltipText => $"{HealthStatusText} · {HealthDetailText}";
    internal long EditRevision => editRevision;
    public bool HasUnsavedChanges => Id == Guid.Empty || isDirty || editRevision != savedEditRevision;
    public ObservableCollection<ModelEditorViewModel> Models { get; } = [];
    public bool IsModelDragPreviewOwner { get => isModelDragPreviewOwner; set => SetProperty(ref isModelDragPreviewOwner, value); }
    public bool HasModels => Models.Any(model => model.IsRealModel);
    public ObservableCollection<HeaderEditorViewModel> Headers { get; } = [];
    public bool HasNoHeaders => Headers.Count == 0;
    public int IncompleteHeaderCount => Headers.Count(IsIncomplete);
    public bool HasIncompleteHeaders => IncompleteHeaderCount > 0;
    public ObservableCollection<CliIdentityItemViewModel> CliIdentities { get; } = [];
    public CliIdentityType? CurrentCliIdentity { get; private set; }
    public string CurrentCliIdentitySummary
    {
        get
        {
            if (CurrentCliIdentity is not { } type)
                return ResourceLookup.Resolve("providers.cli.current.none");

            var item = CliIdentities.FirstOrDefault(candidate => candidate.Type == type);
            var label = CliIdentityService.GetProfile(type).DisplayName;
            var version = string.IsNullOrWhiteSpace(item?.Version) ? "-" : item.Version;
            return string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("providers.cli.current.prefix"), $"{label} {version}");
        }
    }
    public ICommand ApplyCliIdentityCommand { get; }
    public ICommand RefreshCliVersionsCommand { get; }
    private bool isRefreshingCliVersions;
    public bool IsRefreshingCliVersions { get => isRefreshingCliVersions; private set { if (!SetProperty(ref isRefreshingCliVersions, value)) return; (RefreshCliVersionsCommand as AsyncCommand)?.RaiseCanExecuteChanged(); } }
    private static string HealthStatusKey(ProviderHealthState state) => state switch
    {
        ProviderHealthState.Unknown => "providers.health.status.pending",
        ProviderHealthState.Checking => "providers.health.status.checking",
        ProviderHealthState.Healthy => "providers.health.status.healthy",
        ProviderHealthState.HealthyEmptyModels => "providers.health.status.empty",
        ProviderHealthState.AuthFailed => "providers.health.status.auth",
        ProviderHealthState.ConfigurationError => "providers.health.status.config",
        ProviderHealthState.EndpointError => "providers.health.status.endpoint",
        ProviderHealthState.Unavailable => "providers.health.status.unavailable",
        ProviderHealthState.RateLimited => "providers.health.status.rate",
        ProviderHealthState.RequestRejected => "providers.health.status.request",
        ProviderHealthState.UpstreamError => "providers.health.status.upstream",
        ProviderHealthState.ProtocolError => "providers.health.status.protocol",
        ProviderHealthState.Disabled => "providers.health.status.disabled",
        _ => "providers.health.status.pending"
    };

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ApiKeyVisibilityToolTip));
        OnPropertyChanged(nameof(ApiKeyWatermark));
        OnPropertyChanged(nameof(CurrentCliIdentitySummary));
        OnPropertyChanged(nameof(HealthStatusText));
        OnPropertyChanged(nameof(HealthDetailText));
        OnPropertyChanged(nameof(HealthTooltipText));
    }
    public ProviderEditorViewModel()
    {
        PropertyChanged += (_, args) =>
        {
            if (!suppressDirtyTracking && IsPersistedProperty(args.PropertyName))
            {
                isDirty = true;
                editRevision++;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        };
        Headers.CollectionChanged += HeadersChanged;
        Models.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModels));
        ApplyCliIdentityCommand = CreateAsyncCommand((object? parameter) => ApplyCliIdentityAsync(parameter), (object? _) => true);
        RefreshCliVersionsCommand = CreateAsyncCommand((object? _) => RefreshCliVersionsAsync(), (object? _) => !IsRefreshingCliVersions);
        InitializeCliIdentities();
    }
    public static ProviderEditorViewModel FromResponse(ProviderResponse response) { var value = new ProviderEditorViewModel(); value.ApplyResponse(response); foreach (var model in response.Models) value.Models.Add(ModelEditorViewModel.FromResponse(model)); return value; }
    public ProviderInput ToInput() => new(BusinessId, DisplayName, BaseUrl, ApiMode, Enabled, apiKeyEdited ? ApiKey : null, false, ToHeaderDictionary(), UseProxy, string.IsNullOrWhiteSpace(ModelListUrl) ? null : ModelListUrl, EndpointFormat);
    internal ProviderHealthCheckRequest ToHealthCheckRequest() => new(BusinessId, Enabled, BaseUrl, ModelListUrl, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey, ToHeaderDictionary(), IncompleteHeaderCount, UseProxy);
    internal void ApplyHealthResult(ProviderHealthResult result)
    {
        healthState = result.State;
        healthFailureKind = result.FailureKind;
        healthStatusCode = result.StatusCode;
        healthLatencyMs = result.LatencyMs;
        healthModelCount = result.DiscoveredModelCount;
        healthFailureCode = result.FailureCode;
        healthLastCheckedAt = result.State is ProviderHealthState.Unknown or ProviderHealthState.Checking or ProviderHealthState.Disabled ? null : DateTimeOffset.Now;
        OnPropertyChanged(nameof(HealthState));
        OnPropertyChanged(nameof(HealthFailureKind));
        OnPropertyChanged(nameof(HealthStatusCode));
        OnPropertyChanged(nameof(HealthLatencyMs));
        OnPropertyChanged(nameof(HealthModelCount));
        OnPropertyChanged(nameof(HealthLastCheckedAt));
        OnPropertyChanged(nameof(IsHealthUnknown));
        OnPropertyChanged(nameof(IsHealthChecking));
        OnPropertyChanged(nameof(IsHealthDisabled));
        OnPropertyChanged(nameof(IsHealthSuccess));
        OnPropertyChanged(nameof(IsHealthPassed));
        OnPropertyChanged(nameof(IsHealthWarning));
        OnPropertyChanged(nameof(IsHealthError));
        OnPropertyChanged(nameof(HealthStatusText));
        OnPropertyChanged(nameof(HealthDetailText));
        OnPropertyChanged(nameof(HealthTooltipText));
    }
    internal void ResetHealthForConfigurationChange() => ApplyHealthResult(new(Enabled ? ProviderHealthState.Unknown : ProviderHealthState.Disabled));
    internal void ResetHealthForCancellation() => ResetHealthForConfigurationChange();
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
            LoadCliVersionsFromCache();
        }
        finally
        {
            suppressDirtyTracking = false;
            if (isDirty)
            {
                isDirty = false;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
            savedEditRevision = editRevision;
        }
    }
    public void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;
    /// <summary>自动保存成功后只回填服务端生成的标识与密钥状态，不重写用户正在编辑的文本，也不重建 Headers 行，避免打断输入。</summary>
    public void ApplySaveResult(ProviderResponse response, long? savedRevision = null)
    {
        var currentRevision = editRevision;
        var canApplySecret = savedRevision is null || savedRevision >= currentRevision;
        suppressDirtyTracking = true;
        try
        {
            Id = response.Id;
            HasApiKey = response.HasApiKey;
            OnPropertyChanged(nameof(ApiKeyWatermark));
            if (canApplySecret && (response.ApiKey is not null || !response.HasApiKey))
                SetApiKeyFromResponse(response.ApiKey ?? "");
            else if (canApplySecret)
                apiKeyEdited = false;
            LoadCliVersionsFromCache();
        }
        finally
        {
            suppressDirtyTracking = false;
            var requestedRevision = savedRevision ?? editRevision;
            var wasDirty = HasUnsavedChanges;
            if (requestedRevision >= editRevision)
            {
                isDirty = false;
                savedEditRevision = editRevision;
            }
            else savedEditRevision = Math.Max(savedEditRevision, requestedRevision);
            if (wasDirty != HasUnsavedChanges) OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }
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
    internal static bool IsPersistedProperty(string? propertyName) => propertyName is nameof(BusinessId) or nameof(DisplayName) or nameof(BaseUrl) or nameof(ModelListUrl) or nameof(ApiMode) or nameof(EndpointFormat) or nameof(Enabled) or nameof(UseProxy) or nameof(ApiKey) or nameof(HeadersJson) or nameof(Headers);
    internal static Dictionary<string, string>? ParseDictionary(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);

    // ---------- CLI 身份模拟 ----------

    private static readonly Lazy<CliVersionService> CliVersionServiceLazy = new(() => new CliVersionService());

    /// <summary>
    /// 初始化三家 CLI 身份的默认条目（构造时执行一次）。
    /// IsRecommended 依据 provider.ApiMode 判断：Claude 匹配 "anthropic"，Codex/Grok 匹配 "openai"。
    /// </summary>
    private void InitializeCliIdentities()
    {
        var mode = ApiMode ?? "";
        foreach (var type in new[] { CliIdentityType.ClaudeCode, CliIdentityType.Codex, CliIdentityType.Grok })
        {
            var isRecommended = type switch
            {
                CliIdentityType.ClaudeCode => string.Equals(mode, "anthropic", StringComparison.OrdinalIgnoreCase),
                CliIdentityType.Codex => string.Equals(mode, "openai", StringComparison.OrdinalIgnoreCase),
                CliIdentityType.Grok => false,
                _ => false,
            };
            var item = new CliIdentityItemViewModel(type, isRecommended)
            {
                Version = CliVersionService.GetDefaultVersion(type)
            };
            item.PropertyChanged += CliIdentityItemChanged;
            CliIdentities.Add(item);
        }
        ReconcileCliIdentitiesFromHeaders();
    }

    private void UpdateCliIdentityRecommendations()
    {
        foreach (var item in CliIdentities)
        {
            var isRecommended = item.Type switch
            {
                CliIdentityType.ClaudeCode => string.Equals(ApiMode, "anthropic", StringComparison.OrdinalIgnoreCase),
                CliIdentityType.Codex => string.Equals(ApiMode, "openai", StringComparison.OrdinalIgnoreCase),
                CliIdentityType.Grok => false,
                _ => false,
            };
            item.SetRecommended(isRecommended);
        }
    }

    private void CliIdentityItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not CliIdentityItemViewModel item || args.PropertyName != nameof(CliIdentityItemViewModel.Version)) return;
        OnPropertyChanged(nameof(CurrentCliIdentitySummary));
        if (suppressCliIdentityVersionChange || !item.IsApplied || string.IsNullOrWhiteSpace(item.Version)) return;

        if (item.Type == CliIdentityType.Grok)
        {
            item.Source = CliVersionSource.UserOverridden;
            new CliVersionCache().SetUserOverride(item.Type, item.Version);
        }

        _ = ApplyCliIdentityAsync(item);
    }

    /// <summary>
    /// 从现有 Headers 反推当前 CLI 身份并更新每条 item 的 IsApplied 标记。
    /// 调用时机：ApplyResponse 后（载入 Provider 时）与 ApplyCliIdentityAsync 后。
    /// </summary>
    internal void ReconcileCliIdentitiesFromHeaders()
    {
        var headers = ToHeaderDictionary();
        var detected = CliIdentityService.DetectCliIdentity(headers);
        var detectedVersion = detected is null ? null : CliIdentityService.DetectCliVersion(headers, detected.Value);
        CurrentCliIdentity = detected;
        suppressCliIdentityVersionChange = true;
        try
        {
            foreach (var item in CliIdentities)
            {
                item.IsApplied = item.Type == detected;
                if (detected is not null && item.Type == detected && !string.IsNullOrEmpty(detectedVersion))
                    item.Version = detectedVersion;
            }
        }
        finally
        {
            suppressCliIdentityVersionChange = false;
        }
        OnPropertyChanged(nameof(CurrentCliIdentity));
        OnPropertyChanged(nameof(CurrentCliIdentitySummary));
    }

    /// <summary>
    /// 从缓存 / 默认值刷新 UI 上显示的版本号（不触发网络请求）。
    /// </summary>
    public void LoadCliVersionsFromCache()
    {
        var cache = new CliVersionCache();
        suppressCliIdentityVersionChange = true;
        try
        {
            foreach (var item in CliIdentities)
            {
                var cached = cache.Get(item.Type);
                if (cached is not null)
                    item.ApplyVersion(cached.Version, cached.Source);
                else
                    item.ApplyVersion(CliVersionService.GetDefaultVersion(item.Type), CliVersionSource.Default);
            }
        }
        finally
        {
            suppressCliIdentityVersionChange = false;
        }
        ReconcileCliIdentitiesFromHeaders();
    }

    public void RefreshCliVersionsIfStale()
    {
        var cache = new CliVersionCache();
        if (CliIdentities.Any(item => cache.IsStale(item.Type)))
            RefreshCliVersionsCommand.Execute(null);
    }

    /// <summary>
    /// 强制刷新三家 CLI 版本（网络请求，失败降级到缓存→默认）。
    /// </summary>
    private async Task RefreshCliVersionsAsync(CancellationToken cancellationToken = default)
    {
        if (IsRefreshingCliVersions) return;
        IsRefreshingCliVersions = true;
        try
        {
            var service = CliVersionServiceLazy.Value;
            var cache = new CliVersionCache();
            var detected = CliIdentityService.DetectCliIdentity(ToHeaderDictionary());
            foreach (var item in CliIdentities)
            {
                if (cancellationToken.IsCancellationRequested) return;
                try
                {
                    var info = await service.GetVersionAsync(item.Type, forceRefresh: true, cancellationToken);
                    item.ApplyVersion(info.Version, info.Source);
                    if (detected is not null && item.Type == detected)
                    {
                        var applied = CliIdentityService.DetectCliVersion(ToHeaderDictionary(), item.Type);
                        item.HasNewVersion = !string.IsNullOrEmpty(applied) && !string.Equals(applied, info.Version, StringComparison.Ordinal);
                    }
                }
                catch
                {
                    // 单条失败不影响其他条目。
                    var fallback = cache.Get(item.Type);
                    item.ApplyVersion(fallback?.Version ?? CliVersionService.GetDefaultVersion(item.Type), fallback is null ? CliVersionSource.Default : CliVersionSource.Cached);
                }
            }
        }
        finally
        {
            IsRefreshingCliVersions = false;
        }
    }

    /// <summary>
    /// 应用 CLI 身份：CommandParameter 是 CliIdentityItemViewModel。
    /// </summary>
    private Task ApplyCliIdentityAsync(object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not CliIdentityItemViewModel item || string.IsNullOrWhiteSpace(item.Version)) return Task.CompletedTask;

        // 若已手改版本，写入用户覆盖缓存（避免下次被默认值覆盖）。
        if (item.Source == CliVersionSource.UserOverridden || (item.Type == CliIdentityType.Grok && CliVersionService.GetDefaultVersion(item.Type) != item.Version))
        {
            var cache = new CliVersionCache();
            cache.SetUserOverride(item.Type, item.Version);
        }

        // 剥除其他家族头 → 合并目标家族头。
        var headers = ToHeaderDictionary();
        var updated = CliIdentityService.ApplyCliIdentity(headers, item.Type, item.Version);
        ApplyHeaders(updated);
        ReconcileCliIdentitiesFromHeaders();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将 header 字典覆盖回 Headers 集合（供 CLI 身份应用后使用）。
    /// 复用 Headers.CollectionChanged 的追踪逻辑。
    /// </summary>
    internal void ApplyHeaders(IDictionary<string, string> updatedHeaders)
    {
        var incomplete = Headers.Where(IsIncomplete).ToList();
        Headers.CollectionChanged -= HeadersChanged;
        foreach (var h in Headers) h.PropertyChanged -= HeaderChanged;
        Headers.Clear();
        foreach (var (key, val) in updatedHeaders)
        {
            var header = new HeaderEditorViewModel { Name = key, Value = val };
            header.PropertyChanged += HeaderChanged;
            Headers.Add(header);
        }
        foreach (var header in incomplete)
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

    private static AsyncCommand CreateAsyncCommand(Func<object?, Task> action, Func<object?, bool> canExecute)
        => new(action, canExecute);
}

public sealed record EndpointFormatOption(string Value, string DisplayName)
{
    public static IReadOnlyList<EndpointFormatOption> All { get; } = [new("chat_completions", "OpenAI-Completions"), new("responses", "Responses API")];
    public static EndpointFormatOption FromValue(string? value) => All.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)) ?? All[1];
    public static string Normalize(string? value) => FromValue(value).Value;
    public override string ToString() => DisplayName;
}

/// <summary>
/// CLI 身份下拉菜单中单条项的 ViewModel。
/// 承载显示名、版本（可手改）、来源标记、已应用标记与推荐标记。
/// </summary>
    public sealed class CliIdentityItemViewModel : NotifyViewModel
{
    private string version = "";
    private bool isApplied;
    private bool hasNewVersion;
    private CliVersionSource source;
    public CliIdentityType Type { get; }
    public string DisplayName { get; }
    private bool isRecommended;
    public bool IsRecommended { get => isRecommended; private set => SetProperty(ref isRecommended, value); }
    public bool IsVersionReadOnly => Type != CliIdentityType.Grok;
    /// <summary>版本，可被用户手改（Grok 场景）。</summary>
    public string Version { get => version; set => SetProperty(ref version, value); }
    /// <summary>是否已应用该身份到当前 Provider 的 Headers。</summary>
    public bool IsApplied { get => isApplied; set => SetProperty(ref isApplied, value); }
    /// <summary>缓存/本地值是否与在线最新版本不同（触发「有新版本」标记）。</summary>
    public bool HasNewVersion { get => hasNewVersion; set => SetProperty(ref hasNewVersion, value); }
    /// <summary>版本来源标记（UI 上显示为标签）。</summary>
    public CliVersionSource Source { get => source; set => SetProperty(ref source, value); }

    /// <summary>UA 预览字符串，如 "claude-cli/2.1.263"。</summary>
    public string ShortDescription => $"{CliIdentityService.GetProfile(Type).UaPrefix}{Version}";

    public CliIdentityItemViewModel(CliIdentityType type, bool isRecommended = false)
    {
        Type = type;
        DisplayName = CliIdentityService.GetProfile(type).DisplayName;
        IsRecommended = isRecommended;
        Source = CliVersionSource.Default;
    }

    internal void SetRecommended(bool value) => IsRecommended = value;

    public void ApplyVersion(string version, CliVersionSource source, bool hasNewVersion = false)
    {
        Version = version;
        Source = source;
        HasNewVersion = hasNewVersion;
        OnPropertyChanged(nameof(ShortDescription));
    }
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
    private long editRevision;
    private long savedEditRevision;
    private bool suppressDirtyTracking;
    private bool isDragging;
    private bool isPlaceholder;
    private string modelId = ""; private string displayName = ""; private string family = "claude"; private string configId = ""; private string baseUrl = ""; private string apiMode = ""; private int contextLength = 128000; private int maxTokens = 4096; private bool vision; private double? temperature; private double? topP; private bool enabled = true; private string apiKey = ""; private bool clearApiKey; private string headersJson = "{}"; private string extraJson = "{}";
    private string? ownedBy; private string? remoteFamily; private int? remoteContextLength; private int? remoteMaxTokens; private bool? remoteVision; private int sortOrder;
    public ModelEditorViewModel()
    {
        PropertyChanged += (_, args) =>
        {
            if (!suppressDirtyTracking && IsPersistedProperty(args.PropertyName))
            {
                isDirty = true;
                editRevision++;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        };
    }
    internal static bool IsPersistedProperty(string? propertyName) => propertyName is nameof(ModelId) or nameof(DisplayName) or nameof(Family) or nameof(ConfigId) or nameof(BaseUrl) or nameof(ApiMode) or nameof(ContextLength) or nameof(MaxTokens) or nameof(Vision) or nameof(Temperature) or nameof(TopP) or nameof(Enabled) or nameof(ApiKey) or nameof(ClearApiKey) or nameof(HeadersJson) or nameof(ExtraJson);
    internal long EditRevision => editRevision;
    public bool HasUnsavedChanges => Id == Guid.Empty || isDirty || editRevision != savedEditRevision;
    public string ModelId { get => modelId; set => SetProperty(ref modelId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string Family { get => family; set => SetProperty(ref family, value); } public string ConfigId { get => configId; set => SetProperty(ref configId, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ApiMode { get => apiMode; set => SetProperty(ref apiMode, value); } public int ContextLength { get => contextLength; set => SetProperty(ref contextLength, value); } public int MaxTokens { get => maxTokens; set => SetProperty(ref maxTokens, value); } public bool Vision { get => vision; set => SetProperty(ref vision, value); } public double? Temperature { get => temperature; set => SetProperty(ref temperature, value); } public double? TopP { get => topP; set => SetProperty(ref topP, value); } public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public string ApiKey { get => apiKey; set => SetProperty(ref apiKey, value); } public bool ClearApiKey { get => clearApiKey; set => SetProperty(ref clearApiKey, value); } public string HeadersJson { get => headersJson; set => SetProperty(ref headersJson, value); } public string ExtraJson { get => extraJson; set => SetProperty(ref extraJson, value); } public bool HasApiKey { get; private set; }
    public string? OwnedBy { get => ownedBy; private set => SetProperty(ref ownedBy, value); }
    public string? RemoteFamily { get => remoteFamily; private set => SetProperty(ref remoteFamily, value); }
    public int? RemoteContextLength { get => remoteContextLength; private set { if (SetProperty(ref remoteContextLength, value)) { OnPropertyChanged(nameof(ContextDisplay)); OnPropertyChanged(nameof(MetadataToolTip)); } } }
    public int? RemoteMaxTokens { get => remoteMaxTokens; private set { if (SetProperty(ref remoteMaxTokens, value)) { OnPropertyChanged(nameof(MaxTokensDisplay)); OnPropertyChanged(nameof(MetadataToolTip)); } } }
    public bool? RemoteVision { get => remoteVision; private set { if (SetProperty(ref remoteVision, value)) OnPropertyChanged(nameof(CapabilitiesDisplay)); } }
    public int SortOrder { get => sortOrder; internal set => SetProperty(ref sortOrder, value); }
    public bool IsDragging { get => isDragging; set => SetProperty(ref isDragging, value); }
    public bool IsPlaceholder { get => isPlaceholder; private init => isPlaceholder = value; }
    public bool IsRealModel => !IsPlaceholder;
    public string ContextDisplay => RemoteContextLength is int value ? value.ToString("N0") : "-";
    public string MaxTokensDisplay => RemoteMaxTokens is int value ? value.ToString("N0") : "-";
    public string MetadataToolTip => RemoteContextLength is null || RemoteMaxTokens is null ? ResourceLookup.Resolve("providers.model.context.missing") : "";
    public string CapabilitiesDisplay => RemoteVision is true ? ResourceLookup.Resolve("providers.model.capability.vision") : RemoteVision is false ? ResourceLookup.Resolve("providers.model.capability.text") : "-";
    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(MetadataToolTip));
        OnPropertyChanged(nameof(CapabilitiesDisplay));
    }
    public static ModelEditorViewModel FromResponse(ModelResponse response)
    {
        var value = new ModelEditorViewModel { Id = response.Id, ProviderId = response.ProviderId, ModelId = response.ModelId, DisplayName = response.DisplayName, ConfigId = response.ConfigId ?? "", Family = response.Family, BaseUrl = response.BaseUrl ?? "", ApiMode = response.ApiMode ?? "", ContextLength = response.ContextLength, MaxTokens = response.MaxTokens, Vision = response.Vision, Temperature = response.Temperature, TopP = response.TopP, Enabled = response.Enabled, HasApiKey = response.HasApiKey, HeadersJson = response.HeadersJson, ExtraJson = response.ExtraJson, ownedBy = response.OwnedBy, remoteFamily = response.RemoteFamily, remoteContextLength = response.RemoteContextLength, remoteMaxTokens = response.RemoteMaxTokens, remoteVision = response.RemoteVision, sortOrder = response.SortOrder };
        value.isDirty = false;
        value.savedEditRevision = value.editRevision;
        return value;
    }
    public static ModelEditorViewModel CreatePlaceholder() => new() { IsPlaceholder = true };
    /// <summary>自动保存成功后只回填服务端标识与远程元数据，不重写用户正在编辑的文本。</summary>
    internal void ApplySaveResult(ModelResponse response, long? savedRevision = null)
    {
        suppressDirtyTracking = true;
        try
        {
            Id = response.Id;
            ProviderId = response.ProviderId;
            HasApiKey = response.HasApiKey;
            OwnedBy = response.OwnedBy;
            RemoteFamily = response.RemoteFamily;
            RemoteContextLength = response.RemoteContextLength;
            RemoteMaxTokens = response.RemoteMaxTokens;
            RemoteVision = response.RemoteVision;
            SortOrder = response.SortOrder;
        }
        finally { suppressDirtyTracking = false; }
        var requestedRevision = savedRevision ?? editRevision;
        var wasDirty = HasUnsavedChanges;
        if (requestedRevision >= editRevision)
        {
            isDirty = false;
            savedEditRevision = editRevision;
        }
        else savedEditRevision = Math.Max(savedEditRevision, requestedRevision);
        if (wasDirty != HasUnsavedChanges) OnPropertyChanged(nameof(HasUnsavedChanges));
    }
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
