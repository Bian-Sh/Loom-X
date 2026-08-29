using System.Collections.Concurrent;

namespace OllamaHub.Activity;

public enum TelemetryEventKind
{
    RequestStarted,
    EdgeAttemptStarted,
    EdgeAttemptCompleted,
    EdgeAttemptFailed,
    EdgeAttemptCancelled,
    RequestCompleted
}

public sealed record RequestTelemetryEvent(
    TelemetryEventKind Kind,
    DateTimeOffset Timestamp,
    string RequestId,
    string EndpointKey,
    string Protocol,
    string? ModelAlias,
    string? ProviderId,
    string? ModelId,
    int AttemptIndex = 0,
    int? StatusCode = null,
    long ElapsedMs = 0,
    bool IsStreaming = false,
    bool WillRetry = false,
    string? ErrorType = null);

public sealed class RequestTelemetryHub
{
    private readonly ConcurrentDictionary<string, byte> activeRequests = new(StringComparer.Ordinal);

    public event EventHandler<RequestTelemetryEvent>? Published;
    public IReadOnlyCollection<string> ActiveRequests => activeRequests.Keys.ToArray();

    public void StartRequest(ActivityRequestContext context)
    {
        if (!activeRequests.TryAdd(context.RequestId, 0)) return;
        Publish(new RequestTelemetryEvent(TelemetryEventKind.RequestStarted, DateTimeOffset.UtcNow, context.RequestId, context.EndpointKey, context.Protocol, context.ModelAlias, context.ProviderId, context.ModelId, IsStreaming: context.IsStreaming));
    }

    public void EdgeAttemptStarted(ActivityRequestContext context, string providerId, string modelId, int attemptIndex)
    {
        Publish(new RequestTelemetryEvent(TelemetryEventKind.EdgeAttemptStarted, DateTimeOffset.UtcNow, context.RequestId, context.EndpointKey, context.Protocol, context.ModelAlias, providerId, modelId, attemptIndex, IsStreaming: context.IsStreaming));
    }

    public void EdgeAttemptCompleted(ActivityRequestContext context, string providerId, string modelId, int statusCode, long elapsedMs, int attemptIndex = 0)
    {
        Publish(new RequestTelemetryEvent(TelemetryEventKind.EdgeAttemptCompleted, DateTimeOffset.UtcNow, context.RequestId, context.EndpointKey, context.Protocol, context.ModelAlias, providerId, modelId, attemptIndex, statusCode, elapsedMs, context.IsStreaming));
    }

    public void EdgeAttemptFailed(ActivityRequestContext context, string providerId, string modelId, int? statusCode, long elapsedMs, bool willRetry, int attemptIndex = 0, string? errorType = null)
    {
        Publish(new RequestTelemetryEvent(TelemetryEventKind.EdgeAttemptFailed, DateTimeOffset.UtcNow, context.RequestId, context.EndpointKey, context.Protocol, context.ModelAlias, providerId, modelId, attemptIndex, statusCode, elapsedMs, context.IsStreaming, willRetry, errorType));
    }

    public void EdgeAttemptCancelled(ActivityRequestContext context, string providerId, string modelId, long elapsedMs, int attemptIndex = 0)
    {
        Publish(new RequestTelemetryEvent(TelemetryEventKind.EdgeAttemptCancelled, DateTimeOffset.UtcNow, context.RequestId, context.EndpointKey, context.Protocol, context.ModelAlias, providerId, modelId, attemptIndex, ElapsedMs: elapsedMs, IsStreaming: context.IsStreaming));
    }

    public void CompleteRequest(ActivityRequestContext context, int statusCode, long elapsedMs, string? errorType)
    {
        if (!activeRequests.TryRemove(context.RequestId, out _)) return;
        Publish(new RequestTelemetryEvent(TelemetryEventKind.RequestCompleted, DateTimeOffset.UtcNow, context.RequestId, context.EndpointKey, context.Protocol, context.ModelAlias, context.ProviderId, context.ModelId, StatusCode: statusCode, ElapsedMs: elapsedMs, IsStreaming: context.IsStreaming, ErrorType: errorType));
    }

    private void Publish(RequestTelemetryEvent telemetryEvent) => Published?.Invoke(this, telemetryEvent);
}
