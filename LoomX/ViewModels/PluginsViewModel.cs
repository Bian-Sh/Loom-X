using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX.Localization;
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
    private PluginRuntime? runtime;
    private bool isLoading;
    private string status = string.Empty;
    private int diagnosticCount;
    private bool hasLoadError;
    private string statusKey = "plugins.status.waiting";
    private int? statusCount;
    private bool disposed;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];
    public ObservableCollection<string> Diagnostics { get; } = [];
    public ICommand RefreshCommand { get; }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public int PluginCount => Plugins.Count;
    public int EnabledPluginCount => Plugins.Count(item => item.Enabled);
    public int DiagnosticCount { get => diagnosticCount; private set => SetProperty(ref diagnosticCount, value); }
    public bool HasPlugins => Plugins.Count > 0;
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public bool HasLoadError { get => hasLoadError; private set => SetProperty(ref hasLoadError, value); }
    public bool IsEmpty => !IsLoading && !HasLoadError && !HasPlugins;
    public string PluginCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.summary.loaded"), PluginCount);
    public string EnabledCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.summary.enabled"), EnabledPluginCount);
    public string DiagnosticCountLabel => string.Format(CultureInfo.CurrentCulture, Loc("plugins.summary.diagnostics"), DiagnosticCount);

    public PluginsViewModel(
        GatewayProcessService gatewayService,
        ToastService? toastService = null,
        ILogger<PluginsViewModel>? logger = null,
        IStringLocalizer<PluginsViewModel>? localizer = null)
    {
        this.gatewayService = gatewayService;
        this.toastService = toastService ?? new ToastService();
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginsViewModel>.Instance;
        this.localizer = localizer ?? LocalizerFactory.Create<PluginsViewModel>();
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsLoading, this.logger);
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
            runtime = gatewayService.GetHostedService<PluginRuntime>();
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

    private void Rebuild(PluginRuntime pluginRuntime)
    {
        Plugins.Clear();
        foreach (var info in pluginRuntime.PluginInfos.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var isCredentialProtection = string.Equals(info.Id, CredentialProtectionPluginId, StringComparison.Ordinal);
            Plugins.Add(new PluginItemViewModel(
                info,
                isCredentialProtection,
                SetPluginEnabled,
                Loc));
        }

        Diagnostics.Clear();
        foreach (var diagnostic in pluginRuntime.Diagnostics)
            Diagnostics.Add(diagnostic);
        DiagnosticCount = Diagnostics.Count;
        RaiseSummaryChanged();
    }

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
        foreach (var plugin in Plugins)
            plugin.RefreshLocalization(Loc);
        SetStatus(statusKey, statusCount);
        OnPropertyChanged(nameof(PluginCountLabel));
        OnPropertyChanged(nameof(EnabledCountLabel));
        OnPropertyChanged(nameof(DiagnosticCountLabel));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LocaleService.CultureChanged -= OnCultureChanged;
    }
}

public sealed class PluginItemViewModel : NotifyViewModel
{
    private readonly Action<PluginItemViewModel, bool> setEnabled;
    private bool enabled;
    private Func<string, string> loc;

    public string Id { get; }
    public string Version { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public int ExtensionCount { get; }
    public bool IsCredentialProtection { get; }
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
        Action<PluginItemViewModel, bool> setEnabled,
        Func<string, string> localize)
    {
        Id = info.Id;
        Version = info.Version;
        Capabilities = info.Capabilities;
        ExtensionCount = info.ExtensionCount;
        enabled = info.Enabled;
        IsCredentialProtection = isCredentialProtection;
        this.setEnabled = setEnabled;
        loc = localize;
    }

    internal void ApplyEnabled(bool value)
    {
        if (!SetProperty(ref enabled, value, nameof(Enabled))) return;
        OnPropertyChanged(nameof(StatusLabel));
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
