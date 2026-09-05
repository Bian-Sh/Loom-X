using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace LoomX.Configuration;

public sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : DbContext(options)
{
    public DbSet<GatewayConfigurationEntity> GatewayConfigurations => Set<GatewayConfigurationEntity>();
    public DbSet<AppSettingsEntity> AppSettings => Set<AppSettingsEntity>();
    public DbSet<ProviderEntity> Providers => Set<ProviderEntity>();
    public DbSet<ModelEntity> Models => Set<ModelEntity>();
    public DbSet<GatewayEndpointEntity> GatewayEndpoints => Set<GatewayEndpointEntity>();
    public DbSet<GatewayComboEntity> GatewayCombos => Set<GatewayComboEntity>();
    public DbSet<GatewayRouteEntity> GatewayRoutes => Set<GatewayRouteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GatewayConfigurationEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ListenUrl).HasMaxLength(2048).IsRequired();
        });
        modelBuilder.Entity<AppSettingsEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Language).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Theme).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ProxyMode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ProxyHost).HasMaxLength(2048).IsRequired();
            entity.Property(item => item.ProxyUsername).HasMaxLength(256);
            entity.Property(item => item.UpdateChannel).HasMaxLength(32).IsRequired();
            entity.Property(item => item.UseProxyForUpdates).HasDefaultValue(true);
        });
        modelBuilder.Entity<ProviderEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.BusinessId).IsUnique();
            entity.Property(item => item.BusinessId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(item => item.BaseUrl).HasMaxLength(2048).IsRequired();
            entity.Property(item => item.ModelListUrl).HasMaxLength(2048);
            entity.Property(item => item.EndpointFormat).HasMaxLength(32).IsRequired().HasDefaultValue("responses");
            entity.HasMany(item => item.Models).WithOne(item => item.Provider).HasForeignKey(item => item.ProviderId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ModelEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProviderId, item.ModelId }).IsUnique();
            entity.Property(item => item.ModelId).HasMaxLength(256).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(item => item.OwnedBy).HasMaxLength(256);
            entity.Property(item => item.RemoteFamily).HasMaxLength(128);
        });
        modelBuilder.Entity<GatewayEndpointEntity>(entity =>
        {
            entity.HasKey(item => item.Key);
            entity.Property(item => item.Key).HasMaxLength(32).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(64).IsRequired();
            entity.Property(item => item.PublicPath).HasMaxLength(128).IsRequired();
            entity.HasMany(item => item.Routes).WithOne(item => item.Endpoint).HasForeignKey(item => item.EndpointKey).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(item => item.Combos).WithOne(item => item.Endpoint).HasForeignKey(item => item.EndpointKey).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GatewayComboEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.EndpointKey, item.Name }).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(256).IsRequired().UseCollation("NOCASE");
            entity.Property(item => item.EndpointKey).HasMaxLength(32).IsRequired();
            entity.HasMany(item => item.Routes).WithOne(item => item.Combo).HasForeignKey(item => item.ComboId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GatewayRouteEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ComboId, item.ModelId }).IsUnique();
            entity.Property(item => item.EndpointKey).HasMaxLength(32).IsRequired();
            entity.HasOne(item => item.Model).WithMany().HasForeignKey(item => item.ModelId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

public sealed class GatewayConfigurationEntity
{
    public int Id { get; set; } = 1;
    public string ListenUrl { get; set; } = "http://127.0.0.1:11434";
}

public sealed class AppSettingsEntity
{
    public int Id { get; set; } = 1;
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "system";
    public string ProxyMode { get; set; } = "direct";
    public string ProxyHost { get; set; } = "http://127.0.0.1";
    public int ProxyPort { get; set; } = 7890;
    public string? ProxyUsername { get; set; }
    public string? ProtectedProxyPassword { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public string UpdateChannel { get; set; } = "stable";
    public bool UseProxyForUpdates { get; set; } = true;
    public bool DiagnosticsEnabled { get; set; }
    public int LogRetentionDays { get; set; } = 30;
    public bool LogStackTrace { get; set; }
    public bool TransparencyEnabled { get; set; } = true;
    public int TransparencyOpacity { get; set; } = 86;
    public int BlurAmount { get; set; } = 24;
    public string TransparencyAlgorithm { get; set; } = "acrylic";
}

public sealed class ProviderEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BusinessId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ModelListUrl { get; set; }
    public string ApiMode { get; set; } = "openai";
    public string EndpointFormat { get; set; } = "responses";
    public bool Enabled { get; set; } = true;
    public bool UseProxy { get; set; }
    public string? ProtectedApiKey { get; set; }
    public string HeadersJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public List<ModelEntity> Models { get; set; } = [];
}

public sealed class ModelEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public ProviderEntity Provider { get; set; } = null!;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ConfigId { get; set; }
    public string Family { get; set; } = "claude";
    public string? BaseUrl { get; set; }
    public string? ProtectedApiKey { get; set; }
    public string? ApiMode { get; set; }
    public int ContextLength { get; set; } = 128000;
    public int MaxTokens { get; set; } = 4096;
    public bool Vision { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public string HeadersJson { get; set; } = "{}";
    public string ExtraJson { get; set; } = "{}";
    public string? OwnedBy { get; set; }
    public string? RemoteFamily { get; set; }
    public int? RemoteContextLength { get; set; }
    public int? RemoteMaxTokens { get; set; }
    public bool? RemoteVision { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class GatewayEndpointEntity
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PublicPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<GatewayComboEntity> Combos { get; set; } = [];
    public List<GatewayRouteEntity> Routes { get; set; } = [];
}

public sealed class GatewayComboEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EndpointKey { get; set; } = string.Empty;
    public GatewayEndpointEntity Endpoint { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public List<GatewayRouteEntity> Routes { get; set; } = [];
}

public sealed class GatewayRouteEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EndpointKey { get; set; } = string.Empty;
    public GatewayEndpointEntity Endpoint { get; set; } = null!;
    public Guid? ComboId { get; set; }
    public GatewayComboEntity? Combo { get; set; }
    public Guid ModelId { get; set; }
    public ModelEntity Model { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public static class ConfigurationDatabase
{
    public static IDisposable AcquireInitializationLock()
    {
        AppDataPaths.EnsureCreated();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            try
            {
                return new FileStream(AppDataPaths.ConfigurationInitializationLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("配置库初始化锁等待超时。");
        }
    }

    public static async Task InitializeAsync(ConfigurationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        using var initializationLock = AcquireInitializationLock();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        if (await IsSchemaReadyAsync(dbContext, cancellationToken)) return;

        await EnsureSchemaAsync(dbContext, cancellationToken);
        if (!await dbContext.GatewayConfigurations.AnyAsync(cancellationToken))
        {
            dbContext.GatewayConfigurations.Add(new GatewayConfigurationEntity());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!await dbContext.AppSettings.AnyAsync(cancellationToken))
        {
            dbContext.AppSettings.Add(new AppSettingsEntity());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<bool> IsSchemaReadyAsync(ConfigurationDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var table in new[] { "AppSettings", "GatewayConfigurations", "Providers", "Models", "GatewayEndpoints", "GatewayCombos", "GatewayRoutes" })
            {
                await using var tableCommand = connection.CreateCommand();
                tableCommand.CommandText = $"SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{table}')";
                if (Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken)) != 1) return false;
            }

            if (!await HasColumnsAsync(connection, "AppSettings", cancellationToken,
                    "Id", "Language", "Theme", "ProxyMode", "ProxyHost", "ProxyPort", "ProxyUsername", "ProtectedProxyPassword",
                    "AutoCheckUpdates", "UpdateChannel", "DiagnosticsEnabled", "LogRetentionDays", "LogStackTrace",
                    "TransparencyEnabled", "TransparencyOpacity", "BlurAmount", "TransparencyAlgorithm", "UseProxyForUpdates")
                || await HasColumnAsync(connection, "AppSettings", "OpenControlCenterOnStartup", cancellationToken))
                return false;

            if (!await HasColumnsAsync(connection, "GatewayConfigurations", cancellationToken, "Id", "ListenUrl")
                || !await HasColumnsAsync(connection, "Providers", cancellationToken,
                    "Id", "BusinessId", "DisplayName", "BaseUrl", "ModelListUrl", "ApiMode", "EndpointFormat", "Enabled", "UseProxy", "ProtectedApiKey", "HeadersJson", "SortOrder")
                || !await HasColumnsAsync(connection, "Models", cancellationToken,
                    "Id", "ProviderId", "ModelId", "DisplayName", "ConfigId", "Family", "BaseUrl", "ProtectedApiKey", "ApiMode",
                    "ContextLength", "MaxTokens", "Vision", "Temperature", "TopP", "HeadersJson", "ExtraJson", "OwnedBy", "RemoteFamily", "RemoteContextLength", "RemoteMaxTokens", "RemoteVision", "Enabled", "SortOrder")
                || !await HasColumnsAsync(connection, "GatewayEndpoints", cancellationToken, "Key", "DisplayName", "PublicPath", "Enabled")
                || !await HasColumnsAsync(connection, "GatewayCombos", cancellationToken, "Id", "EndpointKey", "Name", "Enabled", "SortOrder")
                || !await HasColumnsAsync(connection, "GatewayRoutes", cancellationToken, "Id", "EndpointKey", "ComboId", "ModelId", "Enabled", "SortOrder"))
                return false;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        if (!await dbContext.GatewayConfigurations.AsNoTracking().AnyAsync(cancellationToken)
            || !await dbContext.AppSettings.AsNoTracking().AnyAsync(cancellationToken))
            return false;

        var endpoints = await dbContext.GatewayEndpoints.AsNoTracking().ToDictionaryAsync(item => item.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);
        return endpoints.TryGetValue("openai", out var openAi) && openAi.PublicPath == "/openai"
            && endpoints.TryGetValue("ollama", out var ollama) && ollama.PublicPath == "/"
            && endpoints.TryGetValue("azure", out var azure) && azure.PublicPath == "/azure";
    }

    private static async Task<bool> HasColumnsAsync(DbConnection connection, string table, CancellationToken cancellationToken, params string[] columns)
    {
        await using var command = connection.CreateCommand();
        var names = string.Join(", ", columns.Select(column => $"'{column.Replace("'", "''", StringComparison.Ordinal)}'"));
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name IN ({names})";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == columns.Length;
    }

    private static async Task<bool> HasColumnAsync(DbConnection connection, string table, string column, CancellationToken cancellationToken) =>
        await HasColumnsAsync(connection, table, cancellationToken, column);


    private static async Task EnsureSchemaAsync(ConfigurationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
                Language TEXT NOT NULL,
                Theme TEXT NOT NULL,
                ProxyMode TEXT NOT NULL,
                ProxyHost TEXT NOT NULL,
                ProxyPort INTEGER NOT NULL,
                ProxyUsername TEXT NULL,
                ProtectedProxyPassword TEXT NULL,
                AutoCheckUpdates INTEGER NOT NULL,
                UpdateChannel TEXT NOT NULL,
                UseProxyForUpdates INTEGER NOT NULL DEFAULT 1,
                DiagnosticsEnabled INTEGER NOT NULL,
                LogRetentionDays INTEGER NOT NULL,
                LogStackTrace INTEGER NOT NULL DEFAULT 0,
                TransparencyEnabled INTEGER NOT NULL DEFAULT 1,
                TransparencyOpacity INTEGER NOT NULL DEFAULT 86,
                BlurAmount INTEGER NOT NULL DEFAULT 24,
                TransparencyAlgorithm TEXT NOT NULL DEFAULT 'acrylic'
            )
            """, cancellationToken);

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings DROP COLUMN OpenControlCenterOnStartup", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
        {
        }

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings ADD COLUMN LogStackTrace INTEGER NOT NULL DEFAULT 0", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        foreach (var statement in new[]
        {
            "ALTER TABLE AppSettings ADD COLUMN TransparencyEnabled INTEGER NOT NULL DEFAULT 1",
            "ALTER TABLE AppSettings ADD COLUMN TransparencyOpacity INTEGER NOT NULL DEFAULT 86",
            "ALTER TABLE AppSettings ADD COLUMN BlurAmount INTEGER NOT NULL DEFAULT 24",
            "ALTER TABLE AppSettings ADD COLUMN TransparencyAlgorithm TEXT NOT NULL DEFAULT 'acrylic'"
        })
        {
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            }
            catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings ADD COLUMN UseProxyForUpdates INTEGER NOT NULL DEFAULT 1", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Providers ADD COLUMN UseProxy INTEGER NOT NULL DEFAULT 0", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Providers ADD COLUMN ModelListUrl TEXT NULL", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Providers ADD COLUMN EndpointFormat TEXT NOT NULL DEFAULT 'responses'", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        foreach (var statement in new[]
        {
            "ALTER TABLE Models ADD COLUMN OwnedBy TEXT NULL",
            "ALTER TABLE Models ADD COLUMN RemoteFamily TEXT NULL",
            "ALTER TABLE Models ADD COLUMN RemoteContextLength INTEGER NULL",
            "ALTER TABLE Models ADD COLUMN RemoteMaxTokens INTEGER NULL",
            "ALTER TABLE Models ADD COLUMN RemoteVision INTEGER NULL"
        })
        {
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            }
            catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
            }
        }
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS GatewayEndpoints (
                Key TEXT NOT NULL CONSTRAINT PK_GatewayEndpoints PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                PublicPath TEXT NOT NULL,
                Enabled INTEGER NOT NULL
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS GatewayRoutes (
                Id TEXT NOT NULL CONSTRAINT PK_GatewayRoutes PRIMARY KEY,
                EndpointKey TEXT NOT NULL,
                ComboId TEXT NULL,
                ModelId TEXT NOT NULL,
                Alias TEXT NULL,
                Enabled INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                CONSTRAINT FK_GatewayRoutes_GatewayEndpoints_EndpointKey FOREIGN KEY (EndpointKey) REFERENCES GatewayEndpoints (Key) ON DELETE CASCADE,
                CONSTRAINT FK_GatewayRoutes_Models_ModelId FOREIGN KEY (ModelId) REFERENCES Models (Id) ON DELETE RESTRICT
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS GatewayCombos (
                Id TEXT NOT NULL CONSTRAINT PK_GatewayCombos PRIMARY KEY,
                EndpointKey TEXT NOT NULL,
                Name TEXT NOT NULL,
                Enabled INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                CONSTRAINT FK_GatewayCombos_GatewayEndpoints_EndpointKey FOREIGN KEY (EndpointKey) REFERENCES GatewayEndpoints (Key) ON DELETE CASCADE,
                CONSTRAINT UQ_GatewayCombos_EndpointKey_Name UNIQUE (EndpointKey, Name)
            )
            """, cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE GatewayRoutes ADD COLUMN ComboId TEXT NULL", cancellationToken);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE GatewayEndpoints SET PublicPath = '/openai' WHERE Key = 'openai' AND PublicPath IN ('/v1', '/openai/v1', '/v1/responses')", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE GatewayEndpoints SET PublicPath = '/' WHERE Key = 'ollama' AND (PublicPath = '/api' OR PublicPath = '')", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE GatewayEndpoints SET PublicPath = '/azure' WHERE Key = 'azure' AND PublicPath IN ('/azure/v1', '/azure/v1/responses')", cancellationToken);
        foreach (var endpoint in new[]
        {
            new GatewayEndpointEntity { Key = "openai", DisplayName = "OpenAI", PublicPath = "/openai" },
            new GatewayEndpointEntity { Key = "ollama", DisplayName = "Ollama", PublicPath = "/" },
            new GatewayEndpointEntity { Key = "azure", DisplayName = "Azure", PublicPath = "/azure" }
        })
        {
            if (!await dbContext.GatewayEndpoints.AnyAsync(item => item.Key == endpoint.Key, cancellationToken))
                dbContext.GatewayEndpoints.Add(endpoint);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
