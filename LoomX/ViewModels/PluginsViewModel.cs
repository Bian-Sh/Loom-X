using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX.Localization;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;

namespace LoomX.ViewModels;

public sealed class PluginsViewModel : NotifyViewModel, IDisposable
{
    private const string CredentialProtectionPluginId = "loomx.credential-protection";

    private readonly GatewayProcessService gatewayService;
    private readonly ToastService toastService;
    private readonly ILogger<PluginsViewModel> logger;
    private readonly IStringLocalizer<PluginsViewModel> localizer;
    private readonly Action<Action> uiDispatcher;
    private readonly PluginUiRefreshQueue uiRefreshQueue;
    private PluginRuntime? runtime;
    private bool isLoading;
    private string status = string.Empty;
    private int diagnosticCount;
    private bool hasLoadError;
    private string statusKey = "plugins.status.waiting";
    private int? statusCount;
    private bool disposed;
    private PluginItemViewModel? selectedPlugin;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];
    public ObservableCollection<string> Diagnostics { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand BackToPluginListCommand { get; }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public int PluginCount => Plugins.Count;
    public int EnabledPluginCount => Plugins.Count(item => item.Enabled);
    public int DiagnosticCount { get => diagnosticCount; private set => SetProperty(ref diagnosticCount, value); }
    public bool HasPlugins => Plugins.Count > 0;
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public bool HasLoadError { get => hasLoadError; private set => SetProperty(ref hasLoadError, value); }
    public bool IsEmpty => !IsLoading && !HasLoadError && !HasPlugins;
    public PluginItemViewModel? SelectedPlugin
    {
        get => selectedPlugin;
        private set
        {
            if (!SetProperty(ref selectedPlugin, value)) return;
            OnPropertyChanged(nameof(IsPluginListVisible));
            OnPropertyChanged(nameof(IsPluginDetailVisible));
        }
    }
    public bool IsPluginListVisible => SelectedPlugin is null;
    public bool IsPluginDetailVisible => SelectedPlugin is not null;
    public string PluginCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.summary.loaded"), PluginCount);
    public string EnabledCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.summary.enabled"), EnabledPluginCount);
    public string DiagnosticCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.summary.diagnostics"), DiagnosticCount);

    public PluginsViewModel(
        GatewayProcessService gatewayService,
        ToastService? toastService = null,
        ILogger<PluginsViewModel>? logger = null,
        IStringLocalizer<PluginsViewModel>? localizer = null,
        Action<Action>? uiDispatcher = null)
    {
        this.gatewayService = gatewayService;
        this.toastService = toastService ?? new ToastService();
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginsViewModel>.Instance;
        this.localizer = localizer ?? LocalizerFactory.Create<PluginsViewModel>();
        this.uiDispatcher = uiDispatcher ?? DispatchToUiThread;
        uiRefreshQueue = new PluginUiRefreshQueue(this.uiDispatcher, pluginId =>
            RefreshPluginUi(pluginId, CultureInfo.CurrentUICulture.Name));
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsLoading, this.logger);
        BackToPluginListCommand = new DelegateCommand(ShowPluginList);
        LocaleService.CultureChanged += OnCultureChanged;
        SetStatus("plugins.status.waiting");
    }

    public async Task RefreshAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        HasLoadError = false;
        RaiseSummaryChanged();
        (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        SetStatus("plugins.status.loading");
        try
        {
            await gatewayService.EnsureHostedServicesAsync();
            AttachRuntime(gatewayService.GetHostedService<PluginRuntime>());
            if (runtime is null)
                throw new InvalidOperationException(Loc("plugins.status.runtime_unavailable"));

            Rebuild(runtime);
            SetStatus("plugins.status.ready", PluginCount);
            logger.LogInformation(
                "插件页面刷新完成，插件 {PluginCount} 个，启用 {EnabledPluginCount} 个，诊断 {DiagnosticCount} 条",
                PluginCount,
                EnabledPluginCount,
                DiagnosticCount);
        }
        catch (Exception exception)
        {
            HasLoadError = true;
            SetStatus("plugins.status.failed");
            logger.LogError(exception, "插件页面刷新失败");
            toastService.Show(Loc("plugins.toast.refresh_failed"), ToastLevel.Error);
        }
        finally
        {
            IsLoading = false;
            RaiseSummaryChanged();
            (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void AttachRuntime(PluginRuntime? pluginRuntime)
    {
        if (ReferenceEquals(runtime, pluginRuntime)) return;
        if (runtime is not null)
            runtime.PluginUiInvalidated -= OnPluginUiInvalidated;
        runtime = pluginRuntime;
        if (runtime is not null)
            runtime.PluginUiInvalidated += OnPluginUiInvalidated;
    }

    private void OnPluginUiInvalidated(object? sender, PluginUiInvalidatedEventArgs args) =>
        uiRefreshQueue.Enqueue(args.PluginId);

    private void RefreshPluginUi(string pluginId, string cultureName)
    {
        if (runtime is null) return;
        var plugin = Plugins.FirstOrDefault(item => item.Id == pluginId);
        if (plugin is null) return;

        plugin.ApplyUiContributions(
            runtime.GetUiContributions(pluginId, PluginUiSlot.CardBody, cultureName),
            runtime.GetUiContributions(pluginId, PluginUiSlot.DetailBody, cultureName));
        if (ReferenceEquals(SelectedPlugin, plugin) && !plugin.CanOpenDetail)
            ShowPluginList();
    }

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void Rebuild(PluginRuntime pluginRuntime)
    {
        var selectedId = SelectedPlugin?.Id;
        Plugins.Clear();
        foreach (var info in pluginRuntime.PluginInfos.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var isCredentialProtection = string.Equals(info.Id, CredentialProtectionPluginId, StringComparison.Ordinal);
            var cardContributions = pluginRuntime.GetUiContributions(
                info.Id,
                PluginUiSlot.CardBody,
                CultureInfo.CurrentUICulture.Name);
            var detailContributions = pluginRuntime.GetUiContributions(
                info.Id,
                PluginUiSlot.DetailBody,
                CultureInfo.CurrentUICulture.Name);
            Plugins.Add(new PluginItemViewModel(
                info,
                isCredentialProtection,
                cardContributions,
                detailContributions,
                SetPluginEnabled,
                OpenPluginDetail,
                Loc));
        }

        if (selectedId is not null)
        {
            var replacement = Plugins.FirstOrDefault(item => item.Id == selectedId && item.CanOpenDetail);
            SelectedPlugin = replacement;
        }

        Diagnostics.Clear();
        foreach (var diagnostic in pluginRuntime.Diagnostics)
            Diagnostics.Add(diagnostic);
        DiagnosticCount = Diagnostics.Count;
        RaiseSummaryChanged();
    }

    internal void OpenPluginDetail(PluginItemViewModel item)
    {
        SelectedPlugin = item.CanOpenDetail ? item : null;
    }

    private void ShowPluginList() => SelectedPlugin = null;

    private void SetPluginEnabled(PluginItemViewModel item, bool enabled)
    {
        if (runtime is null || !item.CanToggle || item.Enabled == enabled) return;

        if (!runtime.SetPluginEnabled(item.Id, enabled))
        {
            toastService.Show(Loc("plugins.toast.toggle_failed"), ToastLevel.Error);
            return;
        }

        item.ApplyEnabled(enabled);
        RaiseSummaryChanged();
        var message = string.Format(
            CultureInfo.CurrentCulture,
            Loc(enabled ? "plugins.toast.enabled" : "plugins.toast.disabled"),
            item.DisplayName);
        toastService.Show(message, enabled ? ToastLevel.Success : ToastLevel.Warning);
        logger.LogInformation("插件页面已{State}插件 {PluginId}", enabled ? "启用" : "禁用", item.Id);
    }

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(PluginCount));
        OnPropertyChanged(nameof(EnabledPluginCount));
        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PluginCountLabel));
        OnPropertyChanged(nameof(EnabledCountLabel));
        OnPropertyChanged(nameof(DiagnosticCountLabel));
    }

    private string Loc(string key) => localizer[key]?.Value ?? key;

    private void SetStatus(string key, int? count = null)
    {
        statusKey = key;
        statusCount = count;
        Status = count is null
            ? Loc(key)
            : string.Format(CultureInfo.CurrentCulture, Loc(key), count.Value);
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        uiDispatcher(() =>
        {
            foreach (var plugin in Plugins)
            {
                plugin.RefreshLocalization(Loc);
                RefreshPluginUi(plugin.Id, culture.Name);
            }
            SetStatus(statusKey, statusCount);
            OnPropertyChanged(nameof(PluginCountLabel));
            OnPropertyChanged(nameof(EnabledCountLabel));
            OnPropertyChanged(nameof(DiagnosticCountLabel));
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LocaleService.CultureChanged -= OnCultureChanged;
        if (runtime is not null)
            runtime.PluginUiInvalidated -= OnPluginUiInvalidated;
        uiRefreshQueue.Dispose();
    }
}

public sealed class PluginItemViewModel : NotifyViewModel
{
    private readonly Action<PluginItemViewModel, bool> setEnabled;
    private readonly Action<PluginItemViewModel> openDetail;
    private bool enabled;
    private Func<string, string> loc;

    public string Id { get; }
    public string Version { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public int ExtensionCount { get; }
    public bool IsCredentialProtection { get; }
    public IReadOnlyList<PluginUiContribution> CardContributions { get; private set; }
    public IReadOnlyList<PluginUiContribution> DetailContributions { get; private set; }
    public bool HasCardUi => CardContributions.Count > 0;
    public bool HasDetailUi { get; }
    public bool CanOpenDetail => HasDetailUi && DetailContributions.Count > 0;
    public ICommand OpenDetailCommand { get; }
    public bool IsSystemPlugin => IsCredentialProtection;
    public bool CanToggle => !IsCredentialProtection;
    public string DisplayName => IsCredentialProtection ? Loc("plugins.credential.name") : Id;
    public string Category => IsCredentialProtection ? Loc("plugins.category.system") : Loc("plugins.category.extension");
    public string Description => IsCredentialProtection ? Loc("plugins.credential.description") : Loc("plugins.generic.description");
    public string LifecycleNote => IsCredentialProtection ? Loc("plugins.credential.lifecycle") : Loc("plugins.generic.lifecycle");
    public string StatusLabel => Loc(Enabled ? "plugins.status.enabled" : "plugins.status.disabled");
    public string ExtensionCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.extension_count"), ExtensionCount);
    public string CapabilitySummary => Capabilities.Count == 0 ? Loc("plugins.capabilities.none") : string.Join("  ·  ", Capabilities);

    public bool Enabled
    {
        get => enabled;
        set => setEnabled(this, value);
    }

    public PluginItemViewModel(
        LoadedPluginInfo info,
        bool isCredentialProtection,
        IReadOnlyList<PluginUiContribution> cardContributions,
        IReadOnlyList<PluginUiContribution> detailContributions,
        Action<PluginItemViewModel, bool> setEnabled,
        Action<PluginItemViewModel> openDetail,
        Func<string, string> localize)
    {
        Id = info.Id;
        Version = info.Version;
        Capabilities = info.Capabilities;
        ExtensionCount = info.ExtensionCount;
        enabled = info.Enabled;
        IsCredentialProtection = isCredentialProtection;
        CardContributions = cardContributions;
        DetailContributions = detailContributions;
        HasDetailUi = info.HasDetailUi;
        this.setEnabled = setEnabled;
        this.openDetail = openDetail;
        OpenDetailCommand = new DelegateCommand(() => this.openDetail(this));
        loc = localize;
    }

    internal void ApplyEnabled(bool value)
    {
        if (!SetProperty(ref enabled, value, nameof(Enabled))) return;
        OnPropertyChanged(nameof(StatusLabel));
    }

    internal void ApplyUiContributions(
        IReadOnlyList<PluginUiContribution> cardContributions,
        IReadOnlyList<PluginUiContribution> detailContributions)
    {
        CardContributions = cardContributions;
        DetailContributions = detailContributions;
        OnPropertyChanged(nameof(CardContributions));
        OnPropertyChanged(nameof(DetailContributions));
        OnPropertyChanged(nameof(HasCardUi));
        OnPropertyChanged(nameof(CanOpenDetail));
    }

    internal void RefreshLocalization(Func<string, string> localize)
    {
        loc = localize;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(LifecycleNote));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ExtensionCountLabel));
        OnPropertyChanged(nameof(CapabilitySummary));
    }

    private string Loc(string key) => loc(key);
}

internal sealed class PluginUiRefreshQueue : IDisposable
{
    private readonly Action<Action> dispatch;
    private readonly Action<string> refresh;
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private bool disposed;

    public PluginUiRefreshQueue(Action<Action> dispatch, Action<string> refresh)
    {
        this.dispatch = dispatch;
        this.refresh = refresh;
    }

    public void Enqueue(string pluginId)
    {
        lock (gate)
        {
            if (disposed || !pending.Add(pluginId)) return;
        }

        dispatch(() =>
        {
            lock (gate)
            {
                pending.Remove(pluginId);
                if (disposed) return;
            }
            refresh(pluginId);
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending.Clear();
        }
    }
}
