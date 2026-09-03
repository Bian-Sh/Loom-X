using System.Collections.ObjectModel;
using System.Windows.Input;
using OllamaHub.Configuration;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class GatewayViewModel : NotifyViewModel, IDisposable
{
    private readonly AppDataStore dataStore;
    private readonly ToastService toastService;
    private GatewayEndpointEditorViewModel? selectedEndpoint;
    private GatewayComboEditorViewModel? selectedCombo;
    private GatewayRouteEditorViewModel? draggingRoute;
    private GatewayRouteEditorViewModel? dragPlaceholder;
    private GatewayComboEditorViewModel? dragOwnerCombo;
    private int dragOriginIndex = -1;
    private string status = "";
    private bool isModelSortAscending = true;
    private string modelSearchTerm = "";
    public ObservableCollection<GatewayEndpointEditorViewModel> Endpoints { get; } = [];
    public ObservableCollection<GatewayModelOption> AvailableModels { get; } = [];
    public ObservableCollection<GatewayModelGroup> ModelGroups { get; } = [];
    public bool IsModelSortAscending
    {
        get => isModelSortAscending;
        private set
        {
            if (!SetProperty(ref isModelSortAscending, value)) return;
            OnPropertyChanged(nameof(IsModelSortDescending));
            OnPropertyChanged(nameof(ModelSortToolTip));
        }
    }
    public bool IsModelSortDescending => !IsModelSortAscending;
    public string ModelSortToolTip => IsModelSortAscending ? "按字母降序排序" : "按字母升序排序";
    public GatewayEndpointEditorViewModel? SelectedEndpoint { get => selectedEndpoint; set { if (SetProperty(ref selectedEndpoint, value)) { SelectedCombo = null; OnPropertyChanged(nameof(HasSelectedEndpoint)); FilterModels(""); } } }
    public GatewayComboEditorViewModel? SelectedCombo { get => selectedCombo; private set { if (SetProperty(ref selectedCombo, value)) FilterModels(""); } }
    public GatewayRouteEditorViewModel? DraggingRoute { get => draggingRoute; private set { if (SetProperty(ref draggingRoute, value)) OnPropertyChanged(nameof(IsRouteDragActive)); } }
    public bool IsRouteDragActive => DraggingRoute is not null;
    public bool HasSelectedEndpoint => SelectedEndpoint is not null;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public ICommand AddComboCommand { get; }
    public ICommand ToggleEndpointCommand { get; }
    public ICommand ToggleComboCommand { get; }
    public ICommand RemoveComboCommand { get; }
    public ICommand ToggleRouteCommand { get; }
    public ICommand RemoveRouteCommand { get; }

    public GatewayViewModel(AppDataStore dataStore, ToastService? toastService = null)
    {
        this.dataStore = dataStore;
        this.toastService = toastService ?? new ToastService();
        AddComboCommand = new AsyncCommand(_ => AddComboAsync());
        ToggleEndpointCommand = new AsyncCommand(parameter => ToggleEndpointAsync(parameter as GatewayEndpointEditorViewModel));
        ToggleComboCommand = new AsyncCommand(parameter => ToggleComboAsync(parameter as GatewayComboEditorViewModel));
        RemoveComboCommand = new AsyncCommand(parameter => RemoveComboAsync(parameter as GatewayComboEditorViewModel));
        ToggleRouteCommand = new AsyncCommand(parameter => ToggleRouteAsync(parameter as GatewayRouteEditorViewModel));
        RemoveRouteCommand = new AsyncCommand(parameter => RemoveRouteAsync(parameter as GatewayRouteEditorViewModel));
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        _ = RefreshAsync();
    }

    public GatewayViewModel(ConfigSnapshotService configService, ToastService? toastService = null)
        : this(new AppDataStore(configService, new GatewayProcessService()), toastService) { }

    public void NotifyCopied() => toastService.Show("地址已复制", ToastLevel.Success);
    private async Task RefreshAsync()
    {
        try
        {
            var selectedKey = SelectedEndpoint?.Key;
            await dataStore.InitializeAsync();
            var providers = dataStore.Providers;
            await ReloadAvailableModelsAsync();
            var endpoints = dataStore.GatewayEndpoints;
            var baseUrl = dataStore.CurrentConfig.Server.Urls.FirstOrDefault() ?? "http://127.0.0.1:11434";
            Endpoints.Clear();
            foreach (var endpoint in endpoints) Endpoints.Add(GatewayEndpointEditorViewModel.FromResponse(endpoint, baseUrl));
            SelectedEndpoint = Endpoints.FirstOrDefault(item => item.Key == selectedKey) ?? Endpoints.FirstOrDefault();
            FilterModels("");
            Status = $"已加载 {providers.Count} 个 Provider、{AvailableModels.Count} 个模型和 {Endpoints.Count} 个 Endpoint";
        }
        catch (Exception exception) { Status = $"网关加载失败：{exception.Message}"; }
    }

    private void OnConfigurationChanged(object? sender, EventArgs args) => _ = RefreshAsync();

    private async Task AddComboAsync()
    {
        if (SelectedEndpoint is null) return;
        try
        {
            var name = "新 Combo"; var index = 2;
            while (SelectedEndpoint.Combos.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"新 Combo {index++}";
            var response = await dataStore.CreateGatewayComboAsync(SelectedEndpoint.Key, new GatewayComboInput(name, true, SelectedEndpoint.Combos.Count));
            var combo = GatewayComboEditorViewModel.FromResponse(response); combo.IsExpanded = true;
            SelectedEndpoint.Combos.Add(combo); SelectedCombo = combo; Status = "Combo 模型已添加";
        }
        catch (Exception exception) { Status = $"添加 Combo 模型失败：{exception.Message}"; }
    }

    public async Task SaveComboChangesAsync(GatewayComboEditorViewModel? combo)
    {
        if (combo is null) return;
        try { combo.ApplyResponse(await dataStore.UpdateGatewayComboAsync(combo.Id, new GatewayComboInput(combo.Name, combo.Enabled, combo.SortOrder))); Status = "Combo 模型已保存"; }
        catch (Exception exception) { Status = $"保存 Combo 模型失败：{exception.Message}"; }
    }
    public void SelectCombo(GatewayComboEditorViewModel? combo) => SelectedCombo = combo;
    public GatewayRouteEditorViewModel? FindSelectedRoute(Guid id) => SelectedCombo?.Routes.FirstOrDefault(item => !item.IsPlaceholder && item.Id == id);

    public bool BeginRouteDrag(GatewayRouteEditorViewModel? route)
    {
        if (route is null || route.IsPlaceholder || DraggingRoute is not null || SelectedCombo is null || !SelectedCombo.CanDragRoutes) return false;
        var combo = SelectedCombo;
        var index = combo.Routes.IndexOf(route);
        if (index < 0) return false;

        dragOriginIndex = index;
        dragPlaceholder = GatewayRouteEditorViewModel.CreatePlaceholder();
        dragOwnerCombo = combo;
        dragOwnerCombo.Routes.RemoveAt(index);
        dragOwnerCombo.Routes.Insert(index, dragPlaceholder);
        dragOwnerCombo.IsDragPreviewOwner = true;
        route.IsDragging = true;
        DraggingRoute = route;
        return true;
    }

    public bool MoveRouteDragPlaceholder(int targetIndex)
    {
        if (dragOwnerCombo is null || dragPlaceholder is null) return false;
        var currentIndex = dragOwnerCombo.Routes.IndexOf(dragPlaceholder);
        if (currentIndex < 0) return false;
        var clampedIndex = Math.Clamp(targetIndex, 0, dragOwnerCombo.Routes.Count - 1);
        if (currentIndex == clampedIndex) return false;
        dragOwnerCombo.Routes.Move(currentIndex, clampedIndex);
        return true;
    }

    public async Task CompleteRouteDragAsync()
    {
        if (dragOwnerCombo is null || DraggingRoute is null || dragPlaceholder is null) return;
        var combo = dragOwnerCombo;
        var targetIndex = combo.Routes.IndexOf(dragPlaceholder);
        if (targetIndex < 0) { CancelRouteDrag(); return; }

        combo.Routes.RemoveAt(targetIndex);
        DraggingRoute.IsDragging = false;
        combo.Routes.Insert(targetIndex, DraggingRoute);
        ClearRouteDragState();
        Renumber(combo);
        try
        {
            foreach (var item in combo.Routes.Where(item => !item.IsPlaceholder)) await SaveRouteAsync(item);
            Status = "故障转移顺序已保存";
        }
        catch (Exception exception) { Status = $"保存排序失败：{exception.Message}"; }
    }

    public void CancelRouteDrag()
    {
        if (dragOwnerCombo is null || DraggingRoute is null || dragPlaceholder is null) return;
        var placeholderIndex = dragOwnerCombo.Routes.IndexOf(dragPlaceholder);
        if (placeholderIndex >= 0) dragOwnerCombo.Routes.RemoveAt(placeholderIndex);
        var restoreIndex = Math.Clamp(dragOriginIndex, 0, dragOwnerCombo.Routes.Count);
        DraggingRoute.IsDragging = false;
        dragOwnerCombo.Routes.Insert(restoreIndex, DraggingRoute);
        ClearRouteDragState();
    }

    private void ClearRouteDragState()
    {
        if (dragOwnerCombo is not null) dragOwnerCombo.IsDragPreviewOwner = false;
        DraggingRoute = null;
        dragPlaceholder = null;
        dragOwnerCombo = null;
        dragOriginIndex = -1;
    }
    public void ToggleModelSortDirection()
    {
        IsModelSortAscending = !IsModelSortAscending;
        FilterModels(modelSearchTerm);
    }
    public async Task<bool> PrepareModelPickerAsync(GatewayComboEditorViewModel? combo)
    {
        if (combo is null) return false;
        SelectedCombo = combo;
        try
        {
            await ReloadAvailableModelsAsync();
            FilterModels("");
            Status = AvailableModels.Count == 0 ? "没有可加入的已启用模型，请先在 Provider 中启用模型" : $"已加载 {AvailableModels.Count} 个可加入模型";
            return true;
        }
        catch (Exception exception)
        {
            AvailableModels.Clear();
            ModelGroups.Clear();
            Status = $"加载可加入模型失败：{exception.Message}";
            return false;
        }
    }

    private async Task ReloadAvailableModelsAsync()
    {
        var models = await dataStore.ListEnabledGatewayModelsAsync();
        AvailableModels.Clear();
        foreach (var model in models) AvailableModels.Add(new GatewayModelOption(model.Id, model.ModelName, model.ProviderName));
    }
    public async Task AddRouteAsync(GatewayModelOption? option)
    {
        if (SelectedCombo is null || option is null) return;
        try { SelectedCombo.Routes.Add(GatewayRouteEditorViewModel.FromResponse(await dataStore.CreateGatewayRouteAsync(SelectedCombo.Id, new GatewayRouteInput(option.Id, true, SelectedCombo.Routes.Count)))); Status = "模型已加入 Combo"; }
        catch (Exception exception) { Status = $"加入 Combo 失败：{exception.Message}"; }
    }
    public async Task ToggleModelRouteAsync(GatewayModelOption? option)
    {
        if (SelectedCombo is null || option is null) return;
        var existing = SelectedCombo.Routes.FirstOrDefault(item => item.ModelId == option.Id);
        if (existing is null) await AddRouteAsync(option); else await RemoveRouteAsync(existing);
        option.IsSelected = SelectedCombo.Routes.Any(item => item.ModelId == option.Id);
    }
    private async Task ToggleEndpointAsync(GatewayEndpointEditorViewModel? endpoint)
    {
        if (endpoint is null) return;
        try { endpoint.Enabled = (await dataStore.SetGatewayEndpointEnabledAsync(endpoint.Key, !endpoint.Enabled)).Enabled; Status = $"{endpoint.DisplayName} 已{(endpoint.Enabled ? "启用" : "停用")}"; }
        catch (Exception exception) { Status = $"Endpoint 更新失败：{exception.Message}"; }
    }
    private async Task ToggleComboAsync(GatewayComboEditorViewModel? combo) { if (combo is null) return; combo.Enabled = !combo.Enabled; await SaveComboChangesAsync(combo); }
    private async Task RemoveComboAsync(GatewayComboEditorViewModel? combo)
    {
        if (combo is null || SelectedEndpoint is null) return;
        try { await dataStore.DeleteGatewayComboAsync(combo.Id); SelectedEndpoint.Combos.Remove(combo); if (SelectedCombo == combo) SelectedCombo = null; Status = "Combo 模型已移除"; }
        catch (Exception exception) { Status = $"移除 Combo 模型失败：{exception.Message}"; }
    }
    private async Task ToggleRouteAsync(GatewayRouteEditorViewModel? route) { if (route is null) return; route.Enabled = !route.Enabled; await SaveRouteAsync(route); Status = "成员状态已保存"; }
    private async Task RemoveRouteAsync(GatewayRouteEditorViewModel? route)
    {
        if (route is null || SelectedCombo is null) return;
        try { await dataStore.DeleteGatewayRouteAsync(route.Id); SelectedCombo.Routes.Remove(route); Renumber(SelectedCombo); Status = "模型已从 Combo 移除"; }
        catch (Exception exception) { Status = $"移除成员失败：{exception.Message}"; }
    }
    public async Task MoveRouteAsync(GatewayRouteEditorViewModel? route, GatewayRouteEditorViewModel? target)
    {
        if (route is null || target is null || route == target || SelectedCombo is null) return;
        var from = SelectedCombo.Routes.IndexOf(route); var to = SelectedCombo.Routes.IndexOf(target); if (from < 0 || to < 0) return;
        SelectedCombo.Routes.Move(from, to); Renumber(SelectedCombo);
        try { foreach (var item in SelectedCombo.Routes) await SaveRouteAsync(item); Status = "故障转移顺序已保存"; }
        catch (Exception exception) { Status = $"保存排序失败：{exception.Message}"; }
    }
    private Task SaveRouteAsync(GatewayRouteEditorViewModel route) => dataStore.UpdateGatewayRouteAsync(route.Id, new GatewayRouteInput(route.ModelId, route.Enabled, route.SortOrder));
    private static void Renumber(GatewayComboEditorViewModel combo) { for (var i = 0; i < combo.Routes.Count; i++) combo.Routes[i].SortOrder = i; }
    public void FilterModels(string? search)
    {
        modelSearchTerm = search?.Trim() ?? "";
        var routeIds = SelectedCombo?.Routes.Where(item => !item.IsPlaceholder).Select(item => item.ModelId).ToHashSet() ?? [];
        foreach (var item in AvailableModels) item.IsSelected = routeIds.Contains(item.Id);
        ModelGroups.Clear();
        var filtered = AvailableModels.Where(item => item.MatchesSearch(modelSearchTerm));
        var grouped = filtered.GroupBy(item => item.ProviderName);
        var orderedGroups = IsModelSortAscending
            ? grouped.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            : grouped.OrderByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in orderedGroups)
        {
            var models = IsModelSortAscending
                ? group.OrderBy(item => item.ModelName, StringComparer.OrdinalIgnoreCase)
                : group.OrderByDescending(item => item.ModelName, StringComparer.OrdinalIgnoreCase);
            ModelGroups.Add(new GatewayModelGroup(group.Key, models.ToArray()));
        }
    }

    public void Dispose() => dataStore.ConfigurationChanged -= OnConfigurationChanged;
}

public sealed class GatewayEndpointEditorViewModel : NotifyViewModel
{
    private bool enabled;
    public string Key { get; init; } = ""; public string DisplayName { get; init; } = ""; public string PublicPath { get; init; } = ""; public string PublicUrl { get; init; } = "";
    public ObservableCollection<GatewayComboEditorViewModel> Combos { get; } = [];
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public static GatewayEndpointEditorViewModel FromResponse(GatewayEndpointResponse response, string baseUrl)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var publicUrl = string.Equals(response.Key, "ollama", StringComparison.OrdinalIgnoreCase)
            ? normalizedBaseUrl
            : $"{normalizedBaseUrl}{response.PublicPath}";
        var value = new GatewayEndpointEditorViewModel { Key = response.Key, DisplayName = response.DisplayName, PublicPath = response.PublicPath, PublicUrl = publicUrl, Enabled = response.Enabled };
        foreach (var combo in response.Combos) value.Combos.Add(GatewayComboEditorViewModel.FromResponse(combo)); return value;
    }
}
public sealed class GatewayComboEditorViewModel : NotifyViewModel
{
    private string name = ""; private bool enabled; private bool isExpanded = true; private int sortOrder; private bool isDragPreviewOwner;
    public Guid Id { get; init; } public string Name { get => name; set => SetProperty(ref name, value); } public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public double ExpandIconAngle => IsExpanded ? 90 : 0; public bool IsExpanded { get => isExpanded; set { if (SetProperty(ref isExpanded, value)) OnPropertyChanged(nameof(ExpandIconAngle)); } } public int SortOrder { get => sortOrder; set => SetProperty(ref sortOrder, value); }
    public bool IsDragPreviewOwner { get => isDragPreviewOwner; set => SetProperty(ref isDragPreviewOwner, value); }
    public bool CanDragRoutes => Routes.Count > 1;
    public ObservableCollection<GatewayRouteEditorViewModel> Routes { get; } = [];
    public GatewayComboEditorViewModel() => Routes.CollectionChanged += (_, _) => UpdateDragAvailability();
    private void UpdateDragAvailability()
    {
        var canDrag = CanDragRoutes;
        foreach (var route in Routes.Where(item => item.IsRealRoute)) route.IsDragEnabled = canDrag;
        OnPropertyChanged(nameof(CanDragRoutes));
    }
    public static GatewayComboEditorViewModel FromResponse(GatewayComboResponse response) { var value = new GatewayComboEditorViewModel { Id = response.Id, Name = response.Name, Enabled = response.Enabled, SortOrder = response.SortOrder }; foreach (var route in response.Routes) value.Routes.Add(GatewayRouteEditorViewModel.FromResponse(route)); return value; }
    public void ApplyResponse(GatewayComboResponse response) { Name = response.Name; Enabled = response.Enabled; SortOrder = response.SortOrder; }
}
public sealed class GatewayRouteEditorViewModel : NotifyViewModel
{
    private bool enabled; private int sortOrder; private bool isPlaceholder; private bool isDragging; private bool isDragEnabled;
    public Guid Id { get; init; } public Guid ModelId { get; init; } public string ModelName { get; init; } = ""; public string ProviderName { get; init; } = ""; public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); } public int SortOrder { get => sortOrder; set => SetProperty(ref sortOrder, value); }
    public bool IsPlaceholder { get => isPlaceholder; private init => isPlaceholder = value; }
    public bool IsRealRoute => !IsPlaceholder;
    public bool IsDragEnabled { get => isDragEnabled; set => SetProperty(ref isDragEnabled, value); }
    public bool IsDragging { get => isDragging; set => SetProperty(ref isDragging, value); }
    public static GatewayRouteEditorViewModel FromResponse(GatewayRouteResponse response) => new() { Id = response.Id, ModelId = response.ModelId, ModelName = response.ModelName, ProviderName = response.ProviderName, Enabled = response.Enabled, SortOrder = response.SortOrder };
    public static GatewayRouteEditorViewModel CreatePlaceholder() => new() { IsPlaceholder = true };
}
public sealed class GatewayModelOption : NotifyViewModel
{
    private bool isSelected;
    public Guid Id { get; } public string ModelName { get; } public string ProviderName { get; } public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public GatewayModelOption(Guid id, string modelName, string providerName) => (Id, ModelName, ProviderName) = (id, modelName, providerName);
    public bool MatchesSearch(string searchTerm) => string.IsNullOrWhiteSpace(searchTerm) || ModelName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
}
public sealed class GatewayModelGroup : NotifyViewModel
{
    private bool isExpanded = true;
    public string ProviderName { get; }
    public IReadOnlyList<GatewayModelOption> Models { get; }
    public int ModelCount => Models.Count;
    public double ExpandIconAngle => IsExpanded ? 90 : 0;
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (SetProperty(ref isExpanded, value)) OnPropertyChanged(nameof(ExpandIconAngle));
        }
    }
    public GatewayModelGroup(string providerName, IReadOnlyList<GatewayModelOption> models) => (ProviderName, Models) = (providerName, models);
}
