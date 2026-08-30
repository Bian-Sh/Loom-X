using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OllamaHub.Hosting;

namespace OllamaHub.Activity;

public sealed class ActivityRequestContext
{
    public string RequestId { get; init; } = string.Empty;
    public string EndpointKey { get; set; } = "openai";
    public string Protocol { get; set; } = "OpenAI";
    public string Route { get; set; } = "OpenAI 直通";
    public string? ModelAlias { get; set; }
    public int AttemptIndex { get; set; }
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public bool IsStreaming { get; set; }
}

public static class ActivityContextKeys
{
    public const string Request = "OllamaHub.Activity.Request";
}

public sealed class ActivityMiddleware(RequestDelegate next, IActivityStore activityStore, ILogger<ActivityMiddleware> logger, RequestTelemetryHub? telemetryHub = null)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (!IsTrackedRequest(httpContext))
        {
            await next(httpContext);
            return;
        }

        var requestId = $"req_{Guid.NewGuid():N}"[..16];
        var context = new ActivityRequestContext { RequestId = requestId, EndpointKey = GatewayEndpointRouting.ResolveKey(httpContext.Request.Path) ?? "unknown", Protocol = GatewayEndpointRouting.ResolveLabel(httpContext.Request.Path) };
        httpContext.Items[ActivityContextKeys.Request] = context;
        httpContext.Response.Headers["X-Request-ID"] = requestId;
        var startedAt = Stopwatch.GetTimestamp();
        telemetryHub?.StartRequest(context);
        try
        {
            await next(httpContext);
        }
        catch (Exception exception)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            telemetryHub?.CompleteRequest(context, 500, elapsedMs, exception.GetType().Name);
            await RecordAsync(httpContext, context, startedAt, 500, exception.GetType().Name);
            throw;
        }
        var responseElapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        telemetryHub?.CompleteRequest(context, httpContext.Response.StatusCode, responseElapsedMs, null);
        await RecordAsync(httpContext, context, startedAt, httpContext.Response.StatusCode, null);
    }

    private async Task RecordAsync(HttpContext httpContext, ActivityRequestContext context, long startedAt, int statusCode, string? errorType)
    {
        var input = new ActivityEventInput(DateTimeOffset.UtcNow, context.RequestId, httpContext.Request.Method, httpContext.Request.Path.Value ?? "", context.Protocol, context.Route, context.ProviderId, context.ModelId, statusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, httpContext.Response.ContentLength ?? 0, context.IsStreaming, errorType);
        if (!activityStore.TryEnqueue(input)) logger.LogWarning("活动记录队列已满，丢弃请求摘要 {RequestId}", context.RequestId);
        await Task.CompletedTask;
    }

    private static bool IsTrackedRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && (context.Request.Path.StartsWithSegments("/v1/chat/completions")
            || context.Request.Path.StartsWithSegments("/openai/v1/chat/completions")
            || context.Request.Path.StartsWithSegments("/openai/v1/responses")
            || context.Request.Path.StartsWithSegments("/azure/v1/responses")
            || context.Request.Path.StartsWithSegments("/api/chat"));

}
