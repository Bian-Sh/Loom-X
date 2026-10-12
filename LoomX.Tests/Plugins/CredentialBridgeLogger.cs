using System.Collections.Concurrent;
using LoomX.Plugin.Storage.Sqlite;
using Microsoft.Extensions.Logging;

namespace LoomX.Tests.Plugins;

/// <summary>保留格式化日志和异常消息供敏感数据泄漏断言，不向终端输出凭据。</summary>
internal sealed class CredentialBridgeLogger : ILogger<PluginSqliteStorageBridge>
{
    internal ConcurrentQueue<string> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue(logLevel + ":" + formatter(state, exception) + ":" + exception?.Message);
}
