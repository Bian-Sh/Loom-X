using LoomX.Plugins;
using Microsoft.Extensions.Logging;

namespace LoomX.Plugin.Storage.Sqlite;

/// <summary>宿主持有的插件 SQLite 服务。数据根包含各插件目录；该边界不是操作系统沙箱。</summary>
public sealed class PluginSqliteStorageBridge : IPluginStorageBridge, IAsyncDisposable
{
    private readonly PluginStoragePaths paths;
    private readonly string pluginId;
    private readonly ILogger<PluginSqliteStorageBridge>? logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<SqliteDatabase> handles = [];
    private bool disposed;
    internal const int MaximumHandles = 64;

    public PluginSqliteStorageBridge(string pluginId, string dataRootDirectory, ILogger<PluginSqliteStorageBridge>? logger = null)
    {
        paths = new(pluginId, dataRootDirectory);
        this.pluginId = pluginId;
        this.logger = logger;
    }

    public async ValueTask<IPluginSqliteDatabase> OpenSqliteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var entered = false;
        LogStart("打开");
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false); entered = true;
            if (disposed) throw PluginSqliteErrors.Create(PluginSqlError.Disposed);
            cancellationToken.ThrowIfCancellationRequested();
            lock (handles) if (handles.Count >= MaximumHandles) throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
            var path = paths.Resolve(relativePath);
            var database = await Task.Run(() =>
            {
                paths.CreateAndValidate(path);
                cancellationToken.ThrowIfCancellationRequested();
                var db = new SqliteDatabase(this, paths, path);
                try
                {
                    paths.Checkpoint?.Invoke("连接已打开", path);
                    paths.Validate(path); cancellationToken.ThrowIfCancellationRequested(); return db;
                }
                catch { db.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
            }, CancellationToken.None).ConfigureAwait(false);
            lock (handles) handles.Add(database);
            LogComplete("打开");
            return database;
        }
        catch (Exception ex) { var safe = PluginSqliteErrors.Map(ex); LogFailure("打开", safe); throw safe; }
        finally { if (entered) gate.Release(); }
    }

    internal void Remove(SqliteDatabase database) { lock (handles) handles.Remove(database); }
    internal void LogStart(string operation) => logger?.LogInformation("插件数据库操作开始 {PluginId} {Operation}", pluginId, operation);
    internal void LogComplete(string operation) => logger?.LogInformation("插件数据库操作完成 {PluginId} {Operation}", pluginId, operation);
    internal void LogFailure(string operation, PluginSqlException error)
    {
        // 不传提供程序异常：其消息可能包含 SQL、路径或敏感参数。
        if (error.Error is PluginSqlError.Corrupt or PluginSqlError.Unknown)
            logger?.LogError("插件数据库操作失败 {PluginId} {Operation} {Error} {SqliteCode}", pluginId, operation, error.Error, error.SqliteErrorCode);
        else logger?.LogWarning("插件数据库操作未完成 {PluginId} {Operation} {Error} {SqliteCode}", pluginId, operation, error.Error, error.SqliteErrorCode);
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            SqliteDatabase[] snapshot; lock (handles) snapshot = handles.ToArray();
            PluginSqlException? failure = null;
            foreach (var database in snapshot)
            {
                try { await database.DisposeAsync().ConfigureAwait(false); }
                catch (PluginSqlException ex) { failure ??= ex; }
            }
            if (failure is not null) throw failure;
            LogComplete("释放存储");
        }
        finally { gate.Release(); }
    }
}
