using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaHub.Activity;
using Xunit;

namespace OllamaHub.Tests;

public sealed class ActivityMiddlewareTests
{
    [Fact]
    public void ActivityStore_NotifiesObserversWhenActivityIsEnqueued()
    {
        using var store = new ActivityStore(NullLogger<ActivityStore>.Instance);
        ActivityEventInput? received = null;
        store.ActivityEnqueued += (_, input) => received = input;
        var input = new ActivityEventInput(DateTimeOffset.UtcNow, "req_observer", "POST", "/v1/chat/completions", "OpenAI", "OpenAI 直通", null, "model", 200, 4, 0, false, null);

        Assert.True(store.TryEnqueue(input));
        Assert.Same(input, received);
    }

    [Fact]
    public async Task RecordsTrackedRequestWithGeneratedRequestId()
    {
        var store = new RecordingActivityStore();
        var middleware = new ActivityMiddleware(context =>
        {
            var activity = (ActivityRequestContext)context.Items[ActivityContextKeys.Request]!;
            activity.ProviderId = "智脑";
            activity.ModelId = "claude-sonnet";
            activity.Route = "OpenAI → Anthropic";
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        }, store, NullLogger<ActivityMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/chat/completions";

        await middleware.InvokeAsync(context);

        var record = Assert.Single(store.Records);
        Assert.StartsWith("req_", record.RequestId);
        Assert.Equal("智脑", record.ProviderId);
        Assert.Equal("claude-sonnet", record.ModelId);
        Assert.Equal("OpenAI → Anthropic", record.Route);
        Assert.Equal(record.RequestId, context.Response.Headers["X-Request-ID"].ToString());
    }

    [Fact]
    public async Task IgnoresUntrackedRequest()
    {
        var store = new RecordingActivityStore();
        var called = false;
        var middleware = new ActivityMiddleware(_ => { called = true; return Task.CompletedTask; }, store, NullLogger<ActivityMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/tags";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Empty(store.Records);
    }

    private sealed class RecordingActivityStore : IActivityStore
    {
        public event EventHandler<ActivityEventInput>? ActivityEnqueued;
        public List<ActivityEventInput> Records { get; } = [];
        public bool TryEnqueue(ActivityEventInput input) { Records.Add(input); ActivityEnqueued?.Invoke(this, input); return true; }
        public Task<IReadOnlyList<ActivityEventRecord>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityEventRecord>>([]);
        public Task<ActivityEventRecord?> GetAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<ActivityEventRecord?>(null);
    }
}
