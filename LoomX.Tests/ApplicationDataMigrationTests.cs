using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using LoomX.Activity;
using LoomX.Configuration;
using LoomX.Logging;
using LoomX.Tests.Logging;
using Xunit;

namespace LoomX.Tests;

public sealed class ApplicationDataMigrationTests
{
    [Fact]
    public async Task MigratesConfigurationAndActivityAndKeepsLegacyFiles()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        var legacyConfig = Path.Combine(legacyRoot, "OllamaHub.db");
        var legacyActivity = Path.Combine(legacyRoot, "Activity.db");

        try
        {
            await SeedConfigurationAsync(legacyConfig);
            await SeedActivityAsync(legacyActivity);
            var logger = new RecordingLogger<ApplicationDataMigration>();
            var migration = new ApplicationDataMigration(legacyRoot, newRoot, logger);

            await migration.EnsureMigratedAsync();

            var targetConfig = Path.Combine(newRoot, "LoomX.db");
            var targetActivity = Path.Combine(newRoot, "LoomX.Activity.db");
            Assert.True(File.Exists(targetConfig));
            Assert.True(File.Exists(targetActivity));
            Assert.True(File.Exists(legacyConfig));
            Assert.True(File.Exists(legacyActivity));

            await using (var config = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={targetConfig}").Options))
            {
                Assert.Equal("secret-ciphertext", (await config.Providers.SingleAsync()).ProtectedApiKey);
            }

            await using (var activity = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={targetActivity}").Options))
            {
                Assert.Equal("request-1", (await activity.Events.SingleAsync()).RequestId);
            }

            Assert.Contains(logger.Messages, message => message.Contains("配置库", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("活动库", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("secret-ciphertext", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExistingTargetWinsAndRepeatedMigrationIsIdempotent()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        var legacyPath = Path.Combine(legacyRoot, "OllamaHub.db");
        var targetPath = Path.Combine(newRoot, "LoomX.db");

        try
        {
            await SeedConfigurationAsync(legacyPath, "legacy-secret");
            await SeedConfigurationAsync(targetPath, "target-secret");
            var before = await File.ReadAllBytesAsync(targetPath);
            var migration = new ApplicationDataMigration(legacyRoot, newRoot, new RecordingLogger<ApplicationDataMigration>());

            await migration.EnsureMigratedAsync();
            await migration.EnsureMigratedAsync();

            Assert.Equal(before, await File.ReadAllBytesAsync(targetPath));
            await using var db = OpenConfiguration(targetPath);
            Assert.Equal("target-secret", (await db.Providers.SingleAsync()).ProtectedApiKey);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MissingLegacyDatabasesAreSkippedAndStaleTemporaryFilesAreRemoved()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        Directory.CreateDirectory(newRoot);
        var staleTemporaryPath = Path.Combine(newRoot, "LoomX.db.migrating.stale.tmp");
        await File.WriteAllTextAsync(staleTemporaryPath, "stale");

        try
        {
            var migration = new ApplicationDataMigration(legacyRoot, newRoot, new RecordingLogger<ApplicationDataMigration>());

            await migration.EnsureMigratedAsync();

            Assert.False(File.Exists(Path.Combine(newRoot, "LoomX.db")));
            Assert.False(File.Exists(Path.Combine(newRoot, "LoomX.Activity.db")));
            Assert.False(File.Exists(staleTemporaryPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task WalChangesAreIncludedInSnapshot()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        var legacyPath = Path.Combine(legacyRoot, "OllamaHub.db");

        try
        {
            await SeedConfigurationAsync(legacyPath, "before-wal");
            await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = legacyPath,
                Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await writer.OpenAsync();
            await ExecuteAsync(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=1000000;");
            await ExecuteAsync(writer, "UPDATE Providers SET ProtectedApiKey = 'after-wal'");
            Assert.True(File.Exists(legacyPath + "-wal"));

            var migration = new ApplicationDataMigration(legacyRoot, newRoot, new RecordingLogger<ApplicationDataMigration>());
            await migration.EnsureMigratedAsync();

            await using var target = OpenConfiguration(Path.Combine(newRoot, "LoomX.db"));
            Assert.Equal("after-wal", (await target.Providers.SingleAsync()).ProtectedApiKey);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CorruptSourceDoesNotCreateTargetOrLeaveTemporaryFile()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        var legacyPath = Path.Combine(legacyRoot, "OllamaHub.db");
        await File.WriteAllTextAsync(legacyPath, "not a sqlite database");

        try
        {
            var migration = new ApplicationDataMigration(legacyRoot, newRoot, new RecordingLogger<ApplicationDataMigration>());

            var exception = await Assert.ThrowsAsync<ApplicationDataMigrationException>(() => migration.EnsureMigratedAsync());

            Assert.Equal("配置库", exception.DatabaseKind);
            Assert.Equal("快照", exception.Stage);
            Assert.False(File.Exists(Path.Combine(newRoot, "LoomX.db")));
            Assert.Empty(Directory.EnumerateFiles(newRoot, "LoomX.db.migrating.*.tmp"));
            Assert.True(File.Exists(legacyPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MissingRequiredTableIsRejectedBeforeCommit()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        var legacyPath = Path.Combine(legacyRoot, "OllamaHub.db");

        try
        {
            await using (var connection = new SqliteConnection(CreateTestConnectionString(legacyPath)))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, "CREATE TABLE AppSettings (Id INTEGER PRIMARY KEY);");
            }

            var migration = new ApplicationDataMigration(legacyRoot, newRoot, new RecordingLogger<ApplicationDataMigration>());
            var exception = await Assert.ThrowsAsync<ApplicationDataMigrationException>(() => migration.EnsureMigratedAsync());

            Assert.Equal("配置库", exception.DatabaseKind);
            Assert.Equal("完整性检查", exception.Stage);
            Assert.False(File.Exists(Path.Combine(newRoot, "LoomX.db")));
            Assert.Empty(Directory.EnumerateFiles(newRoot, "LoomX.db.migrating.*.tmp"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ConfigurationSuccessActivityFailureCanRetryOnlyActivity()
    {
        var root = CreateDirectory();
        var legacyRoot = Path.Combine(root, "OllamaHub");
        var newRoot = Path.Combine(root, "LoomX");
        Directory.CreateDirectory(legacyRoot);
        var legacyConfig = Path.Combine(legacyRoot, "OllamaHub.db");
        var legacyActivity = Path.Combine(legacyRoot, "Activity.db");

        try
        {
            await SeedConfigurationAsync(legacyConfig, "config-secret");
            await File.WriteAllTextAsync(legacyActivity, "broken activity database");
            var migration = new ApplicationDataMigration(legacyRoot, newRoot, new RecordingLogger<ApplicationDataMigration>());

            var firstFailure = await Assert.ThrowsAsync<ApplicationDataMigrationException>(() => migration.EnsureMigratedAsync());
            Assert.Equal("活动库", firstFailure.DatabaseKind);
            Assert.True(File.Exists(Path.Combine(newRoot, "LoomX.db")));
            Assert.False(File.Exists(Path.Combine(newRoot, "LoomX.Activity.db")));

            File.Delete(legacyActivity);
            await SeedActivityAsync(legacyActivity);
            await migration.EnsureMigratedAsync();

            Assert.True(File.Exists(Path.Combine(newRoot, "LoomX.Activity.db")));
            await using var config = OpenConfiguration(Path.Combine(newRoot, "LoomX.db"));
            Assert.Equal("config-secret", (await config.Providers.SingleAsync()).ProtectedApiKey);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task SeedConfigurationAsync(string path, string protectedApiKey = "secret-ciphertext")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var db = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite(CreateTestConnectionString(path)).Options);
        await db.Database.EnsureCreatedAsync();
        db.Providers.Add(new ProviderEntity
        {
            BusinessId = "provider-1",
            DisplayName = "测试 Provider",
            BaseUrl = "https://example.com",
            ProtectedApiKey = protectedApiKey
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedActivityAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite(CreateTestConnectionString(path)).Options);
        await db.Database.EnsureCreatedAsync();
        db.Events.Add(new ActivityEventEntity
        {
            CreatedAt = DateTimeOffset.UtcNow,
            RequestId = "request-1",
            IncomingPath = "/api/tags",
            Protocol = "Ollama",
            Route = "Ollama 直通",
            StatusCode = 200
        });
        await db.SaveChangesAsync();
    }

    private static ConfigurationDbContext OpenConfiguration(string path) =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite(CreateTestConnectionString(path)).Options);

    private static string CreateTestConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false
    }.ToString();

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(directory); attempt++)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { if (attempt < 19) Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { if (attempt < 19) Thread.Sleep(50); }
        }
    }
}
