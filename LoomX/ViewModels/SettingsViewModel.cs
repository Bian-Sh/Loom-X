using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Net;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LoomX;
using LoomX.Configuration;
using LoomX.Services;
using LoomX.Localization;
using LoomX.Logging;

namespace LoomX.ViewModels;

public sealed class SettingOption : INotifyPropertyChanged
{
    private readonly string? stableDisplayName;

    public SettingOption(string value, string localizationKey, string? stableDisplayName = null)
    {
        Value = value;
        LocalizationKey = localizationKey;
        this.stableDisplayName = stableDisplayName;
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public string Value { get; }
    public string LocalizationKey { get; }
    public string DisplayName => GetDisplayName(LocaleService.CurrentCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal string GetDisplayName(CultureInfo culture) => stableDisplayName ?? ResourceLookup.Resolve(LocalizationKey, culture);
    internal void NotifyDisplayNameChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    private void OnCultureChanged(object? sender, CultureInfo culture) => NotifyDisplayNameChanged();

    public override string ToString() => DisplayName;
}

public sealed class SettingsViewModel : NotifyViewModel, IDisposable
{
    private const string AcrylicTransparencyAlgorithm = "acrylic";
    private readonly AppDataStore dataStore;
    private readonly ToastService toastService;
    private readonly UpdateCoordinator updateCoordinator;
    private readonly bool ownsUpdateCoordinator;
    private readonly ILogger<SettingsViewModel> logger;
    private readonly Action<bool, int, int, string>? applyAppearance;
    private readonly IStringLocalizer<SettingsViewModel> _loc;
    private SettingOption selectedLanguage = LanguageOptions[0];
    private SettingOption selectedTheme = ThemeOptions[0];
    private SettingOption selectedProxyMode = ProxyModeOptions[0];
    private string proxyHost = "http://127.0.0.1";
    private int proxyPort = 7890;
    private string proxyUsername = "";
    private string proxyPassword = "";
    private bool clearProxyPassword;
    private bool autoCheckUpdates = true;
    private bool useProxyForUpdates = true;
    private bool diagnosticsEnabled;
    private bool logStackTrace;
    private bool transparencyEnabled = true;
    private int transparencyOpacity = 86;
    private int blurAmount = 24;
    private SettingOption selectedLogRetention = LogRetentionOptions[1];
    private bool isBusy;
    private string status;
    private bool hasProxyPassword;
    private bool suppressAutoSave;
    private CancellationTokenSource? autoSaveCancellation;

    public static IReadOnlyList<SettingOption> LanguageOptions { get; } =
    [
        new("zh-CN", "settings.option.language.zh-CN", "简体中文"),
        new("zh-TW", "settings.option.language.zh-TW", "繁體中文"),
        new("en-US", "settings.option.language.en-US", "English"),
        new("ja-JP", "settings.option.language.ja-JP", "日本語")
    ];
    public static IReadOnlyList<SettingOption> ThemeOptions { get; } = [new("system", "settings.option.theme.system"), new("dark", "settings.option.theme.dark"), new("light", "settings.option.theme.light")];
    public static IReadOnlyList<SettingOption> ProxyModeOptions { get; } = [new("direct", "settings.option.proxy.direct"), new("system", "settings.option.proxy.system"), new("custom", "settings.option.proxy.custom")];
    public static IReadOnlyList<SettingOption> LogRetentionOptions { get; } = [new("7", "settings.option.retention.7"), new("30", "settings.option.retention.30"), new("90", "settings.option.retention.90"), new("365", "settings.option.retention.365"), new("3650", "settings.option.retention.3650")];

    public SettingOption SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            if (!SetProperty(ref selectedLanguage, value)) return;
            LocaleService.SetCulture(value.Value);
            QueueAutoSave();
        }
    }
    public SettingOption SelectedTheme { get => selectedTheme; set { if (SetProperty(ref selectedTheme, value)) QueueAutoSave(); } }
    public SettingOption SelectedProxyMode
    {
        get => selectedProxyMode;
        set
        {
            if (!SetProperty(ref selectedProxyMode, value)) return;
            OnPropertyChanged(nameof(IsCustomProxyVisible));
            OnPropertyChanged(nameof(ProxyStatus));
            QueueAutoSave();
        }
    }
    public string ProxyHost { get => proxyHost; set { if (SetProperty(ref proxyHost, value)) { OnPropertyChanged(nameof(ProxyStatus)); QueueAutoSave(); } } }
    public int ProxyPort { get => proxyPort; set { if (SetProperty(ref proxyPort, value)) { OnPropertyChanged(nameof(ProxyStatus)); QueueAutoSave(); } } }
    public string ProxyUsername { get => proxyUsername; set { if (SetProperty(ref proxyUsername, value)) QueueAutoSave(); } }
    public string ProxyPassword { get => proxyPassword; set { if (SetProperty(ref proxyPassword, value)) QueueAutoSave(); } }
    public bool ClearProxyPassword { get => clearProxyPassword; set { if (SetProperty(ref clearProxyPassword, value)) QueueAutoSave(); } }
    public bool HasProxyPassword { get => hasProxyPassword; private set => SetProperty(ref hasProxyPassword, value); }
    public bool AutoCheckUpdates { get => autoCheckUpdates; set { if (SetProperty(ref autoCheckUpdates, value)) QueueAutoSave(); } }
    public bool UseProxyForUpdates { get => useProxyForUpdates; set { if (SetProperty(ref useProxyForUpdates, value)) QueueAutoSave(); } }
    public bool DiagnosticsEnabled { get => diagnosticsEnabled; set { if (SetProperty(ref diagnosticsEnabled, value)) QueueAutoSave(); } }
    public bool LogStackTrace { get => logStackTrace; set { if (SetProperty(ref logStackTrace, value)) { LoggingBootstrap.SetIncludeStackTrace(value); QueueAutoSave(); } } }
    public bool TransparencyEnabled { get => transparencyEnabled; set { if (SetProperty(ref transparencyEnabled, value)) { if (!suppressAutoSave) ApplyAppearancePreview(); QueueAutoSave(); } } }
    public int TransparencyOpacity { get => transparencyOpacity; set { if (SetProperty(ref transparencyOpacity, value)) { if (!suppressAutoSave) ApplyAppearancePreview(); QueueAutoSave(); } } }
    public int BlurAmount { get => blurAmount; set { if (SetProperty(ref blurAmount, value)) { if (!suppressAutoSave) ApplyAppearancePreview(); QueueAutoSave(); } } }
    public SettingOption SelectedLogRetention { get => selectedLogRetention; set { if (SetProperty(ref selectedLogRetention, value)) OnPropertyChanged(nameof(LogRetentionDays)); } }
    public int LogRetentionDays => int.Parse(SelectedLogRetention.Value);
    public bool IsBusy { get => isBusy; private set { if (SetProperty(ref isBusy, value)) { OnPropertyChanged(nameof(IsNotBusy)); } } }
    public bool IsNotBusy => !IsBusy;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string VersionLabel => AppVersion.Label;
    public string DataDirectory => AppDataPaths.RootDirectory;
    public bool IsCustomProxyVisible => SelectedProxyMode.Value == "custom";
    public string ProxyStatus => SelectedProxyMode.Value switch
    {
        "direct" => Loc("settings.proxy.test.direct.success"),
        "system" => Loc("settings.proxy.test.system.success"),
        _ => $"{Loc("settings.proxy.mode.label")}: {ProxyHost}:{ProxyPort}"
    };

    public ICommand LoadCommand { get; }
    public ICommand TestProxyCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenDataDirectoryCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }

    public SettingsViewModel(AppDataStore dataStore, ILogger<SettingsViewModel>? logger = null, ToastService? toastService = null, Action<bool, int, int, string>? applyAppearance = null, UpdateCoordinator? updateCoordinator = null, IStringLocalizer<SettingsViewModel>? localizer = null)
    {
        this.dataStore = dataStore;
        this.logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        this.toastService = toastService ?? new ToastService();
        this.updateCoordinator = updateCoordinator ?? new UpdateCoordinator(dataStore);
        ownsUpdateCoordinator = updateCoordinator is null;
        this.applyAppearance = applyAppearance;
        _loc = localizer ?? LocalizerFactory.Create<SettingsViewModel>();
        Status = Loc("settings.status.loading");
        LoadCommand = new AsyncCommand(LoadAsync);
        TestProxyCommand = new AsyncCommand(TestProxyAsync);
        CheckUpdateCommand = new AsyncCommand(CheckUpdateAsync);
        OpenDataDirectoryCommand = new AsyncCommand(OpenDataDirectoryAsync);
        ClearLogsCommand = new AsyncCommand(ClearLogsAsync);
        ExportDiagnosticsCommand = new AsyncCommand(ExportDiagnosticsAsync);
        dataStore.ConfigurationChanged += OnConfigurationChanged;
        LocaleService.CultureChanged += OnCultureChanged;
        _ = LoadAsync();
    }

    public SettingsViewModel(ConfigSnapshotService configService, ILogger<SettingsViewModel>? logger = null, ToastService? toastService = null, Action<bool, int, int, string>? applyAppearance = null)
        : this(new AppDataStore(configService, new GatewayProcessService()), logger, toastService, applyAppearance, null) { }

    private string Loc(string key) => _loc[key]?.Value ?? key;

    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = Loc("settings.status.loading");
        try
        {
            suppressAutoSave = true;
            await dataStore.InitializeAsync();
            var settings = dataStore.Settings ?? throw new InvalidOperationException("设置快照尚未就绪。");
            SelectedLanguage = FindOption(LanguageOptions, settings.Language, LanguageOptions[0]);
            SelectedTheme = FindOption(ThemeOptions, settings.Theme, ThemeOptions[0]);
            SelectedProxyMode = FindOption(ProxyModeOptions, settings.ProxyMode, ProxyModeOptions[0]);
            AutoCheckUpdates = settings.AutoCheckUpdates;
            UseProxyForUpdates = settings.UseProxyForUpdates;
            DiagnosticsEnabled = settings.DiagnosticsEnabled;
            LogStackTrace = settings.LogStackTrace;
            ProxyHost = settings.ProxyHost;
            ProxyPort = settings.ProxyPort;
            ProxyUsername = settings.ProxyUsername ?? "";
            ProxyPassword = "";
            ClearProxyPassword = false;
            HasProxyPassword = settings.HasProxyPassword;
            SelectedLogRetention = FindOption(LogRetentionOptions, settings.LogRetentionDays.ToString(), LogRetentionOptions[1]);
            TransparencyEnabled = settings.TransparencyEnabled;
            TransparencyOpacity = settings.TransparencyOpacity;
            BlurAmount = settings.BlurAmount;
            Status = Loc("settings.status.loaded");
            logger.LogInformation("设置加载完成 {ProxyMode} {AutoCheckUpdates} {UseProxyForUpdates}", settings.ProxyMode, settings.AutoCheckUpdates, settings.UseProxyForUpdates);
        }
        catch (Exception exception)
        {
            Status = string.Format(Loc("settings.status.load.failed"), exception.Message);
            logger.LogError(exception, "设置加载失败");
        }
        finally { suppressAutoSave = false; IsBusy = false; }
    }

    private async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = Loc("settings.status.saving");
        try
        {
            var input = new AppSettingsInput(
                SelectedLanguage.Value,
                SelectedTheme.Value,
                SelectedProxyMode.Value,
                ProxyHost,
                ProxyPort,
                string.IsNullOrWhiteSpace(ProxyUsername) ? null : ProxyUsername,
                string.IsNullOrWhiteSpace(ProxyPassword) ? null : ProxyPassword,
                ClearProxyPassword,
                AutoCheckUpdates,
                "stable",
                DiagnosticsEnabled,
                LogRetentionDays,
                LogStackTrace,
                TransparencyEnabled,
                TransparencyOpacity,
                BlurAmount,
                AcrylicTransparencyAlgorithm,
                UseProxyForUpdates);
            var response = await dataStore.UpdateSettingsAsync(input, cancellationToken);
            HasProxyPassword = response.HasProxyPassword;
            ProxyPassword = "";
            ClearProxyPassword = false;
            Status = string.Format(Loc("settings.status.saved"), DateTime.Now.ToString("HH:mm:ss"));
            logger.LogInformation("设置保存完成 {ProxyMode} {AutoCheckUpdates} {UseProxyForUpdates} {DiagnosticsEnabled}", response.ProxyMode, response.AutoCheckUpdates, response.UseProxyForUpdates, response.DiagnosticsEnabled);
        }
        catch (Exception exception)
        {
            Status = string.Format(Loc("settings.status.save.failed"), exception.Message);
            logger.LogError(exception, "设置保存失败");
        }
        finally { IsBusy = false; }
    }

    private void QueueAutoSave()
    {
        if (suppressAutoSave) return;
        autoSaveCancellation?.Cancel();
        autoSaveCancellation?.Dispose();
        autoSaveCancellation = new CancellationTokenSource();
        var token = autoSaveCancellation.Token;
        Status = Loc("settings.status.waiting");
        _ = AutoSaveAfterDelayAsync(token);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await SaveAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task TestProxyAsync()
    {
        if (IsBusy) return;
        if (SelectedProxyMode.Value == "direct") { Status = Loc("settings.proxy.test.direct.success"); toastService.Show(Loc("settings.proxy.test.toast.success"), ToastLevel.Success); logger.LogInformation("代理测试完成 {ProxyMode}", SelectedProxyMode.Value); return; }
        if (SelectedProxyMode.Value == "system") { Status = Loc("settings.proxy.test.system.success"); toastService.Show(Loc("settings.proxy.test.toast.success"), ToastLevel.Success); logger.LogInformation("代理测试完成 {ProxyMode}", SelectedProxyMode.Value); return; }
        if (!Uri.TryCreate(ProxyHost?.Trim(), UriKind.Absolute, out var proxyUri) || proxyUri.Scheme is not ("http" or "https") || ProxyPort is < 1 or > 65535)
        {
            Status = Loc("settings.proxy.test.invalid");
            toastService.Show(Loc("settings.proxy.test.invalid.toast"), ToastLevel.Warning);
            logger.LogWarning("代理测试配置无效 {ProxyMode}", SelectedProxyMode.Value);
            return;
        }

        IsBusy = true;
        Status = Loc("settings.proxy.test.running");
        try
        {
            using var handler = new HttpClientHandler { Proxy = new WebProxy($"{proxyUri.Scheme}://{proxyUri.Host}:{ProxyPort}"), UseProxy = true };
            if (!string.IsNullOrWhiteSpace(ProxyUsername)) handler.Proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("https://www.example.com", HttpCompletionOption.ResponseHeadersRead);
            var statusCode = (int)response.StatusCode;
            Status = response.IsSuccessStatusCode
                ? string.Format(Loc("settings.proxy.test.success"), statusCode)
                : string.Format(Loc("settings.proxy.test.response"), statusCode);
            toastService.Show(
                response.IsSuccessStatusCode ? Loc("settings.proxy.test.toast.success") : Loc("settings.proxy.test.toast.response"),
                response.IsSuccessStatusCode ? ToastLevel.Success : ToastLevel.Warning);
            logger.LogInformation("代理测试完成 {ProxyMode} {StatusCode}", SelectedProxyMode.Value, statusCode);
        }
        catch (Exception exception) { Status = string.Format(Loc("settings.proxy.test.exception"), exception.Message); toastService.Show(Loc("settings.proxy.test.toast.failed"), ToastLevel.Error); logger.LogWarning(exception, "代理测试失败 {ProxyMode}", SelectedProxyMode.Value); }
        finally { IsBusy = false; }
    }

    private async Task CheckUpdateAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = Loc("settings.update.check.status.running");
        try
        {
            var result = await updateCoordinator.CheckNowAsync(true);
            Status = result?.Latest is null
                ? string.Format(Loc("settings.update.check.status.latest"), VersionLabel)
                : string.Format(Loc("settings.update.check.status.found"), result.Latest.Version);
            toastService.Show(
                result?.Latest is null ? Loc("settings.update.check.toast.latest") : string.Format(Loc("settings.update.check.toast.found"), result.Latest.Version),
                result?.Latest is null ? ToastLevel.Info : ToastLevel.Success);
        }
        finally { IsBusy = false; }
    }

    private Task OpenDataDirectoryAsync()
    {
        try
        {
            AppDataPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo { FileName = AppDataPaths.RootDirectory, UseShellExecute = true });
            Status = Loc("settings.local.data.opened");
            logger.LogInformation("本地数据目录已打开");
        }
        catch (Exception exception) { Status = string.Format(Loc("settings.local.data.open.failed"), exception.Message); logger.LogError(exception, "打开数据目录失败"); }
        return Task.CompletedTask;
    }

    private Task ClearLogsAsync()
    {
        try
        {
            AppDataPaths.EnsureCreated();
            var files = Directory.EnumerateFiles(AppDataPaths.LogDirectory, "*.log").ToArray();
            foreach (var file in files) File.Delete(file);
            Status = files.Length == 0 ? Loc("settings.local.data.logs.empty") : string.Format(Loc("settings.local.data.logs.cleared"), files.Length);
            logger.LogInformation("日志清理完成 {FileCount}", files.Length);
        }
        catch (Exception exception) { Status = string.Format(Loc("settings.local.data.logs.clear.failed"), exception.Message); logger.LogError(exception, "清理日志失败"); }
        return Task.CompletedTask;
    }

    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            AppDataPaths.EnsureCreated();
            var path = Path.Combine(AppDataPaths.RootDirectory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var content = string.Format(
                Loc("settings.local.data.export.content"),
                VersionLabel,
                Environment.OSVersion,
                AppDataPaths.RootDirectory,
                SelectedProxyMode.DisplayName,
                LogRetentionDays);
            await File.WriteAllTextAsync(path, content);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            Status = Loc("settings.local.data.export.done");
            logger.LogInformation("诊断摘要已导出");
        }
        catch (Exception exception) { Status = string.Format(Loc("settings.local.data.export.failed"), exception.Message); logger.LogError(exception, "诊断摘要导出失败"); }
    }

    private static SettingOption FindOption(IReadOnlyList<SettingOption> options, string? value, SettingOption fallback) => options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    private void ApplyAppearancePreview() => applyAppearance?.Invoke(TransparencyEnabled, TransparencyOpacity, BlurAmount, AcrylicTransparencyAlgorithm);

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) _ = LoadAsync();
        else Dispatcher.UIThread.Post(() => _ = LoadAsync());
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        OnPropertyChanged(nameof(ProxyStatus));
    }

    public void Dispose()
    {
        dataStore.ConfigurationChanged -= OnConfigurationChanged;
        LocaleService.CultureChanged -= OnCultureChanged;
        autoSaveCancellation?.Cancel();
        autoSaveCancellation?.Dispose();
        if (ownsUpdateCoordinator) updateCoordinator.Dispose();
    }
}
