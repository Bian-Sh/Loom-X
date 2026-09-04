using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using OllamaHub;
using OllamaHub.Logging;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class ConsoleViewModel : NotifyViewModel, IDisposable
{
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2} )?(?<clock>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\s+(?:\+?[^\s]+\s+)?\[(?<level>[^\]]+)\]\s+(?<module>[^:]+):?\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TabLinePattern = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<clock>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\s+[^\t]*\t\[(?<level>[^\]]+)\]\t(?<module>[^\t]*)\t(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretPattern = new("(?i)(authorization\\s*:\\s*(?:bearer\\s+)?|api[-_ ]?key\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.Compiled);
    private readonly List<ConsoleLogEntry> allLogs = [];
    private readonly RuntimeLogBuffer buffer;
    private readonly ToastService toastService;
    private readonly EventHandler<RuntimeLogEntry> entryHandler;
    private string searchText = "";
    private bool showInfo = true;
    private bool showWarning = true;
    private bool showError = true;
    private int infoCount;
    private int warningCount;
    private int errorCount;
    private double scrollOffsetY;
    private bool followTail = true;

    public ObservableCollection<ConsoleLogEntry> VisibleLogs { get; } = [];
    public string SearchText { get => searchText; set { if (SetProperty(ref searchText, value ?? "")) { OnPropertyChanged(nameof(HasSearchText)); ApplyFilter(); } } }
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowInfo { get => showInfo; set { if (SetProperty(ref showInfo, value)) ApplyFilter(); } }
    public bool ShowWarning { get => showWarning; set { if (SetProperty(ref showWarning, value)) ApplyFilter(); } }
    public bool ShowError { get => showError; set { if (SetProperty(ref showError, value)) ApplyFilter(); } }
    public double ScrollOffsetY => scrollOffsetY;
    public bool FollowTail => followTail;
    public int InfoCount => infoCount;
    public int WarningCount => warningCount;
    public int ErrorCount => errorCount;
    public string CountLabel => $"共 {VisibleLogs.Count} 条";
    public bool HasLogs => VisibleLogs.Count > 0;
    public ICommand ClearCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public ConsoleViewModel(RuntimeLogBuffer? buffer = null, ToastService? toastService = null)
    {
        this.buffer = buffer ?? RuntimeLogBuffer.Default;
        this.toastService = toastService ?? new ToastService();
        entryHandler = (_, entry) => Dispatcher.UIThread.Post(() => AddEntry(entry));
        this.buffer.EntryAdded += entryHandler;
        ClearCommand = new DelegateCommand(Clear);
        ClearSearchCommand = new DelegateCommand(() => SearchText = "");
        foreach (var entry in this.buffer.Snapshot())
        {
            var log = FromRuntime(entry);
            allLogs.Add(log);
            UpdateCounts(log, 1);
        }
        ApplyFilter();
    }

    public void NotifyCopied() => toastService.Show("日志已复制", ToastLevel.Success);

    public void UpdateScrollState(double offsetY, bool shouldFollowTail)
    {
        scrollOffsetY = Math.Max(0, offsetY);
        followTail = shouldFollowTail;
    }

    public static bool TryParse(string line, out ConsoleLogEntry entry)
    {
        var tabMatch = TabLinePattern.Match(line);
        if (tabMatch.Success)
        {
            entry = new ConsoleLogEntry(tabMatch.Groups["clock"].Value, NormalizeLevel(tabMatch.Groups["level"].Value), tabMatch.Groups["module"].Value.Trim(), Sanitize(tabMatch.Groups["message"].Value));
            return true;
        }
        var match = LinePattern.Match(line);
        if (!match.Success)
        {
            entry = new ConsoleLogEntry("--:--:--", "Info", "Runtime", Sanitize(line));
            return false;
        }

        var module = match.Groups["module"].Value.Trim();
        entry = new ConsoleLogEntry(match.Groups["clock"].Value, NormalizeLevel(match.Groups["level"].Value), string.IsNullOrWhiteSpace(module) ? "Runtime" : module, Sanitize(match.Groups["message"].Value));
        return true;
    }

    internal void AddEntry(RuntimeLogEntry runtimeEntry)
    {
        var entry = FromRuntime(runtimeEntry);
        allLogs.Add(entry);
        UpdateCounts(entry, 1);
        while (allLogs.Count > 5000)
        {
            var removed = allLogs[0];
            allLogs.RemoveAt(0);
            VisibleLogs.Remove(removed);
            UpdateCounts(removed, -1);
        }
        if (MatchesFilter(entry)) VisibleLogs.Add(entry);
        NotifyCollectionSummaryChanged();
    }

    private void Clear()
    {
        allLogs.Clear();
        infoCount = warningCount = errorCount = 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = allLogs.Where(item => MatchesFilter(item, query));
        VisibleLogs.Clear();
        foreach (var item in filtered) VisibleLogs.Add(item);
        NotifyCollectionSummaryChanged();
    }

    private bool MatchesFilter(ConsoleLogEntry item)
    {
        return MatchesFilter(item, SearchText.Trim());
    }

    private bool MatchesFilter(ConsoleLogEntry item, string query)
    {
        return ((ShowInfo && IsInfo(item)) || (ShowWarning && item.LevelLabel == "Warning") || (ShowError && item.LevelLabel == "Error")) &&
            (query.Length == 0 || $"{item.Time}\t{item.LevelLabel}\t{item.Module}\t{item.Message}".Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateCounts(ConsoleLogEntry item, int delta)
    {
        if (IsInfo(item)) infoCount += delta;
        else if (item.LevelLabel == "Warning") warningCount += delta;
        else if (item.LevelLabel == "Error") errorCount += delta;
    }

    private void NotifyCollectionSummaryChanged()
    {
        OnPropertyChanged(nameof(InfoCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(HasLogs));
    }

    private static ConsoleLogEntry FromRuntime(RuntimeLogEntry entry)
    {
        var message = entry.Message;
        if (entry.Exception is not null)
        {
            message = LoggingBootstrap.IncludeStackTrace
                ? $"{message} · {entry.Exception}"
                : $"{message} · {entry.Exception.GetType().Name}: {entry.Exception.Message}";
        }
        return new ConsoleLogEntry(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"), NormalizeLevel(entry.Level), entry.Category, Sanitize(message));
    }

    private static bool IsInfo(ConsoleLogEntry item) => item.LevelLabel is "Info" or "Debug" or "Trace" or "OK";
    private static string NormalizeLevel(LogLevel level) => level switch
    {
        LogLevel.Warning => "Warning",
        LogLevel.Error or LogLevel.Critical => "Error",
        LogLevel.Debug => "Debug",
        LogLevel.Trace => "Trace",
        _ => "Info"
    };
    private static string NormalizeLevel(string value) => value.ToLowerInvariant() switch
    {
        "warning" or "warn" or "wrn" => "Warning",
        "error" or "critical" or "err" or "fail" => "Error",
        "debug" => "Debug",
        "trace" or "verbose" => "Trace",
        "none" or "information" or "inf" => "Info",
        _ => value.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "OK" : value
    };
    private static string Sanitize(string value) => SecretPattern.Replace(value, "$1[redacted]");

    public void Dispose() => buffer.EntryAdded -= entryHandler;
}

public sealed class ConsoleLogEntry
{
    public string Time { get; }
    public string LevelLabel { get; }
    public string Module { get; }
    public string Message { get; }
    public string TabSeparated => string.Join('\t', Time, LevelLabel, Module, Message);
    public string LevelColor => LevelLabel switch
    {
        "Error" => "#B83E48",
        "Warning" => "#A26B16",
        "OK" => "#35C98A",
        _ => "#176B87"
    };

    public ConsoleLogEntry(string time, string level, string module, string message) => (Time, LevelLabel, Module, Message) = (time, level, module, message);
}
