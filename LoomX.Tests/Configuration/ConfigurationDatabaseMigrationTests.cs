using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
using Xunit;

namespace LoomX.Tests.Configuration;

public sealed class ConfigurationDatabaseMigrationTests
{
    [Fact]
    public async Task LegacyGatewaySchema_IsMergedIntoGlobalCombosAndEndpointBindings()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"loomx-migration-{Guid.NewGuid():N}.db");
        var sharedOpenAiId = Guid.NewGuid();
        var sharedOllamaId = Guid.NewGuid();
        var sharedAzureId = Guid.NewGuid();
        var localId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var modelOneId = Guid.NewGuid();
        var modelTwoId = Guid.NewGuid();
        try
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var context = new ConfigurationDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.GatewayConfigurations.Add(new GatewayConfigurationEntity());
                context.AppSettings.Add(new AppSettingsEntity());
                context.GatewayEndpoints.AddRange(
                    new GatewayEndpointEntity { Key = "openai", DisplayName = "OpenAI", PublicPath = "/openai" },
                    new GatewayEndpointEntity { Key = "ollama", DisplayName = "Ollama", PublicPath = "/" },
                    new GatewayEndpointEntity { Key = "azure", DisplayName = "Azure", PublicPath = "/azure" });
                var provider = new ProviderEntity { BusinessId = "migration-provider", DisplayName = "迁移 Provider", BaseUrl = "https://example.invalid" };
                provider.Models.Add(new ModelEntity { Id = modelOneId, ModelId = "model-one", DisplayName = "模型一" });
                provider.Models.Add(new ModelEntity { Id = modelTwoId, ModelId = "model-two", DisplayName = "模型二" });
                context.Providers.Add(provider);
                await context.SaveChangesAsync();

                await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
                await context.Database.ExecuteSqlRawAsync("DROP TABLE GatewayEndpointComboBindings");
                await context.Database.ExecuteSqlRawAsync("DROP TABLE GatewayRoutes");
                await context.Database.ExecuteSqlRawAsync("DROP TABLE GatewayCombos");
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE GatewayCombos (
                        Id TEXT NOT NULL PRIMARY KEY,
                        EndpointKey TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Enabled INTEGER NOT NULL,
                        SortOrder INTEGER NOT NULL
                    )
                    """);
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE GatewayRoutes (
                        Id TEXT NOT NULL PRIMARY KEY,
                        EndpointKey TEXT NOT NULL,
                        ComboId TEXT NULL,
                        ModelId TEXT NOT NULL,
                        Alias TEXT NULL,
                        Enabled INTEGER NOT NULL,
                        SortOrder INTEGER NOT NULL
                    )
                    """);

                await InsertComboAsync(context, sharedOpenAiId, "openai", "共享模型", true, 1);
                await InsertComboAsync(context, sharedOllamaId, "ollama", "共享模型", false, 0);
                await InsertComboAsync(context, sharedAzureId, "azure", "共享模型", true, 3);
                await InsertComboAsync(context, localId, "ollama", "本地专用", true, 5);
                await InsertComboAsync(context, orphanId, "孤儿组合", "missing", true, 0);

                await InsertRouteAsync(context, Guid.NewGuid(), "openai", sharedOpenAiId, modelOneId, true, 2);
                await InsertRouteAsync(context, Guid.NewGuid(), "ollama", sharedOllamaId, modelOneId, false, 1);
                await InsertRouteAsync(context, Guid.NewGuid(), "azure", sharedAzureId, modelTwoId, true, 0);
                await InsertRouteAsync(context, Guid.NewGuid(), "ollama", localId, modelOneId, true, 0);
                await InsertRouteAsync(context, Guid.NewGuid(), "openai", null, modelTwoId, true, 99);
                await InsertRouteAsync(context, Guid.NewGuid(), "openai", sharedOpenAiId, Guid.NewGuid(), true, 10);
                context.ChangeTracker.Clear();
            }

            await using (var migratedContext = new ConfigurationDbContext(options))
            {
                await ConfigurationDatabase.InitializeAsync(migratedContext);

                var combos = await migratedContext.GatewayCombos
                    .AsNoTracking()
                    .Include(combo => combo.EndpointBindings)
                    .Include(combo => combo.Routes)
                    .ToListAsync();
                var shared = Assert.Single(combos, combo => combo.Name == "共享模型");
                var local = Assert.Single(combos, combo => combo.Name == "本地专用");

                Assert.Equal(2, combos.Count);
                Assert.Equal(3, shared.EndpointBindings.Count);
                Assert.Equal(["ollama", "openai", "azure"], shared.EndpointBindings.OrderBy(item => item.SortOrder).Select(item => item.EndpointKey));
                Assert.True(shared.Enabled);
                Assert.Equal(2, shared.Routes.Count);
                Assert.Equal(new[] { modelOneId, modelTwoId }, shared.Routes.OrderBy(item => item.SortOrder).Select(item => item.ModelId));
                Assert.True(shared.Routes.All(item => item.Enabled));
                Assert.Single(local.EndpointBindings);
                Assert.Equal("ollama", local.EndpointBindings[0].EndpointKey);
                Assert.Single(local.Routes);
                Assert.Equal(modelOneId, local.Routes[0].ModelId);

                var storedModelIds = await ReadValuesAsync(migratedContext, "SELECT Id FROM Models");
                var storedRouteModelIds = await ReadValuesAsync(migratedContext, "SELECT ModelId FROM GatewayRoutes");
                Assert.All(storedRouteModelIds, modelId => Assert.Contains(modelId, storedModelIds));
                Assert.DoesNotContain("EndpointKey", await ReadColumnsAsync(migratedContext, "GatewayCombos"));
                Assert.DoesNotContain("EndpointKey", await ReadColumnsAsync(migratedContext, "GatewayRoutes"));
                Assert.Empty(await ReadForeignKeyViolationsAsync(migratedContext));

                await ConfigurationDatabase.InitializeAsync(migratedContext);
                Assert.Equal(2, await migratedContext.GatewayCombos.CountAsync());
                Assert.Equal(3, await migratedContext.GatewayRoutes.CountAsync());
                Assert.Equal(4, await migratedContext.GatewayEndpointComboBindings.CountAsync());
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static Task<int> InsertComboAsync(ConfigurationDbContext context, Guid id, string endpointKey, string name, bool enabled, int sortOrder) =>
        context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO GatewayCombos (Id, EndpointKey, Name, Enabled, SortOrder) VALUES ({id.ToString()}, {endpointKey}, {name}, {enabled}, {sortOrder})");

    private static Task<int> InsertRouteAsync(ConfigurationDbContext context, Guid id, string endpointKey, Guid? comboId, Guid modelId, bool enabled, int sortOrder) =>
        context.Database.ExecuteSqlRawAsync(
            "INSERT INTO GatewayRoutes (Id, EndpointKey, ComboId, ModelId, Alias, Enabled, SortOrder) VALUES ($id, $endpointKey, $comboId, $modelId, NULL, $enabled, $sortOrder)",
            new SqliteParameter("$id", id.ToString()),
            new SqliteParameter("$endpointKey", endpointKey),
            new SqliteParameter("$comboId", comboId?.ToString() ?? (object)DBNull.Value),
            new SqliteParameter("$modelId", modelId.ToString()),
            new SqliteParameter("$enabled", enabled ? 1 : 0),
            new SqliteParameter("$sortOrder", sortOrder));

    private static async Task<HashSet<string>> ReadColumnsAsync(ConfigurationDbContext context, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return result;
    }

    private static async Task<List<string>> ReadValuesAsync(ConfigurationDbContext context, string sql)
    {
        var result = new List<string>();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return result;
    }

    private static async Task<List<string>> ReadForeignKeyViolationsAsync(ConfigurationDbContext context)
    {
        var result = new List<string>();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(string.Join("/", Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index)?.ToString() ?? string.Empty)));
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return result;
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { File.Delete(path); break; }
                catch (IOException) when (attempt < 4) { Thread.Sleep(50); }
                catch (IOException) { }
            }
        }
    }
}
