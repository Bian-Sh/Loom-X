using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using LoomX.Configuration;
using LoomX.Services;

namespace LoomX.ViewModels;

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
    private readonly SemaphoreSlim gatewayMutationLock = new(1, 1);
    private int gatewayMutationDepth;
    private bool refreshPending;
    private bool isRefreshing;
    public ObservableCollection<GatewayEndpointEditorViewModel> Endpoints { get; } = [];
    public ObservableCollection<GatewayComboEditorViewModel> Combos { get; } = [];
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
    public GatewayEndpointEditorViewModel? SelectedEndpoint { get => selectedEndpoint; set { if (SetProperty(ref selectedEndpoint, value)) OnPropertyChanged(nameof(HasSelectedEndpoint)); } }
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
    public ICommand RotateGatewayApiKeyCommand { get; }
    public ICommand SaveGatewayReasoningEffortCommand { get; }

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
        RotateGatewayApiKeyCommand = new AsyncCommand(parameter => RotateGatewayApiKeyAsync(parameter as GatewayEndpointEditorViewModel));
        SaveGatewayReasoningEffortCommand = new AsyncCommand(parameter => SaveGatewayReasoningEffortAsync(parameter as GatewayEndpointEditorViewModel));
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        _ = RefreshAsync();
    }

    public GatewayViewModel(ConfigSnapshotService configService, ToastService? toastService = null)
        : this(new AppDataStore(configService, new GatewayProcessService()), toastService) { }

    public void NotifyCopied() => toastService.Show("地址已复制", ToastLevel.Success);
    public void NotifyApiKeyCopied() => toastService.Show("API Key 已复制", ToastLevel.Success);
    private async Task RefreshAsync()
    {
        await gatewayMutationLock.WaitAsync();
        try
        {
            if (gatewayMutationDepth > 0)
            {
                refreshPending = true;
                return;
            }

            await RefreshCoreAsync();
        }
        finally { gatewayMutationLock.Release(); }
    }

    private async Task RefreshCoreAsync()
    {
        if (isRefreshing) return;
        isRefreshing = true;
        try
        {
            var selectedKey = SelectedEndpoint?.Key;
            var selectedComboId = SelectedCombo?.Id;
            var selectedComboExpanded = SelectedCombo?.IsExpanded ?? true;
            await dataStore.InitializeAsync();
            await ReloadAvailableModelsAsync();
            var endpoints = dataStore.GatewayEndpoints;
            var comboResponses = dataStore.GatewayCombos;
            var baseUrl = dataStore.CurrentConfig.Server.Urls.FirstOrDefault() ?? "http://127.0.0.1:11434";
            Combos.Clear();
            foreach (var combo in comboResponses) Combos.Add(GatewayComboEditorViewModel.FromResponse(combo));
            SelectedCombo = selectedComboId is { } comboId ? Combos.FirstOrDefault(item => item.Id == comboId) : null;
            if (SelectedCombo is not null) SelectedCombo.IsExpanded = selectedComboExpanded;
            Endpoints.Clear();
            foreach (var endpoint in endpoints) Endpoints.Add(GatewayEndpointEditorViewModel.FromResponse(endpoint, baseUrl, Combos));
            SelectedEndpoint = Endpoints.FirstOrDefault(item => item.Key == selectedKey) ?? Endpoints.FirstOrDefault();
            FilterModels("");
            Status = $"已加载 {dataStore.Providers.Count} 个 Provider、{AvailableModels.Count} 个模型、{Endpoints.Count} 个 Endpoint 和 {Combos.Count} 个 Combo";
        }
        catch (Exception exception) { Status = $"网关加载失败：{exception.Message}"; }
        finally
        {
            isRefreshing = false;
            refreshPending = false;
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        if (gatewayMutationDepth > 0 || isRefreshing)
        {
            refreshPending = true;
            return;
        }

        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }

    private async Task AddComboAsync()
    {
        try
        {
            var name = "新 Combo"; var index = 2;
            while (Combos.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"新 Combo {index++}";
            GatewayComboResponse? response = null;
            await RunGatewayMutationAsync(async () => response = await dataStore.CreateGatewayComboAsync(new GatewayComboInput(name, true, Combos.Count)));
            SelectedCombo = response is null ? null : Combos.FirstOrDefault(item => item.Id == response.Id);
            if (SelectedCombo is not null) SelectedCombo.IsExpanded = true;
            Status = "全局 Combo 已添加";
        }
        catch (Exception exception) { Status = $"添加 Combo 模型失败：{exception.Message}"; }
    }

    public async Task SaveComboChangesAsync(GatewayComboEditorViewModel? combo)
    {
        if (combo is null) return;
        var comboId = combo.Id;
        var saved = false;
        try
        {
            await RunGatewayMutationAsync(async () =>
            {
                if (!IsCurrentCombo(combo) || !combo.HasPendingChanges) return;
                var response = await dataStore.UpdateGatewayComboAsync(comboId, new GatewayComboInput(combo.Name, combo.Enabled, combo.SortOrder));
                FindCurrentCombo(comboId)?.ApplyResponse(response);
                saved = true;
            });
            if (saved) Status = "Combo 模型已保存";
        }
        catch (Exception exception) { Status = $"保存 Combo 模型失败：{exception.Message}"; }
    }
    public void SelectCombo(GatewayComboEditorViewModel? combo) => SelectedCombo = combo;
    public GatewayRouteEditorViewModel? FindSelectedRoute(Guid id) => SelectedCombo?.Routes.FirstOrDefault(item => !item.IsPlaceholder && item.Id == id);

    public async Task ToggleEndpointComboAsync(GatewayComboBindingOption? option)
    {
        if (option?.Owner is not { } endpoint) return;
        var previous = option.IsSelected;
        option.IsSelected = !previous;
        try
        {
            var selected = endpoint.ComboOptions.Where(item => item.IsSelected).Select(item => item.ComboId).ToArray();
            GatewayEndpointResponse? response = null;
            await RunGatewayMutationAsync(async () => response = await dataStore.UpdateGatewayEndpointComboBindingsAsync(endpoint.Key, new GatewayEndpointComboSelectionInput(selected)));
            if (response is null) return;
            endpoint.ApplyBindings(response, Combos);
            Status = $"{endpoint.DisplayName} 的 Combo 暴露范围已保存";
        }
        catch (Exception exception)
        {
            option.IsSelected = previous;
            Status = $"Endpoint Combo 更新失败：{exception.Message}";
            toastService.Show("Endpoint Combo 更新失败", ToastLevel.Error);
        }
    }

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
        var updates = CreateRouteSaveRequests(combo);
        try
        {
            await RunGatewayMutationAsync(async () =>
            {
                foreach (var update in updates) await SaveRouteAsync(update.Id, update.Input);
            });
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
        if (SelectedCombo is null || option is null || !IsCurrentCombo(SelectedCombo)) return;
        try
        {
            GatewayRouteResponse? response = null;
            await RunGatewayMutationAsync(async () =>
            {
                var combo = SelectedCombo is { } selected && FindCurrentCombo(selected.Id) is { } current ? current : null;
                if (combo is null) return;
                response = await dataStore.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(option.Id, true, combo.Routes.Count));
            });
            if (response is not null && FindCurrentCombo(response.ComboId) is { } combo && !combo.Routes.Any(item => item.Id == response.Id))
                combo.Routes.Add(GatewayRouteEditorViewModel.FromResponse(response));
            Status = "模型已加入 Combo";
        }
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
    private async Task RotateGatewayApiKeyAsync(GatewayEndpointEditorViewModel? endpoint)
    {
        if (endpoint is null || !endpoint.IsApiKeyVisible) return;
        try
        {
            endpoint.ApplyResponse(await dataStore.RotateGatewayApiKeyAsync(endpoint.Key));
            Status = $"{endpoint.DisplayName} API Key 已重新生成";
            toastService.Show("API Key 已重新生成", ToastLevel.Success);
        }
        catch (Exception exception)
        {
            Status = $"API Key 重新生成失败：{exception.Message}";
            toastService.Show("API Key 重新生成失败", ToastLevel.Error);
        }
    }

    public async Task SaveGatewayReasoningEffortAsync(GatewayEndpointEditorViewModel? endpoint)
    {
        if (endpoint is null || !endpoint.IsOllamaEndpoint) return;
        try
        {
            endpoint.ApplyResponse(await dataStore.UpdateGatewayEndpointReasoningEffortAsync(endpoint.Key, endpoint.ReasoningEffort));
            endpoint.MarkReasoningEffortSaved();
            Status = $"{endpoint.DisplayName} Reasoning effort 已保存";
            toastService.Show("Reasoning effort 已保存", ToastLevel.Success);
        }
        catch (Exception exception)
        {
            Status = $"Reasoning effort 保存失败：{exception.Message}";
            toastService.Show("Reasoning effort 保存失败", ToastLevel.Error);
        }
    }
    private async Task ToggleComboAsync(GatewayComboEditorViewModel? combo)
    {
        if (combo is null) return;
        var saved = false;
        try
        {
            await RunGatewayMutationAsync(async () =>
            {
                var current = FindCurrentCombo(combo.Id);
                if (current is null) return;
                current.Enabled = !current.Enabled;
                var response = await dataStore.UpdateGatewayComboAsync(current.Id, new GatewayComboInput(current.Name, current.Enabled, current.SortOrder));
                FindCurrentCombo(current.Id)?.ApplyResponse(response);
                saved = true;
            });
            if (saved) Status = "Combo 模型已保存";
        }
        catch (Exception exception) { Status = $"保存 Combo 模型失败：{exception.Message}"; }
    }
    private async Task RemoveComboAsync(GatewayComboEditorViewModel? combo)
    {
        if (combo is null || !IsCurrentCombo(combo)) return;
        try
        {
            var comboId = combo.Id;
            await RunGatewayMutationAsync(() => dataStore.DeleteGatewayComboAsync(comboId));
            if (FindCurrentCombo(comboId) is { } current) Combos.Remove(current);
            if (SelectedCombo?.Id == comboId) SelectedCombo = null;
            Status = "全局 Combo 已移除";
        }
        catch (Exception exception) { Status = $"移除 Combo 模型失败：{exception.Message}"; }
    }
    private async Task ToggleRouteAsync(GatewayRouteEditorViewModel? route)
    {
        if (route is null) return;
        try
        {
            await RunGatewayMutationAsync(async () =>
            {
                var current = FindCurrentRoute(route.Id);
                if (current is null) return;
                current.Enabled = !current.Enabled;
                await SaveRouteAsync(current);
            });
            Status = "成员状态已保存";
        }
        catch (Exception exception) { Status = $"成员保存失败：{exception.Message}"; }
    }
    private async Task RemoveRouteAsync(GatewayRouteEditorViewModel? route)
    {
        if (route is null || SelectedCombo is null || !IsCurrentRoute(route)) return;
        var comboId = SelectedCombo.Id;
        var routeId = route.Id;
        try
        {
            await RunGatewayMutationAsync(() => dataStore.DeleteGatewayRouteAsync(routeId));
            if (FindCurrentCombo(comboId) is { } combo)
            {
                if (FindCurrentRoute(routeId) is { } currentRoute) combo.Routes.Remove(currentRoute);
                Renumber(combo);
            }
            Status = "模型已从 Combo 移除";
        }
        catch (Exception exception) { Status = $"移除成员失败：{exception.Message}"; }
    }
    public async Task MoveRouteAsync(GatewayRouteEditorViewModel? route, GatewayRouteEditorViewModel? target)
    {
        if (route is null || target is null || route == target || SelectedCombo is null || !IsCurrentCombo(SelectedCombo)) return;
        var from = SelectedCombo.Routes.IndexOf(route); var to = SelectedCombo.Routes.IndexOf(target); if (from < 0 || to < 0) return;
        var combo = SelectedCombo;
        SelectedCombo.Routes.Move(from, to); Renumber(SelectedCombo);
        var updates = CreateRouteSaveRequests(combo);
        try
        {
            await RunGatewayMutationAsync(async () =>
            {
                foreach (var update in updates) await SaveRouteAsync(update.Id, update.Input);
            });
            Status = "故障转移顺序已保存";
        }
        catch (Exception exception) { Status = $"保存排序失败：{exception.Message}"; }
    }
    private Task SaveRouteAsync(GatewayRouteEditorViewModel route) => SaveRouteAsync(route.Id, new GatewayRouteInput(route.ModelId, route.Enabled, route.SortOrder));
    private Task SaveRouteAsync(Guid routeId, GatewayRouteInput input) => dataStore.UpdateGatewayRouteAsync(routeId, input);
    private static IReadOnlyList<RouteSaveRequest> CreateRouteSaveRequests(GatewayComboEditorViewModel combo) =>
        combo.Routes.Where(item => !item.IsPlaceholder)
            .Select(item => new RouteSaveRequest(item.Id, new GatewayRouteInput(item.ModelId, item.Enabled, item.SortOrder)))
            .ToArray();
    private static void Renumber(GatewayComboEditorViewModel combo) { for (var i = 0; i < combo.Routes.Count; i++) combo.Routes[i].SortOrder = i; }

    private GatewayComboEditorViewModel? FindCurrentCombo(Guid id) => Combos.FirstOrDefault(item => item.Id == id);
    private GatewayRouteEditorViewModel? FindCurrentRoute(Guid id) => SelectedCombo?.Routes.FirstOrDefault(item => !item.IsPlaceholder && item.Id == id);
    private bool IsCurrentCombo(GatewayComboEditorViewModel combo) => FindCurrentCombo(combo.Id) is not null;
    private bool IsCurrentRoute(GatewayRouteEditorViewModel route) => FindCurrentRoute(route.Id) is not null;

    private async Task RunGatewayMutationAsync(Func<Task> operation)
    {
        await gatewayMutationLock.WaitAsync();
        gatewayMutationDepth++;
        try { await operation(); }
        finally
        {
            if (gatewayMutationDepth == 1 && refreshPending)
            {
                refreshPending = false;
                await RefreshCoreAsync();
            }
            gatewayMutationDepth--;
            gatewayMutationLock.Release();
        }
    }
    private readonly record struct RouteSaveRequest(Guid Id, GatewayRouteInput Input);
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

    public void Dispose()
    {
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        gatewayMutationLock.Dispose();
    }
}

public sealed class GatewayEndpointEditorViewModel : NotifyViewModel
{
    private bool enabled;
    private string apiKey = "";
    private string reasoningEffort = GatewayEndpointSettings.DefaultReasoningEffort;
    private bool isHydrated;
    private bool reasoningEffortDirty;
    private bool isComboPickerOpen;
    public string Key { get; init; } = ""; public string DisplayName { get; init; } = ""; public string PublicPath { get; init; } = ""; public string PublicUrl { get; init; } = "";
    public ObservableCollection<GatewayComboBindingOption> ComboOptions { get; } = [];
    public int SelectedComboCount => ComboOptions.Count(item => item.IsSelected);
    public string SelectedComboSummary => SelectedComboCount == 0 ? "未暴露 Combo" : string.Join("、", ComboOptions.Where(item => item.IsSelected).Select(item => item.Name));
    public bool IsComboPickerOpen { get => isComboPickerOpen; set => SetProperty(ref isComboPickerOpen, value); }
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public string ApiKey { get => apiKey; private set { if (SetProperty(ref apiKey, value)) OnPropertyChanged(nameof(MaskedApiKey)); } }
    public string MaskedApiKey => MaskApiKey(ApiKey);
    public bool IsApiKeyVisible => GatewayEndpointSettings.RequiresApiKey(Key);
    public bool IsOllamaEndpoint => string.Equals(Key, "ollama", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<string> ReasoningEffortOptions => GatewayEndpointSettings.ReasoningEfforts;
    public string ReasoningEffort
    {
        get => reasoningEffort;
        set
        {
            if (!SetProperty(ref reasoningEffort, GatewayEndpointSettings.NormalizeReasoningEffort(value))) return;
            if (isHydrated) reasoningEffortDirty = true;
        }
    }
    public bool IsHydrated => isHydrated;
    public bool HasPendingReasoningEffortChange => reasoningEffortDirty;
    public static GatewayEndpointEditorViewModel FromResponse(GatewayEndpointResponse response, string baseUrl, IReadOnlyList<GatewayComboEditorViewModel> combos)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var publicUrl = string.Equals(response.Key, "ollama", StringComparison.OrdinalIgnoreCase)
            ? normalizedBaseUrl
            : $"{normalizedBaseUrl}{response.PublicPath}";
        var value = new GatewayEndpointEditorViewModel { Key = response.Key, DisplayName = response.DisplayName, PublicPath = response.PublicPath, PublicUrl = publicUrl, Enabled = response.Enabled, ApiKey = response.ApiKey ?? "", reasoningEffort = GatewayEndpointSettings.NormalizeReasoningEffort(response.ReasoningEffort) };
        value.ApplyBindings(response, combos);
        value.isHydrated = true;
        return value;
    }
    public static GatewayEndpointEditorViewModel FromResponse(GatewayEndpointResponse response, string baseUrl) => FromResponse(response, baseUrl, []);
    public void ApplyResponse(GatewayEndpointResponse response)
    {
        Enabled = response.Enabled;
        ApiKey = response.ApiKey ?? ApiKey;
        ReasoningEffort = response.ReasoningEffort;
    }
    public void ApplyBindings(GatewayEndpointResponse response, IReadOnlyList<GatewayComboEditorViewModel> combos)
    {
        var selected = response.Combos.ToDictionary(item => item.ComboId);
        ComboOptions.Clear();
        foreach (var combo in combos.OrderBy(item => item.SortOrder))
        {
            selected.TryGetValue(combo.Id, out var binding);
            ComboOptions.Add(new GatewayComboBindingOption(this, combo.Id, combo.Name, combo.Enabled, binding is not null && binding.Enabled));
        }
        OnPropertyChanged(nameof(SelectedComboCount));
        OnPropertyChanged(nameof(SelectedComboSummary));
    }
    internal void OnComboOptionChanged()
    {
        OnPropertyChanged(nameof(SelectedComboCount));
        OnPropertyChanged(nameof(SelectedComboSummary));
    }
    public void MarkReasoningEffortSaved() => reasoningEffortDirty = false;
    private static string MaskApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未生成";
        if (value.Length <= 8) return new string('•', value.Length);
        return $"{value[..4]}••••{value[^4..]}";
    }
}
public sealed class GatewayComboBindingOption : NotifyViewModel
{
    private bool isSelected;
    public GatewayEndpointEditorViewModel Owner { get; }
    public Guid ComboId { get; }
    public string Name { get; }
    public bool ComboEnabled { get; }
    public bool IsSelected { get => isSelected; set { if (SetProperty(ref isSelected, value)) Owner.OnComboOptionChanged(); } }
    public string StatusText => ComboEnabled ? "" : "全局停用";
    public GatewayComboBindingOption(GatewayEndpointEditorViewModel owner, Guid comboId, string name, bool comboEnabled, bool isSelected)
    {
        Owner = owner;
        ComboId = comboId;
        Name = name;
        ComboEnabled = comboEnabled;
        this.isSelected = isSelected;
    }
}
public sealed class GatewayComboEditorViewModel : NotifyViewModel
{
    private string name = ""; private string savedName = ""; private bool enabled; private bool savedEnabled; private bool isExpanded = true; private int sortOrder; private bool isDragPreviewOwner;
    public Guid Id { get; init; }
    public string Name { get => name; set { if (SetProperty(ref name, value)) OnPropertyChanged(nameof(HasPendingChanges)); } }
    public bool Enabled { get => enabled; set { if (SetProperty(ref enabled, value)) OnPropertyChanged(nameof(HasPendingChanges)); } }
    public bool HasPendingChanges => !string.Equals(name, savedName, StringComparison.Ordinal) || enabled != savedEnabled;
    public double ExpandIconAngle => IsExpanded ? 90 : 0; public bool IsExpanded { get => isExpanded; set { if (SetProperty(ref isExpanded, value)) OnPropertyChanged(nameof(ExpandIconAngle)); } } public int SortOrder { get => sortOrder; set => SetProperty(ref sortOrder, value); }
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
    public static GatewayComboEditorViewModel FromResponse(GatewayComboResponse response) { var value = new GatewayComboEditorViewModel { Id = response.Id }; value.ApplyResponse(response); foreach (var route in response.Routes) value.Routes.Add(GatewayRouteEditorViewModel.FromResponse(route)); return value; }
    public void ApplyResponse(GatewayComboResponse response) { Name = response.Name; Enabled = response.Enabled; SortOrder = response.SortOrder; savedName = name; savedEnabled = enabled; OnPropertyChanged(nameof(HasPendingChanges)); }
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
