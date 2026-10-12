using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

/// <summary>测试专用旧格式 Vault；只在明确的新建临时数据根写入合成记录。</summary>
internal static class LegacyVaultFixture
{
    public const string PluginId = "loomx.credential-protection";
    public const string Secret = "sk-playground-legacy-example-0123456789";
    public const string Token = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
    public static string Seed(string dataRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("历史 Vault 检查要求 Windows DPAPI。");
        var directory = Path.Combine(dataRoot, PluginId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "credential-tokens.db");
        if (File.Exists(path)) throw new InvalidOperationException("禁止覆盖已有 Vault。");
        var protectedValue = "dpapi:v1:" + Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(Secret), null, DataProtectionScope.CurrentUser));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE CredentialTokens (
                Token TEXT NOT NULL PRIMARY KEY,
                OriginalHash TEXT NOT NULL UNIQUE,
                ProtectedOriginal TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO CredentialTokens(Token,OriginalHash,ProtectedOriginal,CreatedAtUtc) VALUES($token,$hash,$protected,$created);";
        insert.Parameters.AddWithValue("$token", Token);
        insert.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))));
        insert.Parameters.AddWithValue("$protected", protectedValue);
        insert.Parameters.AddWithValue("$created", "2026-01-01T00:00:00.0000000+00:00");
        insert.ExecuteNonQuery();
        return protectedValue;
    }
}
