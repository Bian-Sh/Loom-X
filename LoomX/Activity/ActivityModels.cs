using Microsoft.EntityFrameworkCore;

namespace LoomX.Activity;

public sealed class ActivityDbContext(DbContextOptions<ActivityDbContext> options) : DbContext(options)
{
    public DbSet<ActivityEventEntity> Events => Set<ActivityEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityEventEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RequestId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Method).HasMaxLength(16).IsRequired();
            entity.Property(item => item.IncomingPath).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Protocol).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Route).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ProviderId).HasMaxLength(128);
            entity.Property(item => item.ModelId).HasMaxLength(256);
            entity.Property(item => item.ErrorType).HasMaxLength(128);
            entity.HasIndex(item => item.CreatedAt);
            entity.HasIndex(item => item.StatusCode);
            entity.HasIndex(item => item.ProviderId);
            entity.HasIndex(item => item.ModelId);
        });
    }
}

public sealed class ActivityEventEntity
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string IncomingPath { get; set; } = string.Empty;
    public string Protocol { get; set; } = "OpenAI";
    public string Route { get; set; } = "OpenAI 直通";
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public int StatusCode { get; set; }
    public long ElapsedMs { get; set; }
    public long ResponseBytes { get; set; }
    public bool IsStreaming { get; set; }
    public string? ErrorType { get; set; }
}

public sealed record ActivityEventInput(
    DateTimeOffset CreatedAt,
    string RequestId,
    string Method,
    string IncomingPath,
    string Protocol,
    string Route,
    string? ProviderId,
    string? ModelId,
    int StatusCode,
    long ElapsedMs,
    long ResponseBytes,
    bool IsStreaming,
    string? ErrorType);

public sealed record ActivityEventRecord(
    long Id,
    DateTimeOffset CreatedAt,
    string RequestId,
    string Method,
    string IncomingPath,
    string Protocol,
    string Route,
    string? ProviderId,
    string? ModelId,
    int StatusCode,
    long ElapsedMs,
    long ResponseBytes,
    bool IsStreaming,
    string? ErrorType);

public sealed record ActivityQuery(
    string? SearchText = null,
    string? Status = null,
    string? Protocol = null,
    int Limit = 500);

public sealed record ActivityCursor(DateTimeOffset CreatedAt, long Id);

public sealed record ActivityPage(
    IReadOnlyList<ActivityEventRecord> Items,
    ActivityCursor? NextCursor,
    bool HasMore);

public static class ActivityDatabase
{
    public static async Task InitializeAsync(ActivityDbContext dbContext, CancellationToken cancellationToken = default) =>
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
}
