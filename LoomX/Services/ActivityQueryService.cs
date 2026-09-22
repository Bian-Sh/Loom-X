using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LoomX;
using LoomX.Activity;

namespace LoomX.Services;

public sealed class ActivityQueryService
{
    private readonly string databasePath;

    public ActivityQueryService() : this(AppDataPaths.ActivityDatabasePath) { }

    internal ActivityQueryService(string databasePath) => this.databasePath = databasePath;

    public async Task<IReadOnlyList<ActivityEventRecord>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        var page = await QueryPageAsync(query, null, cancellationToken);
        return page.Items;
    }

    public async Task<ActivityPage> QueryPageAsync(ActivityQuery query, ActivityCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        await ActivityDatabase.InitializeAsync(db, cancellationToken);
        var events = db.Events.AsNoTracking().AsQueryable();
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
        var limit = Math.Clamp(query.Limit, 1, 5000);
        // SQLite 对 DateTimeOffset 的 ORDER BY 翻译不完整，先由数据库完成筛选，再在客户端按复合游标稳定排序。
        var records = await events
            .Select(item => new ActivityEventRecord(item.Id, item.CreatedAt, item.RequestId, item.Method, item.IncomingPath, item.Protocol, item.Route, item.ProviderId, item.ModelId, item.StatusCode, item.ElapsedMs, item.ResponseBytes, item.IsStreaming, item.ErrorType))
            .ToListAsync(cancellationToken);
        var ordered = records
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id);
        if (cursor is not null)
            ordered = ordered.Where(item => item.CreatedAt < cursor.CreatedAt || (item.CreatedAt == cursor.CreatedAt && item.Id < cursor.Id)).OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id);
        records = ordered.Take(limit + 1).ToList();
        var hasMore = records.Count > limit;
        if (hasMore) records.RemoveAt(records.Count - 1);
        var items = records.ToArray();
        var nextCursor = items.Length == 0 ? cursor : new ActivityCursor(items[^1].CreatedAt, items[^1].Id);
        return new ActivityPage(items, nextCursor, hasMore);
    }

    private ActivityDbContext CreateContext()
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite(connectionString).Options;
        return new ActivityDbContext(options);
    }
}
