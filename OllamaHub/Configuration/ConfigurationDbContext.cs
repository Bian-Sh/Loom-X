using Microsoft.EntityFrameworkCore;

namespace OllamaHub.Configuration;

public sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : DbContext(options)
{
    public DbSet<GatewayConfigurationEntity> GatewayConfigurations => Set<GatewayConfigurationEntity>();
    public DbSet<ProviderEntity> Providers => Set<ProviderEntity>();
    public DbSet<ModelEntity> Models => Set<ModelEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GatewayConfigurationEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ListenUrl).HasMaxLength(2048).IsRequired();
        });
        modelBuilder.Entity<ProviderEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.BusinessId).IsUnique();
            entity.Property(item => item.BusinessId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(item => item.BaseUrl).HasMaxLength(2048).IsRequired();
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

public sealed class ProviderEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BusinessId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiMode { get; set; } = "openai";
    public bool Enabled { get; set; } = true;
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
        if (!await dbContext.GatewayConfigurations.AnyAsync(cancellationToken))
        {
            dbContext.GatewayConfigurations.Add(new GatewayConfigurationEntity());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
