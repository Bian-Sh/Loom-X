using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaHub;
using OllamaHub.Configuration;
using OllamaHub.Desktop.Services;
using OllamaHub.Logging;

namespace OllamaHub.Desktop.ViewModels;

public sealed record SettingOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class SettingsViewModel : NotifyViewModel
{
    private readonly ConfigSnapshotService configService;
    private readonly ToastService toastService;
    private readonly ILogger<SettingsViewModel> logger;
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private SettingOption selectedLanguage = LanguageOptions[0];
    private SettingOption selectedTheme = ThemeOptions[0];
    private SettingOption selectedProxyMode = ProxyModeOptions[0];
    private SettingOption selectedUpdateChannel = UpdateChannelOptions[0];
    private string proxyHost = "http://127.0.0.1";
    private int proxyPort = 7890;
    private string proxyUsername = "";
    private string proxyPassword = "";
    private bool clearProxyPassword;
    private bool autoCheckUpdates = true;
    private bool diagnosticsEnabled;
    private bool logStackTrace;
    private SettingOption selectedLogRetention = LogRetentionOptions[1];
    private bool isBusy;
    private string status = "正在加载设置…";
    private bool hasProxyPassword;
    private bool suppressAutoSave;
    private CancellationTokenSource? autoSaveCancellation;

    public static IReadOnlyList<SettingOption> LanguageOptions { get; } = [new("zh-CN", "简体中文"), new("en-US", "English"), new("ja-JP", "日本語")];
    public static IReadOnlyList<SettingOption> ThemeOptions { get; } = [new("system", "跟随系统"), new("dark", "深色"), new("light", "浅色")];
    public static IReadOnlyList<SettingOption> ProxyModeOptions { get; } = [new("direct", "直连"), new("system", "系统代理"), new("custom", "自定义代理")];
    public static IReadOnlyList<SettingOption> UpdateChannelOptions { get; } = [new("stable", "稳定版"), new("preview", "预览版")];
    public static IReadOnlyList<SettingOption> LogRetentionOptions { get; } = [new("7", "7 天"), new("30", "30 天"), new("90", "90 天"), new("365", "365 天"), new("3650", "永久保留")];

    public SettingOption SelectedLanguage { get => selectedLanguage; set { if (SetProperty(ref selectedLanguage, value)) QueueAutoSave(); } }
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
    public SettingOption SelectedUpdateChannel { get => selectedUpdateChannel; set { if (SetProperty(ref selectedUpdateChannel, value)) QueueAutoSave(); } }
    public string ProxyHost { get => proxyHost; set { if (SetProperty(ref proxyHost, value)) { OnPropertyChanged(nameof(ProxyStatus)); QueueAutoSave(); } } }
    public int ProxyPort { get => proxyPort; set { if (SetProperty(ref proxyPort, value)) { OnPropertyChanged(nameof(ProxyStatus)); QueueAutoSave(); } } }
    public string ProxyUsername { get => proxyUsername; set { if (SetProperty(ref proxyUsername, value)) QueueAutoSave(); } }
    public string ProxyPassword { get => proxyPassword; set { if (SetProperty(ref proxyPassword, value)) QueueAutoSave(); } }
    public bool ClearProxyPassword { get => clearProxyPassword; set { if (SetProperty(ref clearProxyPassword, value)) QueueAutoSave(); } }
    public bool HasProxyPassword { get => hasProxyPassword; private set => SetProperty(ref hasProxyPassword, value); }
    public bool AutoCheckUpdates { get => autoCheckUpdates; set { if (SetProperty(ref autoCheckUpdates, value)) QueueAutoSave(); } }
    public bool DiagnosticsEnabled { get => diagnosticsEnabled; set { if (SetProperty(ref diagnosticsEnabled, value)) QueueAutoSave(); } }
    public bool LogStackTrace { get => logStackTrace; set { if (SetProperty(ref logStackTrace, value)) { LoggingBootstrap.SetIncludeStackTrace(value); QueueAutoSave(); } } }
    public SettingOption SelectedLogRetention { get => selectedLogRetention; set { if (SetProperty(ref selectedLogRetention, value)) OnPropertyChanged(nameof(LogRetentionDays)); } }
    public int LogRetentionDays => int.Parse(SelectedLogRetention.Value);
    public bool IsBusy { get => isBusy; private set { if (SetProperty(ref isBusy, value)) { OnPropertyChanged(nameof(IsNotBusy)); } } }
    public bool IsNotBusy => !IsBusy;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string VersionLabel => "v0.12.6";
    public string DataDirectory => AppDataPaths.RootDirectory;
    public bool IsCustomProxyVisible => SelectedProxyMode.Value == "custom";
    public string ProxyStatus => SelectedProxyMode.Value switch
    {
        "direct" => "当前为直连模式。",
        "system" => "当前跟随 Windows 系统代理。",
        _ => $"当前使用自定义代理：{ProxyHost}:{ProxyPort}"
    };

    public ICommand LoadCommand { get; }
    public ICommand TestProxyCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenDataDirectoryCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }

    public SettingsViewModel(ConfigSnapshotService configService, ILogger<SettingsViewModel>? logger = null, ToastService? toastService = null)
    {
        this.configService = configService;
        this.logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        this.toastService = toastService ?? new ToastService();
        LoadCommand = new AsyncCommand(LoadAsync);
        TestProxyCommand = new AsyncCommand(TestProxyAsync);
        CheckUpdateCommand = new AsyncCommand(CheckUpdateAsync);
        OpenDataDirectoryCommand = new AsyncCommand(OpenDataDirectoryAsync);
        ClearLogsCommand = new AsyncCommand(ClearLogsAsync);
        ExportDiagnosticsCommand = new AsyncCommand(ExportDiagnosticsAsync);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "正在加载设置…";
        try
        {
            suppressAutoSave = true;
            var settings = await configService.GetSettingsAsync();
            SelectedLanguage = FindOption(LanguageOptions, settings.Language, LanguageOptions[0]);
            SelectedTheme = FindOption(ThemeOptions, settings.Theme, ThemeOptions[0]);
            SelectedProxyMode = FindOption(ProxyModeOptions, settings.ProxyMode, ProxyModeOptions[0]);
            SelectedUpdateChannel = FindOption(UpdateChannelOptions, settings.UpdateChannel, UpdateChannelOptions[0]);
            AutoCheckUpdates = settings.AutoCheckUpdates;
            DiagnosticsEnabled = settings.DiagnosticsEnabled;
            LogStackTrace = settings.LogStackTrace;
            ProxyHost = settings.ProxyHost;
            ProxyPort = settings.ProxyPort;
            ProxyUsername = settings.ProxyUsername ?? "";
            ProxyPassword = "";
            ClearProxyPassword = false;
            HasProxyPassword = settings.HasProxyPassword;
            SelectedLogRetention = FindOption(LogRetentionOptions, settings.LogRetentionDays.ToString(), LogRetentionOptions[1]);
            Status = "设置已加载";
            logger.LogInformation("设置加载完成 {ProxyMode} {UpdateChannel}", settings.ProxyMode, settings.UpdateChannel);
        }
        catch (Exception exception)
        {
            Status = $"加载设置失败：{exception.Message}";
            logger.LogError(exception, "设置加载失败");
        }
        finally { suppressAutoSave = false; IsBusy = false; }
    }

    private async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "正在保存设置…";
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
                SelectedUpdateChannel.Value,
                DiagnosticsEnabled,
                LogRetentionDays,
                LogStackTrace);
            var response = await configService.UpdateSettingsAsync(input, cancellationToken);
            HasProxyPassword = response.HasProxyPassword;
            ProxyPassword = "";
            ClearProxyPassword = false;
            Status = $"设置已保存 · {DateTime.Now:HH:mm:ss}";
            logger.LogInformation("设置保存完成 {ProxyMode} {UpdateChannel} {DiagnosticsEnabled}", response.ProxyMode, response.UpdateChannel, response.DiagnosticsEnabled);
        }
        catch (Exception exception)
        {
            Status = $"保存设置失败：{exception.Message}";
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
        Status = "等待自动保存…";
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
        if (SelectedProxyMode.Value == "direct") { Status = "直连模式配置有效。"; toastService.Show("直连模式配置有效", ToastLevel.Success); logger.LogInformation("代理测试完成 {ProxyMode}", SelectedProxyMode.Value); return; }
        if (SelectedProxyMode.Value == "system") { Status = "系统代理模式已选择，将跟随 Windows 设置。"; toastService.Show("系统代理模式配置有效", ToastLevel.Success); logger.LogInformation("代理测试完成 {ProxyMode}", SelectedProxyMode.Value); return; }
        if (!Uri.TryCreate(ProxyHost?.Trim(), UriKind.Absolute, out var proxyUri) || proxyUri.Scheme is not ("http" or "https") || ProxyPort is < 1 or > 65535)
        {
            Status = "代理测试失败：请填写有效的 HTTP/HTTPS 地址和端口。";
            toastService.Show("代理测试配置无效", ToastLevel.Warning);
            logger.LogWarning("代理测试配置无效 {ProxyMode}", SelectedProxyMode.Value);
            return;
        }

        IsBusy = true;
        Status = "正在测试代理连接…";
        try
        {
            using var handler = new HttpClientHandler { Proxy = new WebProxy($"{proxyUri.Scheme}://{proxyUri.Host}:{ProxyPort}"), UseProxy = true };
            if (!string.IsNullOrWhiteSpace(ProxyUsername)) handler.Proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("https://www.example.com", HttpCompletionOption.ResponseHeadersRead);
            Status = response.IsSuccessStatusCode ? $"代理连接正常 · {(int)response.StatusCode}" : $"代理已响应 · {(int)response.StatusCode}";
            toastService.Show(response.IsSuccessStatusCode ? "代理连接测试成功" : "代理已响应，请检查配置", response.IsSuccessStatusCode ? ToastLevel.Success : ToastLevel.Warning);
            logger.LogInformation("代理测试完成 {ProxyMode} {StatusCode}", SelectedProxyMode.Value, (int)response.StatusCode);
        }
        catch (Exception exception) { Status = $"代理测试失败：{exception.Message}"; toastService.Show("代理连接测试失败", ToastLevel.Error); logger.LogWarning(exception, "代理测试失败 {ProxyMode}", SelectedProxyMode.Value); }
        finally { IsBusy = false; }
    }

    private Task CheckUpdateAsync()
    {
        Status = $"当前版本 {VersionLabel}；远程更新服务尚未接入。";
        logger.LogInformation("本地更新检查完成 {Version}", VersionLabel);
        return Task.CompletedTask;
    }

    private Task OpenDataDirectoryAsync()
    {
        try
        {
            AppDataPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo { FileName = AppDataPaths.RootDirectory, UseShellExecute = true });
            Status = "已打开本地数据目录。";
            logger.LogInformation("本地数据目录已打开");
        }
        catch (Exception exception) { Status = $"打开数据目录失败：{exception.Message}"; logger.LogError(exception, "打开本地数据目录失败"); }
        return Task.CompletedTask;
    }

    private Task ClearLogsAsync()
    {
        try
        {
            AppDataPaths.EnsureCreated();
            var files = Directory.EnumerateFiles(AppDataPaths.LogDirectory, "*.log").ToArray();
            foreach (var file in files) File.Delete(file);
            Status = files.Length == 0 ? "没有可清理的日志。" : $"已清理 {files.Length} 个日志文件。";
            logger.LogInformation("日志清理完成 {FileCount}", files.Length);
        }
        catch (Exception exception) { Status = $"清理日志失败：{exception.Message}"; logger.LogError(exception, "日志清理失败"); }
        return Task.CompletedTask;
    }

    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            AppDataPaths.EnsureCreated();
            var path = Path.Combine(AppDataPaths.RootDirectory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var content = $"OllamaHub 诊断摘要\n版本：{VersionLabel}\n系统：{Environment.OSVersion}\n数据目录：{AppDataPaths.RootDirectory}\n代理模式：{SelectedProxyMode.DisplayName}\n日志保留：{LogRetentionDays} 天\n";
            await File.WriteAllTextAsync(path, content);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            Status = "诊断摘要已导出。";
            logger.LogInformation("诊断摘要已导出");
        }
        catch (Exception exception) { Status = $"导出诊断失败：{exception.Message}"; logger.LogError(exception, "诊断摘要导出失败"); }
    }

    private static SettingOption FindOption(IReadOnlyList<SettingOption> options, string? value, SettingOption fallback) => options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
}
