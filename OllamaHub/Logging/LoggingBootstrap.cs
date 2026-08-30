using Serilog;
using Serilog.Events;
using Serilog.Formatting;

namespace OllamaHub.Logging;

public static class LoggingBootstrap
{
    private static readonly object SyncRoot = new();
    private static bool configured;
    public static bool IncludeStackTrace { get; private set; }

    public static void SetIncludeStackTrace(bool enabled) => IncludeStackTrace = enabled;

    public static void Configure()
    {
        lock (SyncRoot)
        {
            if (configured) return;
            AppDataPaths.EnsureCreated();
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Sink(new RuntimeLogSink(RuntimeLogBuffer.Default))
                .WriteTo.File(
                    new RuntimeLogTextFormatter(),
                    Path.Combine(AppDataPaths.LogDirectory, "ollamahub-.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 30,
                    shared: true)
                .CreateLogger();
            configured = true;
        }
    }
}

internal sealed class RuntimeLogTextFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "???"
        };
        var source = logEvent.Properties.TryGetValue("SourceContext", out var sourceValue) ? sourceValue.ToString().Trim('"') : "Runtime";
        output.Write(logEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        output.Write('\t');
        output.Write('[');
        output.Write(level);
        output.Write("]\t");
        output.Write(source);
        output.Write('\t');
        output.Write(logEvent.RenderMessage());
        output.WriteLine();
        if (LoggingBootstrap.IncludeStackTrace && logEvent.Exception is not null) output.WriteLine(logEvent.Exception);
    }
}
