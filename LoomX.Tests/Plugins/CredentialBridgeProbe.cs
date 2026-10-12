using System.Collections.Concurrent;
using LoomX.Plugins;

namespace LoomX.Tests.Plugins;

/// <summary>在真实 Bridge 前观测调用边界或注入稳定故障，不重复实现 SQLite。</summary>
internal sealed class CredentialBridgeProbe(IPluginStorageBridge inner) : IPluginStorageBridge
{
    internal ConcurrentQueue<string> Events { get; } = new();
    internal ConcurrentQueue<string> Paths { get; } = new();
    internal ConcurrentQueue<(string Operation, PluginSqlCommand Command)> Commands { get; } = new();
    internal Func<string, PluginSqlCommand?, CancellationToken, ValueTask>? Before { get; set; }
    internal Func<string, PluginSqlCommand?, CancellationToken, ValueTask>? After { get; set; }

    private async ValueTask ObserveAsync(string operation, PluginSqlCommand? command, CancellationToken token)
    {
        Events.Enqueue(operation);
        if (command is not null) Commands.Enqueue((operation, command));
        if (Before is not null) await Before(operation, command, token).ConfigureAwait(false);
    }

    private async ValueTask<T> RunAsync<T>(string operation, PluginSqlCommand? command, CancellationToken token, Func<ValueTask<T>> action)
    {
        await ObserveAsync(operation, command, token).ConfigureAwait(false);
        var result = await action().ConfigureAwait(false);
        if (After is not null) await After(operation, command, token).ConfigureAwait(false);
        return result;
    }

    private async ValueTask RunAsync(string operation, PluginSqlCommand? command, CancellationToken token, Func<ValueTask> action)
    {
        await ObserveAsync(operation, command, token).ConfigureAwait(false);
        await action().ConfigureAwait(false);
        if (After is not null) await After(operation, command, token).ConfigureAwait(false);
    }

    public async ValueTask<IPluginSqliteDatabase> OpenSqliteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        Paths.Enqueue(relativePath);
        var db = await RunAsync("open", null, cancellationToken, () => inner.OpenSqliteAsync(relativePath, cancellationToken)).ConfigureAwait(false);
        return new Database(this, db);
    }

    private sealed class Database(CredentialBridgeProbe owner, IPluginSqliteDatabase inner) : IPluginSqliteDatabase
    {
        public ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => owner.RunAsync("execute", command, cancellationToken, () => inner.ExecuteAsync(command, cancellationToken));
        public ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => owner.RunAsync("query-single", command, cancellationToken, () => inner.QuerySingleAsync(command, cancellationToken));
        public ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => owner.RunAsync("query", command, cancellationToken, () => inner.QueryAsync(command, cancellationToken));
        public async ValueTask<IPluginSqliteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => new Transaction(owner,
            await owner.RunAsync("begin", null, cancellationToken, () => inner.BeginTransactionAsync(cancellationToken)).ConfigureAwait(false));
        public ValueTask DisposeAsync() => owner.RunAsync("database.dispose", null, CancellationToken.None, inner.DisposeAsync);
    }

    private sealed class Transaction(CredentialBridgeProbe owner, IPluginSqliteTransaction inner) : IPluginSqliteTransaction
    {
        public ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => owner.RunAsync("transaction.execute", command, cancellationToken, () => inner.ExecuteAsync(command, cancellationToken));
        public ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => owner.RunAsync("transaction.query-single", command, cancellationToken, () => inner.QuerySingleAsync(command, cancellationToken));
        public ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => owner.RunAsync("transaction.query", command, cancellationToken, () => inner.QueryAsync(command, cancellationToken));
        public ValueTask CommitAsync(CancellationToken cancellationToken = default) => owner.RunAsync("transaction.commit", null, cancellationToken, () => inner.CommitAsync(cancellationToken));
        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => owner.RunAsync("transaction.rollback", null, cancellationToken, () => inner.RollbackAsync(cancellationToken));
        public ValueTask DisposeAsync() => owner.RunAsync("transaction.dispose", null, CancellationToken.None, inner.DisposeAsync);
    }
}
