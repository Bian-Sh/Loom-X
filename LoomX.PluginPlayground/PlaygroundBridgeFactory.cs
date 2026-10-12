using System.Collections.Concurrent;
using LoomX.Plugin.Storage.Sqlite;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;

/// <summary>测试组合根：SQLite 使用真实宿主实现，日志/通知仅捕获夹具定义的安全摘要。</summary>
internal sealed class PlaygroundBridgeFactory(string dataRoot, ILoggerFactory loggerFactory)
{
    public List<(string Id, PluginBridgeScope Scope)> Scopes { get; } = [];
    public ConcurrentQueue<(string Id, PluginLogEntry Entry)> Logs { get; } = new();
    public ConcurrentQueue<(string Id, PluginNotification Notification)> Notifications { get; } = new();
    public PluginBridgeScope Create(PluginManifest manifest, string directory)
    {
        var expected = Path.GetFullPath(Path.Combine(dataRoot, manifest.Id));
        if (!string.Equals(Path.GetFullPath(directory), expected, StringComparison.OrdinalIgnoreCase))
            throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "测试 Bridge 数据目录不符合隔离约定。");
        var feedback = new FeedbackAdapter(this, manifest.Id, loggerFactory.CreateLogger<FeedbackAdapter>());
        var storage = manifest.HostCapabilities.Contains("storage.sqlite/v1", StringComparer.Ordinal)
            ? new PluginSqliteStorageBridge(manifest.Id, dataRoot, loggerFactory.CreateLogger<PluginSqliteStorageBridge>()) : null;
        var scope = new PluginBridgeScope(manifest.Id, manifest.HostCapabilities, storage, feedback, feedback);
        Scopes.Add((manifest.Id, scope));
        return scope;
    }

    // ponytail：测试夹具事件白名单，不复刻或为生产 feedback 过滤器提供背书。
    private sealed class FeedbackAdapter(PlaygroundBridgeFactory owner, string pluginId, ILogger<FeedbackAdapter> logger)
        : IPluginLogBridge, IPluginNotificationBridge
    {
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = entry.Fields ?? [];
            var initialize = entry.MessageTemplate == "源码插件初始化完成 {Generation}" && fields.Count == 1
                && fields[0].Name == "Generation" && fields[0].Value.Kind == PluginLogValueKind.Text
                && fields[0].Value.Text is "g1" or "g2";
            var completed = entry.MessageTemplate == "源码插件检查完成 {StatusCode}" && fields.Count == 1
                && fields[0].Name == "StatusCode" && fields[0].Value.Kind == PluginLogValueKind.Integer
                && fields[0].Value.Integer == 200;
            if (entry.Level != PluginLogLevel.Information || !(initialize || completed))
                throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "测试日志只接受已声明的安全事件。");
            // 重建已激活 union 值，丢弃未激活的 Text 等载荷；同时不保留调用方可变数组。
            var value = initialize ? new PluginLogValue(PluginLogValueKind.Text, Text: fields[0].Value.Text)
                : new PluginLogValue(PluginLogValueKind.Integer, Integer: 200);
            owner.Logs.Enqueue((pluginId, entry with
            { Fields = Array.AsReadOnly(new[] { new PluginLogField(fields[0].Name, value) }) }));
            logger.LogInformation("Playground 插件事件完成 {PluginId} {Event}", pluginId, initialize ? "初始化" : "检查");
            return ValueTask.CompletedTask;
        }
        public ValueTask ShowAsync(PluginNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (notification.Level != PluginNotificationLevel.Success
                || notification.Message is not ("源码插件初始化完成" or "源码插件检查完成"))
                throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "测试通知只接受已声明的安全摘要。");
            owner.Notifications.Enqueue((pluginId, notification));
            logger.LogInformation("Playground 插件通知完成 {PluginId} {Level}", pluginId, notification.Level);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>宿主 ILogger 捕获器，不把业务日志或任意异常写到 CLI。</summary>
internal sealed class CaptureLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Events { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void Dispose() { }
    private sealed class CaptureLogger(CaptureLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Events.Enqueue((category, level, formatter(state, exception) + exception?.ToString()));
    }
}
