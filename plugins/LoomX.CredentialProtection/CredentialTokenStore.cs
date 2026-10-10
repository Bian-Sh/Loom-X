using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LoomX.CredentialProtection;

/// <summary>
/// Plugin-owned 长期 token 映射。原值使用当前 Windows 用户的 DPAPI 加密后写入 SQLite，
/// 数据库不保存明文；原值哈希仅用于稳定复用同一 token。
/// </summary>
public sealed class CredentialTokenStore
{
    private const string DatabaseFileName = "credential-tokens.db";
    private const string ProtectedPrefix = "dpapi:v1:";
    private readonly string connectionString;
    private readonly object gate = new();

    public CredentialTokenStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, DatabaseFileName),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        connectionString = builder.ToString();
        Initialize();
    }

    public string GetOrCreateToken(string original, Func<string> createToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(createToken);
        var originalHash = ComputeHash(original);

        lock (gate)
        {
            var existing = FindByHash(originalHash);
            if (existing is not null) return existing;

            // 主键冲突后不能复用已失败的事务：事务已进入错误状态，
            // 继续在同一事务里重试会为同一原值签出第二个 token，
            // 导致同一段明文被替换两次、产出嵌套 placeholder。
            // 因此每次尝试都开启独立事务，冲突时回滚并在新事务中查回既有 token。
            while (true)
            {
                var token = createToken();
                var conflict = false;

                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = transaction;
                        insert.CommandText = """
                            INSERT INTO CredentialTokens(Token, OriginalHash, ProtectedOriginal, CreatedAtUtc)
                            VALUES($token, $hash, $protected, $created);
                            """;
                        insert.Parameters.AddWithValue("$token", token);
                        insert.Parameters.AddWithValue("$hash", originalHash);
                        insert.Parameters.AddWithValue("$protected", Protect(original));
                        insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                        insert.ExecuteNonQuery();
                        transaction.Commit();
                        return token;
                    }
                    catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                    {
                        conflict = true;
                        transaction.Rollback();
                    }
                }

                if (!conflict) return token;

                existing = FindByHash(originalHash);
                if (existing is not null) return existing;
            }
        }
    }

    private string? FindByHash(string originalHash)
    {
        using var connection = OpenConnection();
        using var find = connection.CreateCommand();
        find.CommandText = "SELECT Token FROM CredentialTokens WHERE OriginalHash = $hash LIMIT 1;";
        find.Parameters.AddWithValue("$hash", originalHash);
        return find.ExecuteScalar() as string;
    }

    public IReadOnlyDictionary<string, string> ResolveTokens(IEnumerable<string> tokens)
    {
        var requested = tokens.Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        lock (gate)
        {
            using var connection = OpenConnection();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var token in requested)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT ProtectedOriginal FROM CredentialTokens WHERE Token = $token LIMIT 1;";
                command.Parameters.AddWithValue("$token", token);
                if (command.ExecuteScalar() is string protectedOriginal)
                    result[token] = Unprotect(protectedOriginal);
            }
            return result;
        }
    }

    private void Initialize()
    {
        lock (gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS CredentialTokens (
                    Token TEXT NOT NULL PRIMARY KEY,
                    OriginalHash TEXT NOT NULL UNIQUE,
                    ProtectedOriginal TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
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
