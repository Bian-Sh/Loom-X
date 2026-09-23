using Microsoft.Extensions.Logging;

namespace LoomX.Tests.Logging;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public List<RecordingLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var pair in values)
            {
                if (!string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                    properties[pair.Key] = pair.Value;
            }
        }

        Entries.Add(new RecordingLogEntry(logLevel, message, exception, properties));
        Messages.Add(exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }
}

internal sealed record RecordingLogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
