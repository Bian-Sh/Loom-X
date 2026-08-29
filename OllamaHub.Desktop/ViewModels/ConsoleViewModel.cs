using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Threading;
using OllamaHub;

namespace OllamaHub.Desktop.ViewModels;

public sealed class ConsoleViewModel : NotifyViewModel, IDisposable
{
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2} )?(?<clock>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\s+(?:\+?[^\s]+\s+)?\[(?<level>[^\]]+)\]\s+(?<module>[^:]+):\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SerilogPattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2})\s+(?<clock>\d{2}:\d{2}:\d{2})\s+\[(?<level>[^\]]+)\]\s+(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretPattern = new("(?i)(authorization\\s*:\\s*(?:bearer\\s+)?|api[-_ ]?key\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.Compiled);
    private readonly List<ConsoleLogEntry> allLogs = [];
    private readonly FileSystemWatcher? logWatcher;
    private string selectedLevel = "全部等级";
    private string selectedModule = "全部模块";
    private string searchText = "";
    private string logFileLabel = "日志文件：未找到 · API key 已自动隐藏";

    public ObservableCollection<ConsoleLogEntry> VisibleLogs { get; } = [];
    public IReadOnlyList<string> LevelOptions { get; } = ["全部等级", "Info", "Warning", "Error"];
    public ObservableCollection<string> ModuleOptions { get; } = ["全部模块"];
    public string SelectedLevel { get => selectedLevel; set { if (SetProperty(ref selectedLevel, string.IsNullOrWhiteSpace(value) ? "全部等级" : value)) ApplyFilter(); } }
    public string SelectedModule { get => selectedModule; set { if (SetProperty(ref selectedModule, string.IsNullOrWhiteSpace(value) ? "全部模块" : value)) ApplyFilter(); } }
    public string SearchText { get => searchText; set { if (SetProperty(ref searchText, value ?? "")) ApplyFilter(); } }
    public string CountLabel => $"显示 {VisibleLogs.Count} 条脱敏日志";
    public bool HasLogs => VisibleLogs.Count > 0;
    public string LogFileLabel { get => logFileLabel; private set => SetProperty(ref logFileLabel, value); }
    public ICommand ClearCommand { get; }

    public ConsoleViewModel()
    {
        if (Directory.Exists(AppDataPaths.LogDirectory))
        {
            logWatcher = new FileSystemWatcher(AppDataPaths.LogDirectory, "*.log") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            logWatcher.Changed += LogFileChanged;
            logWatcher.Created += LogFileChanged;
            logWatcher.Renamed += LogFileChanged;
            logWatcher.EnableRaisingEvents = true;
        }
        ClearCommand = new DelegateCommand(Clear);
        LoadLogs();
    }

    internal static bool TryParse(string line, out ConsoleLogEntry entry)
    {
        var match = LinePattern.Match(line);
        if (!match.Success)
        {
            var serilog = SerilogPattern.Match(line);
            if (serilog.Success)
            {
                entry = new ConsoleLogEntry(serilog.Groups["clock"].Value, NormalizeLevel(serilog.Groups["level"].Value), "Runtime", Sanitize(serilog.Groups["message"].Value));
                return true;
            }
        }
        if (!match.Success)
        {
            entry = new ConsoleLogEntry("--:--:--", "Info", "Runtime", Sanitize(line));
            return false;
        }

        var level = NormalizeLevel(match.Groups["level"].Value);
        entry = new ConsoleLogEntry(match.Groups["clock"].Value, level, match.Groups["module"].Value.Trim(), Sanitize(match.Groups["message"].Value));
        return true;
    }

    private void LoadLogs()
    {
        var previousModule = SelectedModule;
        allLogs.Clear();
        try
        {
            var files = Directory.Exists(AppDataPaths.LogDirectory)
                ? Directory.EnumerateFiles(AppDataPaths.LogDirectory, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).ToArray()
                : [];
            var path = files.FirstOrDefault();
            if (path is null)
            {
                LogFileLabel = "日志文件：未找到 · API key 已自动隐藏";
            }
            else
            {
                foreach (var line in ReadSharedLines(path).TakeLast(5000))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    TryParse(line, out var entry);
                    allLogs.Add(entry);
                }
                LogFileLabel = $"日志文件：{Path.GetFileName(path)} · API key 已自动隐藏";
            }
        }
        catch (IOException)
        {
            LogFileLabel = "日志文件：读取中 · API key 已自动隐藏";
        }
        RebuildModuleOptions(previousModule);
        ApplyFilter();
    }

    private void RebuildModuleOptions(string previousModule)
    {
        var modules = allLogs.Select(item => item.Module).Distinct().ToArray();
        var restoredModule = modules.Contains(previousModule) ? previousModule : "全部模块";
        if (restoredModule != previousModule) SelectedModule = restoredModule;
        for (var index = ModuleOptions.Count - 1; index > 0; index--)
            if (!modules.Contains(ModuleOptions[index])) ModuleOptions.RemoveAt(index);
        foreach (var module in modules)
            if (!ModuleOptions.Contains(module)) ModuleOptions.Add(module);
        selectedModule = restoredModule;
        OnPropertyChanged(nameof(SelectedModule));
    }

    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private void LogFileChanged(object sender, FileSystemEventArgs args) => Dispatcher.UIThread.Post(LoadLogs);

    private void Clear()
    {
        allLogs.Clear();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = allLogs.Where(item =>
            (SelectedLevel == "全部等级" || item.LevelLabel == SelectedLevel || (SelectedLevel == "Info" && item.LevelLabel == "OK")) &&
            (SelectedModule == "全部模块" || item.Module == SelectedModule) &&
            (query.Length == 0 || $"{item.Time} {item.LevelLabel} {item.Module} {item.Message}".Contains(query, StringComparison.OrdinalIgnoreCase)));
        VisibleLogs.Clear();
        foreach (var item in filtered) VisibleLogs.Add(item);
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(HasLogs));
    }

    private static string NormalizeLevel(string value) => value.ToLowerInvariant() switch
    {
        "warning" or "warn" or "wrn" => "Warning",
        "error" or "critical" or "err" => "Error",
        "debug" or "trace" => "Debug",
        "none" => "Info",
        _ => value.Equals("information", StringComparison.OrdinalIgnoreCase) || value.Equals("inf", StringComparison.OrdinalIgnoreCase) ? "Info" : value.ToUpperInvariant()
    };

    private static string Sanitize(string value) => SecretPattern.Replace(value, "$1[redacted]");

    public void Dispose()
    {
        if (logWatcher is null) return;
        logWatcher.EnableRaisingEvents = false;
        logWatcher.Changed -= LogFileChanged;
        logWatcher.Created -= LogFileChanged;
        logWatcher.Renamed -= LogFileChanged;
        logWatcher.Dispose();
    }
}

public sealed class ConsoleLogEntry
{
    public string Time { get; }
    public string LevelLabel { get; }
    public string Module { get; }
    public string Message { get; }
    public string LevelColor => LevelLabel switch
    {
        "Error" => "#B83E48",
        "Warning" => "#A26B16",
        "OK" => "#35C98A",
        _ => "#176B87"
    };

    public ConsoleLogEntry(string time, string level, string module, string message)
    {
        Time = time;
        LevelLabel = level;
        Module = module;
        Message = message;
    }
}
