using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace OllamaHub.Logging;

public sealed record RuntimeLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception = null);

public sealed class RuntimeLogBuffer
{
    private const int Capacity = 5000;
    private readonly ConcurrentQueue<RuntimeLogEntry> entries = new();

    public static RuntimeLogBuffer Default { get; } = new();
    public event EventHandler<RuntimeLogEntry>? EntryAdded;

    public void Append(LogLevel level, string category, string message, Exception? exception = null) =>
        Append(new RuntimeLogEntry(DateTimeOffset.Now, level, category, message, exception));

    public void Append(RuntimeLogEntry entry)
    {
        entries.Enqueue(entry);
        while (entries.Count > Capacity) entries.TryDequeue(out _);
        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<RuntimeLogEntry> Snapshot() => entries.ToArray();
    public void Clear() { while (entries.TryDequeue(out _)) { } }
}

public sealed class RuntimeLogSink(RuntimeLogBuffer buffer) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose or LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.None
        };
        if (level == LogLevel.None) return;
        var category = logEvent.Properties.TryGetValue("SourceContext", out var source)
            ? source.ToString().Trim('"')
            : "Runtime";
        var message = logEvent.RenderMessage();
        buffer.Append(new RuntimeLogEntry(logEvent.Timestamp, level, category, message, logEvent.Exception));
    }
}
