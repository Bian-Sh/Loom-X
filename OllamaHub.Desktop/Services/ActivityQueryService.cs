using Microsoft.EntityFrameworkCore;
using OllamaHub;
using OllamaHub.Activity;

namespace OllamaHub.Desktop.Services;

public sealed class ActivityQueryService
{
    public async Task<IReadOnlyList<ActivityEventRecord>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        await ActivityDatabase.InitializeAsync(db, cancellationToken);
        var events = db.Events.AsNoTracking().OrderByDescending(item => item.Id).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            events = query.Status switch
            {
                "ok" => events.Where(item => item.StatusCode >= 200 && item.StatusCode < 300),
                "fail" => events.Where(item => item.StatusCode >= 500),
                "warn" => events.Where(item => item.StatusCode >= 400 && item.StatusCode < 500),
                _ => events
            };
        }
        if (!string.IsNullOrWhiteSpace(query.Protocol)) events = events.Where(item => item.Protocol == query.Protocol);
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var search = query.SearchText.Trim();
            events = events.Where(item => item.RequestId.Contains(search) || (item.ProviderId ?? "").Contains(search) || (item.ModelId ?? "").Contains(search) || item.Route.Contains(search));
        }
        var records = await events
            .Take(Math.Clamp(query.Limit, 1, 5000))
            .Select(item => new ActivityEventRecord(item.Id, item.CreatedAt, item.RequestId, item.Method, item.IncomingPath, item.Protocol, item.Route, item.ProviderId, item.ModelId, item.StatusCode, item.ElapsedMs, item.ResponseBytes, item.IsStreaming, item.ErrorType))
            .ToListAsync(cancellationToken);
        return records
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToArray();
    }

    private static ActivityDbContext CreateContext()
    {
        AppDataPaths.EnsureCreated();
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={AppDataPaths.ActivityDatabasePath}").Options;
        return new ActivityDbContext(options);
    }
}
