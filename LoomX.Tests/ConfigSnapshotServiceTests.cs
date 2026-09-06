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

    [Fact]
    public async Task GatewayComboAndRouteUpdatesReturnPersistedRowsAfterReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loomx-config-gateway-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            await using (var db = new ConfigurationDbContext(options))
                await ConfigurationDatabase.InitializeAsync(db);

            using var service = new ConfigSnapshotService(path);
            var provider = await service.CreateProviderAsync(new ProviderInput("gateway", "网关 Provider", "https://example.com", "openai", true, null, false, null));
            var model = await service.CreateModelAsync(provider.Id, new ModelInput("model", "模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));
            var combo = await service.CreateGatewayComboAsync(new GatewayComboInput("coding", true, 0));
            var route = await service.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(model.Id, true, 0));

            var updatedCombo = await service.UpdateGatewayComboAsync(combo.Id, new GatewayComboInput("coding-updated", false, 1));
            Assert.Equal("coding-updated", updatedCombo.Name);
            Assert.False(updatedCombo.Enabled);
            Assert.Equal(1, updatedCombo.SortOrder);
            Assert.Single(updatedCombo.Routes);

            var updatedRoute = await service.UpdateGatewayRouteAsync(route.Id, new GatewayRouteInput(model.Id, false, 2));
            Assert.Equal(route.Id, updatedRoute.Id);
            Assert.False(updatedRoute.Enabled);
            Assert.Equal(2, updatedRoute.SortOrder);

            var listed = Assert.Single(await service.ListGatewayCombosAsync());
            Assert.Equal("coding-updated", listed.Name);
            Assert.False(listed.Enabled);
            Assert.Equal(1, listed.SortOrder);
            Assert.False(Assert.Single(listed.Routes).Enabled);
            Assert.Equal(2, listed.Routes[0].SortOrder);
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public async Task GatewayGuidUpdatesSupportLegacyTextIds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loomx-config-gateway-legacy-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            await using (var db = new ConfigurationDbContext(options))
                await ConfigurationDatabase.InitializeAsync(db);

            using var service = new ConfigSnapshotService(path);
            var provider = await service.CreateProviderAsync(new ProviderInput("legacy-gateway", "旧库 Provider", "https://example.com", "openai", true, null, false, null));
            var model = await service.CreateModelAsync(provider.Id, new ModelInput("legacy-model", "旧库模型", null, "gpt", null, null, 128000, 4096, false, null, null, true, null, false, null, null));
            var combo = await service.CreateGatewayComboAsync(new GatewayComboInput("legacy-combo", true, 0));
            var route = await service.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(model.Id, true, 0));

            await using (var db = new ConfigurationDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE GatewayCombos SET Id = {combo.Id.ToString()} WHERE Id = {combo.Id}");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE GatewayRoutes SET Id = {route.Id.ToString()} WHERE Id = {route.Id}");
            }

            var updatedCombo = await service.UpdateGatewayComboAsync(combo.Id, new GatewayComboInput("legacy-combo-updated", false, 1));
            Assert.Equal("legacy-combo-updated", updatedCombo.Name);
            Assert.False(updatedCombo.Enabled);
            var updatedRoute = await service.UpdateGatewayRouteAsync(route.Id, new GatewayRouteInput(model.Id, false, 2));
            Assert.False(updatedRoute.Enabled);
            Assert.Equal(2, updatedRoute.SortOrder);
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
