using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LoomX.Logging;

public sealed class FileLoggerProvider(string filePath, LogLevel minLogLevel) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, filePath, minLogLevel));

    public void Dispose()
    {
    }
}

internal sealed class FileLogger(string categoryName, string filePath, LogLevel minLogLevel) : ILogger
{
    private static readonly Lock WriteLock = new();
    // 同一目录下所有 logger 共享"目录已存在"结论，避免每条日志都触发一次系统调用。
    private static readonly ConcurrentDictionary<string, byte> ReadyDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<string?> directory = new(() => EnsureDirectory(filePath), LazyThreadSafetyMode.ExecutionAndPublication);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minLogLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}\t[{logLevel}]\t{categoryName}\t{message}";
        if (exception is not null)
        {
            line = $"{line}{Environment.NewLine}{exception}";
        }

        _ = directory.Value;

        lock (WriteLock)
        {
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }

    private static string? EnsureDirectory(string path)
    {
        var target = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(target)) return null;
        if (ReadyDirectories.ContainsKey(target)) return target;
        Directory.CreateDirectory(target);
        ReadyDirectories[target] = 0;
        return target;
    }
}
