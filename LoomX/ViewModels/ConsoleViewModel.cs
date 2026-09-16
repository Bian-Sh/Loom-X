using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX;
using LoomX.Localization;
using LoomX.Logging;
using LoomX.Services;

namespace LoomX.ViewModels;

public sealed class ConsoleViewModel : NotifyViewModel, IDisposable
{
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2} )?(?<clock>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\s+(?:\+?[^\s]+\s+)?\[(?<level>[^\]]+)\]\s+(?<module>[^:]+):?\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TabLinePattern = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<clock>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\s+[^\t]*\t\[(?<level>[^\]]+)\]\t(?<module>[^\t]*)\t(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretPattern = new("(?i)(authorization\\s*:\\s*(?:bearer\\s+)?|api[-_ ]?key\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.Compiled);
    /// <summary>
    /// 日志合并窗口：窗口期内到达的日志由一次 UI 回调批量消费。
    /// 每条日志单独 Dispatcher.Post 会在日志洪水时淹没 UI 队列并让界面失去响应。
    /// </summary>
    private static readonly TimeSpan LogFlushInterval = TimeSpan.FromMilliseconds(200);
    private readonly ConcurrentQueue<RuntimeLogEntry> pendingEntries = new();
    private readonly List<ConsoleLogEntry> allLogs = [];
    private readonly RuntimeLogBuffer buffer;
    private readonly ToastService toastService;
    private readonly EventHandler<RuntimeLogEntry> entryHandler;
    private readonly IStringLocalizer<ConsoleViewModel> _loc;
    private readonly ILogger<ConsoleViewModel>? logger;
    private volatile bool disposed;
    private int flushScheduled;
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
    public string CountLabel => LocFormat("console.count.format", VisibleLogs.Count);
    public bool HasLogs => VisibleLogs.Count > 0;
    public ICommand ClearCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public ConsoleViewModel(RuntimeLogBuffer? buffer = null, ToastService? toastService = null, IStringLocalizer<ConsoleViewModel>? localizer = null, ILogger<ConsoleViewModel>? logger = null)
    {
        this.buffer = buffer ?? RuntimeLogBuffer.Default;
        this.toastService = toastService ?? new ToastService();
        this.logger = logger;
        _loc = localizer ?? LocalizerFactory.Create<ConsoleViewModel>();
        entryHandler = (_, entry) => EnqueueRuntimeEntry(entry);
        this.buffer.EntryAdded += entryHandler;
        ClearCommand = new DelegateCommand(Clear);
        ClearSearchCommand = new DelegateCommand(() => SearchText = "");
        foreach (var entry in this.buffer.Snapshot())
        {
            var log = FromRuntime(entry);
            allLogs.Add(log);
            UpdateCounts(log, 1);
        }
        LocaleService.CultureChanged += OnCultureChanged;
        ApplyFilter();
    }

    private string Loc(string key) => _loc[key]?.Value ?? key;
    private string LocFormat(string key, params object[] args)
    {
        var value = Loc(key);
        return args.Length == 0 ? value : string.Format(System.Globalization.CultureInfo.CurrentCulture, value, args);
    }

    private void OnCultureChanged(object? sender, System.Globalization.CultureInfo culture)
    {
        OnPropertyChanged(nameof(CountLabel));
    }

    public void NotifyCopied() => toastService.Show(Loc("console.copied"), ToastLevel.Success);

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

    /// <summary>
    /// 日志到达时只做无锁入队，不直接向 UI 线程投递。
    /// 合并窗口结束由一次 UI 回调批量消费，避免高频日志把消息泵挤爆。
    /// </summary>
    private void EnqueueRuntimeEntry(RuntimeLogEntry entry)
    {
        if (disposed) return;
        pendingEntries.Enqueue(entry);
        if (Interlocked.CompareExchange(ref flushScheduled, 1, 0) != 0) return;
        _ = FlushAfterDelayAsync();
    }

    private async Task FlushAfterDelayAsync()
    {
        try
        {
            await Task.Delay(LogFlushInterval).ConfigureAwait(false);
            if (disposed) return;
            await Dispatcher.UIThread.InvokeAsync(FlushPendingEntries);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Interlocked.Exchange(ref flushScheduled, 0);
            logger?.LogWarning(exception, "控制台日志批量刷新失败，丢弃本轮待处理条目");
        }
    }

    /// <summary>
    /// 在 UI 线程一次性消化窗口期内累积的日志。
    /// 先复位标志再消费，确保消费过程中新入队的条目能排到下一轮。
    /// </summary>
    private void FlushPendingEntries()
    {
        Interlocked.Exchange(ref flushScheduled, 0);
        var added = 0;
        while (pendingEntries.TryDequeue(out var entry))
        {
            AddEntry(entry);
            added++;
        }
        if (added > 0) logger?.LogDebug("控制台日志批量刷新完成 {EntryCount}", added);
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

    public void Dispose()
    {
        disposed = true;
        buffer.EntryAdded -= entryHandler;
        LocaleService.CultureChanged -= OnCultureChanged;
    }
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
