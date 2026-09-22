using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LoomX.Activity;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class ActivityQueryServiceTests
{
    [Fact]
    public async Task QueryAsyncReturnsRecentRowsWithCreatedAtOrdering()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "LoomX.Activity.db");
        Directory.CreateDirectory(directory);
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite(connectionString).Options;
            await using (var db = new ActivityDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Events.AddRange(
                    CreateEntity(1, "2026-09-02T08:59:00+08:00", "oldest-created-first"),
                    CreateEntity(2, "2026-09-02T09:00:00+08:00", "older-created-second"),
                    CreateEntity(3, "2026-09-02T09:02:00+08:00", "newer-created-third"));
                await db.SaveChangesAsync();
            }

            var records = await new ActivityQueryService(databasePath).QueryAsync(new ActivityQuery(Limit: 2));

            Assert.Equal(["newer-created-third", "older-created-second"], records.Select(item => item.RequestId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QueryAsyncLimitsAfterCreatedAtOrdering()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "LoomX.Activity.db");
        Directory.CreateDirectory(directory);
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite(connectionString).Options;
            await using (var db = new ActivityDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Events.AddRange(
                    CreateEntity(1, "2026-09-02T09:03:00+08:00", "newest-created-first"),
                    CreateEntity(2, "2026-09-02T09:00:00+08:00", "oldest-created-second"),
                    CreateEntity(3, "2026-09-02T09:02:00+08:00", "newer-created-third"));
                await db.SaveChangesAsync();
            }

            var records = await new ActivityQueryService(databasePath).QueryAsync(new ActivityQuery(Limit: 2));

            Assert.Equal(["newest-created-first", "newer-created-third"], records.Select(item => item.RequestId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QueryPageAsyncUsesCreatedAtAndIdCursor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "LoomX.Activity.db");
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using (var db = new ActivityDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var timestamp = DateTimeOffset.Parse("2026-09-02T09:00:00+08:00");
                db.Events.AddRange(
                    CreateEntity(1, timestamp.ToString("O"), "same-time-low-id"),
                    CreateEntity(2, timestamp.ToString("O"), "same-time-high-id"),
                    CreateEntity(3, "2026-09-02T08:59:00+08:00", "older"));
                await db.SaveChangesAsync();
            }

            var service = new ActivityQueryService(databasePath);
            var first = await service.QueryPageAsync(new ActivityQuery(Limit: 2));
            var second = await service.QueryPageAsync(new ActivityQuery(Limit: 2), first.NextCursor);

            Assert.Equal(["same-time-high-id", "same-time-low-id"], first.Items.Select(item => item.RequestId));
            Assert.Equal(["older"], second.Items.Select(item => item.RequestId));
            Assert.False(second.HasMore);
        }
        finally
        {
            for (var attempt = 0; attempt < 20 && Directory.Exists(directory); attempt++)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { if (attempt < 19) Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { if (attempt < 19) Thread.Sleep(50); }
            }
        }
    }

    private static ActivityEventEntity CreateEntity(long id, string createdAt, string requestId) => new()
    {
        Id = id,
        CreatedAt = DateTimeOffset.Parse(createdAt),
        RequestId = requestId,
        Method = "POST",
        IncomingPath = "/v1/chat/completions",
        Protocol = "OpenAI",
        Route = "OpenAI 直通",
        ProviderId = "provider-a",
        ModelId = "model-a",
        StatusCode = 200,
        ElapsedMs = 100
    };
}
