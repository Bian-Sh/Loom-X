using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LoomX.Activity;
using Xunit;

namespace LoomX.Tests;

public sealed class ActivityQueryOrderingTests
{
    [Fact]
    public async Task SQLiteQueryUsesIdOrderingAndRestoresCreatedAtOrderOnClient()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ActivityDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Events.AddRange(
            CreateEntity(1, DateTimeOffset.Parse("2026-08-30T10:00:00+08:00"), "older"),
            CreateEntity(2, DateTimeOffset.Parse("2026-08-30T09:00:00+08:00"), "newer-id"));
        await db.SaveChangesAsync();

        var records = await db.Events
            .AsNoTracking()
            .OrderByDescending(item => item.Id)
            .Take(2)
            .Select(item => new ActivityEventRecord(item.Id, item.CreatedAt, item.RequestId, item.Method, item.IncomingPath, item.Protocol, item.Route, item.ProviderId, item.ModelId, item.StatusCode, item.ElapsedMs, item.ResponseBytes, item.IsStreaming, item.ErrorType))
            .ToListAsync();
        var ordered = records
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToArray();

        Assert.Equal(["older", "newer-id"], ordered.Select(item => item.RequestId));
    }

    private static ActivityEventEntity CreateEntity(long id, DateTimeOffset createdAt, string requestId) => new()
    {
        Id = id,
        CreatedAt = createdAt,
        RequestId = requestId,
        Method = "POST",
        IncomingPath = "/v1/chat/completions",
        Protocol = "OpenAI",
        Route = "OpenAI 直通",
        StatusCode = 200
    };
}
