using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OllamaHub.Activity;

public interface IActivityStore
{
    event EventHandler<ActivityEventInput>? ActivityEnqueued;
    bool TryEnqueue(ActivityEventInput input);
    Task<IReadOnlyList<ActivityEventRecord>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken = default);
    Task<ActivityEventRecord?> GetAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class ActivityStore(ILogger<ActivityStore> logger) : BackgroundService, IActivityStore
{
    private const int MaxRows = 50_000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private readonly Channel<ActivityEventInput> queue = Channel.CreateBounded<ActivityEventInput>(new BoundedChannelOptions(2048)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public event EventHandler<ActivityEventInput>? ActivityEnqueued;

    public bool TryEnqueue(ActivityEventInput input)
    {
        if (!queue.Writer.TryWrite(input)) return false;
        ActivityEnqueued?.Invoke(this, input);
        return true;
    }

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
            events = events.Where(item => (item.RequestId.Contains(search) || (item.ProviderId ?? "").Contains(search) || (item.ModelId ?? "").Contains(search) || item.Route.Contains(search)));
        }
        var limit = Math.Clamp(query.Limit, 1, 5000);
        var records = await events.Take(limit).Select(item => ToRecord(item)).ToListAsync(cancellationToken);
        return records
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToArray();
    }

    public async Task<ActivityEventRecord?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        await ActivityDatabase.InitializeAsync(db, cancellationToken);
        var item = await db.Events.AsNoTracking().SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        return item is null ? null : ToRecord(item);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var db = CreateContext()) await ActivityDatabase.InitializeAsync(db, stoppingToken);
        var pending = new List<ActivityEventInput>(64);
        try
        {
            await foreach (var input in queue.Reader.ReadAllAsync(stoppingToken))
            {
                pending.Add(input);
                if (pending.Count < 32) continue;
                await PersistAsync(pending, stoppingToken);
                pending.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (pending.Count > 0) await PersistAsync(pending, CancellationToken.None);
        }
    }

    private async Task PersistAsync(IReadOnlyCollection<ActivityEventInput> inputs, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = CreateContext();
            foreach (var input in inputs) db.Events.Add(new ActivityEventEntity
            {
                CreatedAt = input.CreatedAt,
                RequestId = input.RequestId,
                Method = input.Method,
                IncomingPath = input.IncomingPath,
                Protocol = input.Protocol,
                Route = input.Route,
                ProviderId = input.ProviderId,
                ModelId = input.ModelId,
                StatusCode = input.StatusCode,
                ElapsedMs = input.ElapsedMs,
                ResponseBytes = input.ResponseBytes,
                IsStreaming = input.IsStreaming,
                ErrorType = input.ErrorType
            });
            await db.SaveChangesAsync(cancellationToken);
            var cutoff = DateTimeOffset.UtcNow - MaxAge;
            await db.Events.Where(item => item.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
            var overflow = await db.Events.OrderByDescending(item => item.Id).Skip(MaxRows).Select(item => item.Id).ToListAsync(cancellationToken);
            if (overflow.Count > 0) await db.Events.Where(item => overflow.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "活动记录写入失败 {Count}", inputs.Count);
        }
    }

    private static ActivityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={AppDataPaths.ActivityDatabasePath}").Options;
        return new ActivityDbContext(options);
    }

    private static ActivityEventRecord ToRecord(ActivityEventEntity item) => new(item.Id, item.CreatedAt, item.RequestId, item.Method, item.IncomingPath, item.Protocol, item.Route, item.ProviderId, item.ModelId, item.StatusCode, item.ElapsedMs, item.ResponseBytes, item.IsStreaming, item.ErrorType);
}
