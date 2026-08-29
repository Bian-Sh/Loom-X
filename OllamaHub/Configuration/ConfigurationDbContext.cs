using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace OllamaHub.Configuration;

public sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : DbContext(options)
{
    public DbSet<GatewayConfigurationEntity> GatewayConfigurations => Set<GatewayConfigurationEntity>();
    public DbSet<AppSettingsEntity> AppSettings => Set<AppSettingsEntity>();
    public DbSet<ProviderEntity> Providers => Set<ProviderEntity>();
    public DbSet<ModelEntity> Models => Set<ModelEntity>();

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
    public bool OpenControlCenterOnStartup { get; set; } = true;
    public string ProxyMode { get; set; } = "direct";
    public string ProxyHost { get; set; } = "http://127.0.0.1";
    public int ProxyPort { get; set; } = 7890;
    public string? ProxyUsername { get; set; }
    public string? ProtectedProxyPassword { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public string UpdateChannel { get; set; } = "stable";
    public bool DiagnosticsEnabled { get; set; }
    public int LogRetentionDays { get; set; } = 30;
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
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public static class ConfigurationDatabase
{
    public static async Task InitializeAsync(ConfigurationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
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

    private static async Task EnsureSchemaAsync(ConfigurationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
                Language TEXT NOT NULL,
                Theme TEXT NOT NULL,
                OpenControlCenterOnStartup INTEGER NOT NULL,
                ProxyMode TEXT NOT NULL,
                ProxyHost TEXT NOT NULL,
                ProxyPort INTEGER NOT NULL,
                ProxyUsername TEXT NULL,
                ProtectedProxyPassword TEXT NULL,
                AutoCheckUpdates INTEGER NOT NULL,
                UpdateChannel TEXT NOT NULL,
                DiagnosticsEnabled INTEGER NOT NULL,
                LogRetentionDays INTEGER NOT NULL
            )
            """, cancellationToken);

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
    }
}
