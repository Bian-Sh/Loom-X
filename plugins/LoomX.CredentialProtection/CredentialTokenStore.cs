using System.Security.Cryptography;
using System.Text;
using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>长期凭据映射仅经公共存储契约访问原 Vault；原值仍使用当前用户 DPAPI 加密。</summary>
public sealed class CredentialTokenStore : IAsyncDisposable
{
    private const string DatabaseFileName = "credential-tokens.db";
    private const string ProtectedPrefix = "dpapi:v1:";
    private readonly IPluginStorageBridge storage;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object disposalGate = new();
    private Task? disposalTask;
    private IPluginSqliteDatabase? database;
    private int disposed;

    public CredentialTokenStore(IPluginStorageBridge storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        this.storage = storage;
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await GetDatabaseAsync(linked.Token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async ValueTask<string> GetOrCreateTokenAsync(
        string original, Func<string> createToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(createToken);
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync(token).ConfigureAwait(false);
            var originalHash = ComputeHash(original);
            var existing = await FindByHashAsync(db, originalHash, token).ConfigureAwait(false);
            if (existing is not null) return existing;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var candidate = createToken();
                // 唯一约束冲突必须先回滚并释放失败事务，再精确查回既有映射。
                var transaction = await db.BeginTransactionAsync(token).ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    try
                    {
                        await transaction.ExecuteAsync(new PluginSqlCommand(
                            """
                            INSERT INTO CredentialTokens(Token, OriginalHash, ProtectedOriginal, CreatedAtUtc)
                            VALUES($token, $hash, $protected, $created);
                            """,
                            [Parameter("$token", candidate), Parameter("$hash", originalHash),
                             Parameter("$protected", Protect(original)), Parameter("$created", DateTimeOffset.UtcNow.ToString("O"))]),
                            token).ConfigureAwait(false);
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                        return candidate;
                    }
                    catch (PluginSqlException exception) when (exception.Error == PluginSqlError.ConstraintViolation)
                    {
                        // 清理不能沿用已取消令牌，否则失败事务可能仍占用句柄。
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                existing = await FindByHashAsync(db, originalHash, token).ConfigureAwait(false);
                if (existing is not null) return existing;
            }
        }
        finally { gate.Release(); }
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveTokensAsync(
        IEnumerable<string> tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            var requested = tokens.Distinct(StringComparer.Ordinal).ToArray();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (requested.Length == 0) return result;
            var db = await GetDatabaseAsync(token).ConfigureAwait(false);
            foreach (var candidate in requested)
            {
                var row = await db.QuerySingleAsync(new PluginSqlCommand(
                    "SELECT ProtectedOriginal FROM CredentialTokens WHERE Token = $token LIMIT 1;",
                    [Parameter("$token", candidate)]), token).ConfigureAwait(false);
                if (row is not null) result[candidate] = Unprotect(row[0].Text
                    ?? throw new CryptographicException("凭据映射不是受保护文本。"));
            }
            return result;
        }
        finally { gate.Release(); }
    }

    private static async ValueTask<string?> FindByHashAsync(
        IPluginSqliteDatabase db, string originalHash, CancellationToken token)
    {
        var row = await db.QuerySingleAsync(new PluginSqlCommand(
            "SELECT Token FROM CredentialTokens WHERE OriginalHash = $hash LIMIT 1;",
            [Parameter("$hash", originalHash)]), token).ConfigureAwait(false);
        return row?[0].Text;
    }

    private async ValueTask<IPluginSqliteDatabase> GetDatabaseAsync(CancellationToken token)
    {
        ThrowIfDisposed();
        token.ThrowIfCancellationRequested();
        if (database is not null) return database;
        var opened = await storage.OpenSqliteAsync(DatabaseFileName, token).ConfigureAwait(false);
        try
        {
            // 保留旧初始化 SQL；Bridge 每次只接受一条语句。
            foreach (var pragma in new[] { "PRAGMA journal_mode=WAL;", "PRAGMA synchronous=NORMAL;", "PRAGMA busy_timeout=5000;" })
                await opened.ExecuteAsync(new PluginSqlCommand(pragma), token).ConfigureAwait(false);
            await opened.ExecuteAsync(new PluginSqlCommand(
                """
                CREATE TABLE IF NOT EXISTS CredentialTokens (
                    Token TEXT NOT NULL PRIMARY KEY,
                    OriginalHash TEXT NOT NULL UNIQUE,
                    ProtectedOriginal TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                """), token).ConfigureAwait(false);
            database = opened;
            return opened;
        }
        catch
        {
            await opened.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static PluginSqlParameter Parameter(string name, string value) => new(name, PluginSqlValue.FromText(value));
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    public ValueTask DisposeAsync()
    {
        // 并发释放共享一次清理，保证所有调用者都等到句柄和取消资源真正退休。
        lock (disposalGate) return new ValueTask(disposalTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        await lifetime.CancelAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (database is null) return;
            var opened = database;
            database = null;
            await opened.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lifetime.Dispose();
            gate.Release();
        }
    }

    private static string ComputeHash(string original) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)));

    private static string Protect(string original)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Credential token persistence requires Windows DPAPI.");
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(original),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string value)
    {
        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            throw new CryptographicException("Credential token value is not DPAPI protected.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Credential token persistence requires Windows DPAPI.");
        var bytes = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
    }
}
