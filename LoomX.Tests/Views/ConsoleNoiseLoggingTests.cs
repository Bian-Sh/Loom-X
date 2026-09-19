using Microsoft.Extensions.Logging;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class ConsoleNoiseLoggingTests
{
    [Fact]
    public void 透明外观诊断只使用Debug级别()
    {
        AvaloniaTestBootstrap.Ensure();
        var logger = new LevelRecordingLogger<MainWindow>();
        var window = new MainWindow(new ToastService(), logger);

        window.ApplyAppearance(true, 90, 44, "acrylic");

        var appearanceEntries = logger.Entries
            .Where(entry => entry.Message.StartsWith("透明外观应用", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, appearanceEntries.Length);
        Assert.All(appearanceEntries, entry => Assert.Equal(LogLevel.Debug, entry.Level));
    }

    [Fact]
    public void Shell启动回退诊断不再使用Warning级别()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "App.axaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("LogDebug(exception, \"桌面应用自启动子进程失败", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogWarning(exception, \"桌面应用自启动子进程失败", source, StringComparison.Ordinal);
    }

    private sealed class LevelRecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
