using System.Collections.ObjectModel;
using System.Windows.Input;
using OllamaHub.Configuration;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class GatewayViewModel : NotifyViewModel
{
    private readonly ConfigSnapshotService configService;
    private readonly ToastService toastService;
    private GatewayEndpointEditorViewModel? selectedEndpoint;
    private string status = "";
    private string selectedSortMode = "Provider";
    public ObservableCollection<GatewayEndpointEditorViewModel> Endpoints { get; } = [];
    public ObservableCollection<GatewayModelOption> AvailableModels { get; } = [];
    public ObservableCollection<GatewayModelGroup> ModelGroups { get; } = [];
    public IReadOnlyList<string> SortModeOptions { get; } = ["Provider", "模型名称"];
    public string SelectedSortMode { get => selectedSortMode; set { if (SetProperty(ref selectedSortMode, value)) FilterModels(""); } }
    public GatewayEndpointEditorViewModel? SelectedEndpoint { get => selectedEndpoint; set { if (SetProperty(ref selectedEndpoint, value)) { OnPropertyChanged(nameof(HasSelectedEndpoint)); FilterModels(""); } } }
    public bool HasSelectedEndpoint => SelectedEndpoint is not null;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public ICommand AddRouteCommand { get; }
    public ICommand ToggleEndpointCommand { get; }
    public ICommand ToggleRouteCommand { get; }
    public ICommand RemoveRouteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public GatewayViewModel(ConfigSnapshotService configService, ToastService? toastService = null)
    {
        this.configService = configService;
        this.toastService = toastService ?? new ToastService();
        AddRouteCommand = new AsyncCommand(parameter => AddRouteAsync(parameter as GatewayModelOption));
        ToggleEndpointCommand = new AsyncCommand(parameter => ToggleEndpointAsync(parameter as GatewayEndpointEditorViewModel));
        ToggleRouteCommand = new AsyncCommand(parameter => ToggleRouteAsync(parameter as GatewayRouteEditorViewModel));
        RemoveRouteCommand = new AsyncCommand(parameter => RemoveRouteAsync(parameter as GatewayRouteEditorViewModel));
        MoveUpCommand = new AsyncCommand(parameter => MoveRouteAsync(parameter as GatewayRouteEditorViewModel, -1));
        MoveDownCommand = new AsyncCommand(parameter => MoveRouteAsync(parameter as GatewayRouteEditorViewModel, 1));
        _ = RefreshAsync();
    }

    public void NotifyCopied() => toastService.Show("地址已复制", ToastLevel.Success);

    private async Task RefreshAsync()
    {
        try
        {
            var selectedKey = SelectedEndpoint?.Key;
            var providers = await configService.ListProvidersAsync();
            AvailableModels.Clear();
            foreach (var provider in providers.Where(item => item.Enabled))
                foreach (var model in provider.Models.Where(item => item.Enabled))
                    AvailableModels.Add(new GatewayModelOption(model.Id, model.DisplayName, provider.DisplayName));
            RebuildModelGroups();
            var endpoints = await configService.ListGatewayEndpointsAsync();
            var baseUrl = configService.Load().Server.Urls.FirstOrDefault() ?? "http://127.0.0.1:11434";
            Endpoints.Clear();
            foreach (var endpoint in endpoints) Endpoints.Add(GatewayEndpointEditorViewModel.FromResponse(endpoint, baseUrl));
            SelectedEndpoint = Endpoints.FirstOrDefault(item => item.Key == selectedKey) ?? Endpoints.FirstOrDefault();
            FilterModels("");
            Status = $"已加载 {providers.Count} 个 Provider、{AvailableModels.Count} 个模型和 {Endpoints.Count} 个 Endpoint";
        }
        catch (Exception exception) { Status = $"网关加载失败：{exception.Message}"; }
    }

    public async Task AddRouteAsync(GatewayModelOption? option)
    {
        if (SelectedEndpoint is null || option is null) return;
        try
        {
            var response = await configService.CreateGatewayRouteAsync(SelectedEndpoint.Key, new GatewayRouteInput(option.Id, null, true, SelectedEndpoint.Routes.Count));
            SelectedEndpoint.Routes.Add(GatewayRouteEditorViewModel.FromResponse(response));
            Status = "模型路由已添加";
        }
        catch (Exception exception) { Status = $"添加路由失败：{exception.Message}"; }
    }

    public async Task ToggleModelRouteAsync(GatewayModelOption? option)
    {
        if (SelectedEndpoint is null || option is null) return;
        var existing = SelectedEndpoint.Routes.FirstOrDefault(item => item.ModelId == option.Id);
        if (existing is null) await AddRouteAsync(option);
        else await RemoveRouteAsync(existing);
        option.IsSelected = SelectedEndpoint.Routes.Any(item => item.ModelId == option.Id);
    }

    private async Task ToggleEndpointAsync(GatewayEndpointEditorViewModel? endpoint)
    {
        if (endpoint is null) return;
        try { var response = await configService.SetGatewayEndpointEnabledAsync(endpoint.Key, !endpoint.Enabled); endpoint.Enabled = response.Enabled; Status = $"{endpoint.DisplayName} 已{(endpoint.Enabled ? "启用" : "停用")}"; }
        catch (Exception exception) { Status = $"Endpoint 更新失败：{exception.Message}"; }
    }

    private async Task ToggleRouteAsync(GatewayRouteEditorViewModel? route)
    {
        if (route is null || SelectedEndpoint is null) return;
        try { route.Enabled = !route.Enabled; await SaveRouteAsync(route); Status = "路由状态已保存"; }
        catch (Exception exception) { Status = $"路由更新失败：{exception.Message}"; }
    }

    private async Task RemoveRouteAsync(GatewayRouteEditorViewModel? route)
    {
        if (route is null || SelectedEndpoint is null) return;
        try { await configService.DeleteGatewayRouteAsync(route.Id); SelectedEndpoint.Routes.Remove(route); Renumber(); Status = "模型路由已删除"; }
        catch (Exception exception) { Status = $"删除路由失败：{exception.Message}"; }
    }

    private async Task MoveRouteAsync(GatewayRouteEditorViewModel? route, int delta)
    {
        if (route is null || SelectedEndpoint is null) return;
        var index = SelectedEndpoint.Routes.IndexOf(route); var target = index + delta;
        if (index < 0 || target < 0 || target >= SelectedEndpoint.Routes.Count) return;
        SelectedEndpoint.Routes.Move(index, target); Renumber();
        try { foreach (var item in SelectedEndpoint.Routes) await SaveRouteAsync(item); Status = "路由优先级已保存"; }
        catch (Exception exception) { Status = $"排序保存失败：{exception.Message}"; }
    }

    private async Task SaveRouteAsync(GatewayRouteEditorViewModel route) => await configService.UpdateGatewayRouteAsync(route.Id, new GatewayRouteInput(route.ModelId, route.Alias, route.Enabled, route.SortOrder));
    public async Task SaveRouteChangesAsync(GatewayRouteEditorViewModel route)
    {
        try { await SaveRouteAsync(route); Status = "路由已保存"; }
        catch (Exception exception) { Status = $"路由更新失败：{exception.Message}"; }
    }
    private void Renumber() { for (var i = 0; i < (SelectedEndpoint?.Routes.Count ?? 0); i++) SelectedEndpoint!.Routes[i].SortOrder = i; }

    private void RebuildModelGroups()
    {
        ModelGroups.Clear();
        foreach (var group in AvailableModels.GroupBy(item => item.ProviderName))
            ModelGroups.Add(new GatewayModelGroup(group.Key, group.ToArray()));
    }

    public void FilterModels(string? search)
    {
        var term = search?.Trim() ?? "";
        var routeIds = SelectedEndpoint?.Routes.Select(item => item.ModelId).ToHashSet() ?? [];
        foreach (var item in AvailableModels) item.IsSelected = routeIds.Contains(item.Id);
        ModelGroups.Clear();
        var filtered = AvailableModels.Where(item => string.IsNullOrWhiteSpace(term) || item.ModelName.Contains(term, StringComparison.OrdinalIgnoreCase) || item.ProviderName.Contains(term, StringComparison.OrdinalIgnoreCase));
        var groups = filtered.GroupBy(item => item.ProviderName);
        foreach (var group in (SelectedSortMode == "模型名称" ? groups.OrderBy(item => item.Min(model => model.ModelName), StringComparer.OrdinalIgnoreCase) : groups.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)))
            ModelGroups.Add(new GatewayModelGroup(group.Key, SelectedSortMode == "模型名称" ? group.OrderBy(item => item.ModelName, StringComparer.OrdinalIgnoreCase).ToArray() : group.OrderBy(item => item.ModelName, StringComparer.OrdinalIgnoreCase).ToArray()));
    }
}

public sealed class GatewayEndpointEditorViewModel : NotifyViewModel
{
    private bool enabled;
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string PublicPath { get; init; } = "";
    public string PublicUrl { get; init; } = "";
    public ObservableCollection<GatewayRouteEditorViewModel> Routes { get; } = [];
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public static GatewayEndpointEditorViewModel FromResponse(GatewayEndpointResponse response, string baseUrl)
    {
        var value = new GatewayEndpointEditorViewModel { Key = response.Key, DisplayName = response.DisplayName, PublicPath = response.PublicPath, PublicUrl = $"{baseUrl.TrimEnd('/')}{response.PublicPath}", Enabled = response.Enabled };
        foreach (var route in response.Routes) value.Routes.Add(GatewayRouteEditorViewModel.FromResponse(route));
        return value;
    }
}

public sealed class GatewayRouteEditorViewModel : NotifyViewModel
{
    private bool enabled;
    private int sortOrder;
    public Guid Id { get; init; }
    public Guid ModelId { get; init; }
    public string ModelName { get; init; } = "";
    public string ProviderName { get; init; } = "";
    public string? Alias { get; set; }
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public int SortOrder { get => sortOrder; set => SetProperty(ref sortOrder, value); }
    public static GatewayRouteEditorViewModel FromResponse(GatewayRouteResponse response) => new() { Id = response.Id, ModelId = response.ModelId, ModelName = response.ModelName, ProviderName = response.ProviderName, Alias = response.Alias, Enabled = response.Enabled, SortOrder = response.SortOrder };
}

public sealed class GatewayModelOption : NotifyViewModel
{
    private bool isSelected;
    public Guid Id { get; }
    public string ModelName { get; }
    public string ProviderName { get; }
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public GatewayModelOption(Guid id, string modelName, string providerName) => (Id, ModelName, ProviderName) = (id, modelName, providerName);
    public override string ToString() => $"{ProviderName} / {ModelName}";
}

public sealed class GatewayModelGroup : NotifyViewModel
{
    private bool isExpanded = true;
    public string ProviderName { get; }
    public IReadOnlyList<GatewayModelOption> Models { get; }
    public int ModelCount => Models.Count;
    public bool IsExpanded { get => isExpanded; set { if (isExpanded == value) return; isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandGlyph)); } }
    public string ExpandGlyph => IsExpanded ? "⌃" : "⌄";
    public GatewayModelGroup(string providerName, IReadOnlyList<GatewayModelOption> models) => (ProviderName, Models) = (providerName, models);
}
