using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant.UserDecisions;

public interface IUserDecisionBroker : IDisposable
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
    private readonly object lifecycleGate = new();
    private EventHandler<PendingUserDecision>? pendingRequested;
    private bool disposed;

    public event EventHandler<PendingUserDecision>? PendingRequested
    {
        add
        {
            lock (lifecycleGate)
            {
                ThrowIfDisposed();
                pendingRequested += value;
            }
        }

        remove
        {
            lock (lifecycleGate)
            {
                pendingRequested -= value;
            }
        }
    }

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

        EventHandler<PendingUserDecision>? handlers;
        string? requestId = null;
        TaskCompletionSource<UserDecisionResult>? completion = null;
        PendingEntry? entry = null;
        lock (lifecycleGate)
        {
            ThrowIfDisposed();
            handlers = pendingRequested;
            if (handlers is not null)
            {
                requestId = CreateRequestId();
                completion = new TaskCompletionSource<UserDecisionResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                entry = new PendingEntry(ownerId, request, completion);
                while (!pendingEntries.TryAdd(requestId, entry))
                {
                    requestId = CreateRequestId();
                }
            }
        }

        if (handlers is null)
        {
            logger.LogWarning("用户决策请求缺少处理器 {FieldCount}", request.Fields.Count);
            return Task.FromException<UserDecisionResult>(
                new InvalidOperationException("当前没有可用的用户决策处理器。"));
        }

        var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var cancellationState = (CancellationState)state!;
                cancellationState.Broker.CancelByToken(
                    cancellationState.RequestId,
                    cancellationState.CancellationToken);
            },
            new CancellationState(this, requestId!, cancellationToken));
        entry!.SetCancellationRegistration(cancellationRegistration);

        logger.LogInformation(
            "用户决策请求已挂起 {RequestId} {FieldCount}",
            requestId,
            request.Fields.Count);

        try
        {
            handlers.Invoke(this, new PendingUserDecision(requestId!, ownerId, request));
        }
        catch (Exception exception)
        {
            if (pendingEntries.TryRemove(requestId!, out var removed))
            {
                var publishException = new InvalidOperationException("无法发布用户决策请求。");
                CompleteEntry(removed, () => removed.Completion.TrySetException(publishException));
                var safeLogException = new InvalidOperationException("用户决策事件订阅者执行失败。");
                logger.LogError(
                    safeLogException,
                    "用户决策请求事件发布失败 {RequestId} {ErrorType}",
                    requestId,
                    exception.GetType().Name);
            }
        }

        return completion!.Task;
    }

    public bool Submit(string requestId, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(values);

        if (!pendingEntries.TryGetValue(requestId, out var entry))
        {
            return false;
        }

        IReadOnlyDictionary<string, object?> snapshot;
        try
        {
            snapshot = CreateSubmissionSnapshot(values);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "用户决策提交快照失败 {RequestId} {ErrorType}",
                requestId,
                exception.GetType().Name);
            return false;
        }

        var validationErrors = UserDecisionValidator.ValidateSubmission(entry.Request, snapshot);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "用户决策提交校验失败 {RequestId} {ErrorCount}",
                requestId,
                validationErrors.Count);
            return false;
        }

        var result = UserDecisionResult.Submit(snapshot);

        if (!pendingEntries.TryRemove(requestId, out var removed))
        {
            return false;
        }

        CompleteEntry(removed, () => removed.Completion.TrySetResult(result));
        logger.LogInformation(
            "用户决策请求已提交 {RequestId} {FieldCount}",
            requestId,
            result.Values.Count);
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

        CompleteEntry(entry, () => entry.Completion.TrySetResult(UserDecisionResult.Cancel(reason)));
        logger.LogInformation(
            "用户决策请求已取消 {RequestId}",
            requestId);
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

            CompleteEntry(entry, () => entry.Completion.TrySetResult(UserDecisionResult.Cancel(reason)));
            cancelledCount++;
        }

        if (cancelledCount > 0)
        {
            logger.LogInformation(
                "用户决策所有者请求已取消 {RequestCount}",
                cancelledCount);
        }

        return cancelledCount;
    }

    public void Dispose()
    {
        var removedEntries = new List<PendingEntry>();
        lock (lifecycleGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pendingRequested = null;
            foreach (var pair in pendingEntries)
            {
                if (pendingEntries.TryRemove(pair.Key, out var entry))
                {
                    removedEntries.Add(entry);
                }
            }
        }

        foreach (var entry in removedEntries)
        {
            CompleteEntry(entry, () => entry.Completion.TrySetCanceled());
        }

        logger.LogInformation("用户决策 Broker 已释放 {RequestCount}", removedEntries.Count);
        GC.SuppressFinalize(this);
    }

    private static IReadOnlyDictionary<string, object?> CreateSubmissionSnapshot(
        IReadOnlyDictionary<string, object?> values)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("用户决策结果字段 id 不能为空。", nameof(values));
            }

            snapshot.Add(pair.Key, CopySubmissionValue(pair.Value));
        }

        return new ReadOnlyDictionary<string, object?>(snapshot);
    }

    private static object? CopySubmissionValue(object? value) => value switch
    {
        string => value,
        IEnumerable<string> items => Array.AsReadOnly(items.ToArray()),
        _ => value,
    };

    private static void CompleteEntry(PendingEntry entry, Action completion)
    {
        try
        {
            completion();
        }
        finally
        {
            entry.DisposeCancellationRegistration();
        }
    }

    private static string CreateRequestId() => Guid.NewGuid().ToString("N");

    private void CancelByToken(string requestId, CancellationToken cancellationToken)
    {
        if (!pendingEntries.TryRemove(requestId, out var entry))
        {
            return;
        }

        CompleteEntry(entry, () => entry.Completion.TrySetCanceled(cancellationToken));
        logger.LogInformation(
            "用户决策请求随调用取消 {RequestId}",
            requestId);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
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
