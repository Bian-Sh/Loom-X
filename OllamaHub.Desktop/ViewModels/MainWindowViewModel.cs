using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using OllamaHub.Configuration;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class MainWindowViewModel : NotifyViewModel
{
    private readonly GatewayProcessService gatewayService;
    private readonly ConfigSnapshotService configService = new();
    private object currentView = new PlaceholderViewModel("加载中", "正在加载桌面控制中心。");
    private string pageTitle = "概览";
    private string pageDescription = "确认本地服务健康，快速查看网关与模型配置。";

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public object CurrentView { get => currentView; private set => SetProperty(ref currentView, value); }
    public string PageTitle { get => pageTitle; private set => SetProperty(ref pageTitle, value); }
    public string PageDescription { get => pageDescription; private set => SetProperty(ref pageDescription, value); }

    public MainWindowViewModel(GatewayProcessService gatewayService)
    {
        this.gatewayService = gatewayService;
        NavigationItems = new([
            new("概览", "◉", () => ShowOverview()),
            new("网关", "▦", () => ShowPlaceholder("网关", "网关路由与监听地址将在下一阶段接入。")),
            new("Provider", "⇄", () => ShowProviders()),
            new("活动", "≋", () => ShowPlaceholder("活动", "请求诊断与协议转换记录将在下一阶段接入。")),
            new("控制台", "⌘", () => ShowPlaceholder("控制台", "实时运行日志将在下一阶段接入。"), "运维"),
            new("设置", "⚙", () => ShowPlaceholder("设置", "主题、代理和数据设置将在下一阶段接入。"))
        ]);
        ShowOverview();
    }

    private void SetActive(string title)
    {
        foreach (var item in NavigationItems) item.IsActive = item.Title == title;
    }

    private void ShowOverview() { SetActive("概览"); PageTitle = "概览"; PageDescription = "确认本地服务健康，快速查看网关与模型配置。"; CurrentView = new OverviewViewModel(gatewayService, configService); }
    private void ShowProviders() { SetActive("Provider"); PageTitle = "Provider"; PageDescription = "管理上游连接、请求协议、密钥与可用模型。"; CurrentView = new ProvidersViewModel(configService); }
    private void ShowPlaceholder(string title, string description) { SetActive(title); PageTitle = title; PageDescription = description; CurrentView = new PlaceholderViewModel(title, description); }
}

public sealed class NavigationItemViewModel : NotifyViewModel
{
    public string Title { get; }
    public string Icon { get; }
    public string? SectionLabel { get; }
    public bool HasSectionLabel => !string.IsNullOrWhiteSpace(SectionLabel);
    private bool isActive;
    public bool IsActive { get => isActive; set => SetProperty(ref isActive, value); }
    public ICommand NavigateCommand { get; }
    public NavigationItemViewModel(string title, string icon, Action action, string? sectionLabel = null) { Title = title; Icon = icon; SectionLabel = sectionLabel; NavigateCommand = new DelegateCommand(action); }
}

public sealed class OverviewViewModel : NotifyViewModel
{
    private readonly GatewayProcessService gatewayService;
    private readonly ConfigSnapshotService configService;
    private string gatewayStatus = "未运行";
    private string endpoint = "未配置";
    private string version = "未知";
    private string lastChecked = "尚未检查";
    private int providerCount;
    private int modelCount;

    public string GatewayStatus { get => gatewayStatus; private set => SetProperty(ref gatewayStatus, value); }
    public string Endpoint { get => endpoint; private set => SetProperty(ref endpoint, value); }
    public string Version { get => version; private set => SetProperty(ref version, value); }
    public string LastChecked { get => lastChecked; private set => SetProperty(ref lastChecked, value); }
    public int ProviderCount { get => providerCount; private set => SetProperty(ref providerCount, value); }
    public int ModelCount { get => modelCount; private set => SetProperty(ref modelCount, value); }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RefreshCommand { get; }

    public OverviewViewModel(GatewayProcessService gatewayService, ConfigSnapshotService configService)
    {
        this.gatewayService = gatewayService;
        this.configService = configService;
        StartCommand = new AsyncCommand(StartAsync);
        StopCommand = new AsyncCommand(StopAsync);
        RefreshCommand = new AsyncCommand(RefreshAsync);
        gatewayService.StateChanged += OnGatewayStateChanged;
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
        var config = configService.Load();
        Endpoint = config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
        ProviderCount = config.Providers.Count;
        ModelCount = config.Models.Count;
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
        await Task.CompletedTask;
    }

    private string LoadEndpoint()
    {
        var config = configService.Load();
        return config.Server.Urls.Count > 0 ? config.Server.Urls[0] : "http://127.0.0.1:11434";
    }

    private void OnGatewayStateChanged(object? sender, EventArgs args) => _ = RefreshAsync();
}

public sealed class ProvidersViewModel : NotifyViewModel
{
    private readonly ConfigSnapshotService configService;
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
    public ObservableCollection<ProviderEditorViewModel> Providers { get; } = [];
    public ProviderEditorViewModel? SelectedProvider
    {
        get => selectedProvider;
        set
        {
            if (ReferenceEquals(selectedProvider, value)) return;
            DetachProvider(selectedProvider);
            SetProperty(ref selectedProvider, value);
            AttachProvider(selectedProvider);
            SelectedModel = null;
            OnPropertyChanged(nameof(HasSelectedProvider));
        }
    }
    public bool HasSelectedProvider => SelectedProvider is not null;

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

    public ProvidersViewModel(ConfigSnapshotService configService)
    {
        this.configService = configService;
        RefreshCommand = new AsyncCommand(RefreshAsync); NewProviderCommand = new DelegateCommand(NewProvider); SaveProviderCommand = new AsyncCommand(SaveProviderAsync); DeleteProviderCommand = new AsyncCommand(DeleteProviderAsync); NewModelCommand = new DelegateCommand(NewModel); SaveModelCommand = new AsyncCommand(SaveModelAsync); DeleteModelCommand = new AsyncCommand(DeleteModelAsync); TestConnectionCommand = new AsyncCommand(TestConnectionAsync); _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try { var selectedId = SelectedProvider?.Id; Providers.Clear(); foreach (var provider in await configService.ListProvidersAsync()) Providers.Add(ProviderEditorViewModel.FromResponse(provider)); SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == selectedId) ?? Providers.FirstOrDefault(); UpdateSummary(); Status = $"已加载 {Providers.Count} 个 Provider"; }
        catch (Exception exception) { Status = $"加载失败：{exception.Message}"; }
    }

    private void NewProvider() { var provider = new ProviderEditorViewModel { DisplayName = "新 Provider", ApiMode = "openai", Enabled = true }; Providers.Add(provider); SelectedProvider = provider; UpdateSummary(); Status = "正在编辑新 Provider"; }

    private async Task SaveProviderAsync()
    {
        if (SelectedProvider is null) return;
        try { var input = SelectedProvider.ToInput(); var response = SelectedProvider.Id == Guid.Empty ? await configService.CreateProviderAsync(input) : await configService.UpdateProviderAsync(SelectedProvider.Id, input); var index = Providers.IndexOf(SelectedProvider); var updated = ProviderEditorViewModel.FromResponse(response); Providers[index] = updated; SelectedProvider = updated; UpdateSummary(); Status = "Provider 已保存"; }
        catch (Exception exception) { Status = $"保存失败：{exception.Message}"; }
    }

    private async Task DeleteProviderAsync()
    {
        if (SelectedProvider is null) return;
        try { if (SelectedProvider.Id != Guid.Empty) await configService.DeleteProviderAsync(SelectedProvider.Id); Providers.Remove(SelectedProvider); SelectedProvider = Providers.FirstOrDefault(); UpdateSummary(); Status = "Provider 已删除"; }
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
        if (provider is null || string.IsNullOrWhiteSpace(provider.BaseUrl)) { ConnectionStatus = "请先填写 Base URL"; return; }
        connectionCancellation?.Cancel();
        connectionCancellation = new CancellationTokenSource();
        var token = connectionCancellation.Token;
        ConnectionStatus = "正在测试连接…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var baseUrl = provider.BaseUrl.TrimEnd('/');
            var endpoint = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{baseUrl}/models" : $"{baseUrl}/v1/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(provider.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            foreach (var header in ProviderEditorViewModel.ParseDictionary(provider.HeadersJson) ?? []) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            stopwatch.Stop();
            ConnectionStatus = response.IsSuccessStatusCode ? $"连接正常 · {(int)response.StatusCode} · {stopwatch.ElapsedMilliseconds} ms" : $"连接失败 · {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { stopwatch.Stop(); ConnectionStatus = $"连接失败 · {exception.Message}"; }
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
    private void ModelChanged(object? sender, PropertyChangedEventArgs args) => ScheduleModelAutoSave();

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
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(500, token); if (!token.IsCancellationRequested) await SaveProviderAsync(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }

    private void ScheduleModelAutoSave()
    {
        autoSaveCancellation?.Cancel();
        autoSaveCancellation = new CancellationTokenSource();
        var token = autoSaveCancellation.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(500, token); if (!token.IsCancellationRequested) await SaveModelAsync(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }
}

public sealed class ProviderEditorViewModel : NotifyViewModel
{
    public Guid Id { get; set; }
    private string businessId = ""; private string displayName = ""; private string baseUrl = ""; private string apiMode = "openai"; private bool enabled; private string apiKey = ""; private bool clearApiKey; private string headersJson = "{}";
    public string BusinessId { get => businessId; set => SetProperty(ref businessId, value); } public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); } public string BaseUrl { get => baseUrl; set => SetProperty(ref baseUrl, value); } public string ApiMode { get => apiMode; set => SetProperty(ref apiMode, value); } public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public string ApiKey { get => apiKey; set => SetProperty(ref apiKey, value); } public bool ClearApiKey { get => clearApiKey; set => SetProperty(ref clearApiKey, value); } public string HeadersJson { get => headersJson; set => SetProperty(ref headersJson, value); } public bool HasApiKey { get; private set; }
    public ObservableCollection<ModelEditorViewModel> Models { get; } = [];
    public static ProviderEditorViewModel FromResponse(ProviderResponse response) { var value = new ProviderEditorViewModel { Id = response.Id, BusinessId = response.BusinessId, DisplayName = response.DisplayName, BaseUrl = response.BaseUrl, ApiMode = response.ApiMode, Enabled = response.Enabled, HasApiKey = response.HasApiKey, HeadersJson = response.HeadersJson }; foreach (var model in response.Models) value.Models.Add(ModelEditorViewModel.FromResponse(model)); return value; }
    public ProviderInput ToInput() => new(BusinessId, DisplayName, BaseUrl, ApiMode, Enabled, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey, ClearApiKey, ParseDictionary(HeadersJson));
    internal static Dictionary<string, string>? ParseDictionary(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
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
    private readonly Func<Task> action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public AsyncCommand(Func<Task> action) => this.action = action;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await action();
}
