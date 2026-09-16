using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant.UserDecisions;

public interface IUserDecisionBroker
{
    event EventHandler<PendingUserDecision>? PendingRequested;

    Task<UserDecisionResult> RequestAsync(
        string ownerId,
        UserDecisionRequest request,
        CancellationToken cancellationToken);

    bool Submit(string requestId, IReadOnlyDictionary<string, object?> values);

    bool Cancel(string requestId, string reason);

    int CancelOwner(string ownerId, string reason);
}

public sealed class UserDecisionBroker(ILogger<UserDecisionBroker> logger) : IUserDecisionBroker
{
    private readonly ConcurrentDictionary<string, PendingEntry> pendingEntries = new(StringComparer.Ordinal);

    public event EventHandler<PendingUserDecision>? PendingRequested;

    public Task<UserDecisionResult> RequestAsync(
        string ownerId,
        UserDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("用户决策 ownerId 不能为空。", nameof(ownerId));
        }

        ArgumentNullException.ThrowIfNull(request);
        var validationErrors = UserDecisionValidator.ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            throw new UserDecisionValidationException(validationErrors);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<UserDecisionResult>(cancellationToken);
        }

        var requestId = CreateRequestId();
        var completion = new TaskCompletionSource<UserDecisionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingEntry(ownerId, request, completion);
        while (!pendingEntries.TryAdd(requestId, entry))
        {
            requestId = CreateRequestId();
        }

        var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var cancellationState = (CancellationState)state!;
                cancellationState.Broker.CancelByToken(
                    cancellationState.RequestId,
                    cancellationState.CancellationToken);
            },
            new CancellationState(this, requestId, cancellationToken));
        entry.SetCancellationRegistration(cancellationRegistration);

        logger.LogInformation(
            "用户决策请求已挂起 {RequestId} {OwnerId} {FieldCount}",
            requestId,
            ownerId,
            request.Fields.Count);

        try
        {
            PendingRequested?.Invoke(this, new PendingUserDecision(requestId, ownerId, request));
        }
        catch (Exception exception)
        {
            if (pendingEntries.TryRemove(requestId, out var removed))
            {
                var publishException = new InvalidOperationException("无法发布用户决策请求。");
                removed.Completion.TrySetException(publishException);
                removed.DisposeCancellationRegistration();
                var safeLogException = new InvalidOperationException("用户决策事件订阅者执行失败。");
                logger.LogError(
                    safeLogException,
                    "用户决策请求事件发布失败 {RequestId} {OwnerId} {ErrorType}",
                    requestId,
                    ownerId,
                    exception.GetType().Name);
            }
        }

        return completion.Task;
    }

    public bool Submit(string requestId, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(values);

        if (!pendingEntries.TryGetValue(requestId, out var entry))
        {
            return false;
        }

        var validationErrors = UserDecisionValidator.ValidateSubmission(entry.Request, values);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "用户决策提交校验失败 {RequestId} {OwnerId} {ErrorCount}",
                requestId,
                entry.OwnerId,
                validationErrors.Count);
            return false;
        }

        if (!pendingEntries.TryRemove(requestId, out var removed))
        {
            return false;
        }

        removed.Completion.TrySetResult(UserDecisionResult.Submit(values));
        removed.DisposeCancellationRegistration();
        logger.LogInformation(
            "用户决策请求已提交 {RequestId} {OwnerId} {FieldCount}",
            requestId,
            removed.OwnerId,
            values.Count);
        return true;
    }

    public bool Cancel(string requestId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!pendingEntries.TryRemove(requestId, out var entry))
        {
            return false;
        }

        entry.Completion.TrySetResult(UserDecisionResult.Cancel(reason));
        entry.DisposeCancellationRegistration();
        logger.LogInformation(
            "用户决策请求已取消 {RequestId} {OwnerId}",
            requestId,
            entry.OwnerId);
        return true;
    }

    public int CancelOwner(string ownerId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var cancelledCount = 0;
        foreach (var pair in pendingEntries)
        {
            if (!string.Equals(pair.Value.OwnerId, ownerId, StringComparison.Ordinal)
                || !pendingEntries.TryRemove(pair.Key, out var entry))
            {
                continue;
            }

            entry.Completion.TrySetResult(UserDecisionResult.Cancel(reason));
            entry.DisposeCancellationRegistration();
            cancelledCount++;
        }

        if (cancelledCount > 0)
        {
            logger.LogInformation(
                "用户决策 Owner 请求已取消 {OwnerId} {RequestCount}",
                ownerId,
                cancelledCount);
        }

        return cancelledCount;
    }

    private static string CreateRequestId() => Guid.NewGuid().ToString("N");

    private void CancelByToken(string requestId, CancellationToken cancellationToken)
    {
        if (!pendingEntries.TryRemove(requestId, out var entry))
        {
            return;
        }

        entry.Completion.TrySetCanceled(cancellationToken);
        entry.DisposeCancellationRegistration();
        logger.LogInformation(
            "用户决策请求随调用取消 {RequestId} {OwnerId}",
            requestId,
            entry.OwnerId);
    }

    private sealed class PendingEntry(
        string ownerId,
        UserDecisionRequest request,
        TaskCompletionSource<UserDecisionResult> completion)
    {
        private readonly object registrationGate = new();
        private CancellationTokenRegistration cancellationRegistration;
        private bool completed;

        public string OwnerId { get; } = ownerId;

        public UserDecisionRequest Request { get; } = request;

        public TaskCompletionSource<UserDecisionResult> Completion { get; } = completion;

        public void SetCancellationRegistration(CancellationTokenRegistration registration)
        {
            var disposeImmediately = false;
            lock (registrationGate)
            {
                if (completed)
                {
                    disposeImmediately = true;
                }
                else
                {
                    cancellationRegistration = registration;
                }
            }

            if (disposeImmediately)
            {
                registration.Dispose();
            }
        }

        public void DisposeCancellationRegistration()
        {
            CancellationTokenRegistration registration;
            lock (registrationGate)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                registration = cancellationRegistration;
                cancellationRegistration = default;
            }

            registration.Dispose();
        }
    }

    private sealed record CancellationState(
        UserDecisionBroker Broker,
        string RequestId,
        CancellationToken CancellationToken);
}
