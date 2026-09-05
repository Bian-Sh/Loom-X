using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests;

public sealed class ConfigSnapshotServiceTests
{
    [Fact]
    public void FileStateReadCanOpenWhileDatabaseWriterHandleIsActive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loomx-config-state-{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(path, "database");
            using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var reader = ConfigSnapshotService.OpenFileForState(path);

            Assert.Equal("database", new StreamReader(reader).ReadToEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ListProvidersRepairsMissingModelMetadataColumnsBeforeRead()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loomx-config-read-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            await using (var db = new ConfigurationDbContext(options))
                await ConfigurationDatabase.InitializeAsync(db);

            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE Models DROP COLUMN OwnedBy; ALTER TABLE Models DROP COLUMN RemoteFamily; ALTER TABLE Models DROP COLUMN RemoteContextLength; ALTER TABLE Models DROP COLUMN RemoteMaxTokens; ALTER TABLE Models DROP COLUMN RemoteVision;";
                await command.ExecuteNonQueryAsync();
            }

            using var service = new ConfigSnapshotService(path);
            var providers = await service.ListProvidersAsync();

            Assert.Empty(providers);
            await using var verifyConnection = new SqliteConnection($"Data Source={path}");
            await verifyConnection.OpenAsync();
            await using var verifyCommand = verifyConnection.CreateCommand();
            verifyCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Models') WHERE name IN ('OwnedBy', 'RemoteFamily', 'RemoteContextLength', 'RemoteMaxTokens', 'RemoteVision')";
            Assert.Equal(5L, (long)(await verifyCommand.ExecuteScalarAsync())!);
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }
}
