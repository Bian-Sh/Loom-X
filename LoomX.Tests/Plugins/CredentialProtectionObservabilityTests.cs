using LoomX.CredentialProtection;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class CredentialProtectionObservabilityTests
{
    [Fact]
    public void SnapshotStartsAtZeroAndRecordsSafeCounters()
    {
        var observability = new CredentialProtectionObservability();
        var notifications = 0;
        object? eventArgs = null;
        observability.Changed += (_, args) =>
        {
            notifications++;
            eventArgs = args;
        };

        Assert.Equal(new CredentialProtectionSnapshot(0, 0, 0, 0, 0), observability.Snapshot());

        observability.RecordRequest(3);
        observability.RecordRequest(0);
        observability.RecordRestoredResponse();
        observability.RecordError();

        Assert.Equal(new CredentialProtectionSnapshot(2, 1, 3, 1, 1), observability.Snapshot());
        Assert.Equal(4, notifications);
        Assert.Same(EventArgs.Empty, eventArgs);
    }

    [Fact]
    public void ConcurrentUpdatesAreNotLost()
    {
        var observability = new CredentialProtectionObservability();

        Parallel.For(0, 1000, index =>
        {
            observability.RecordRequest(index % 2 == 0 ? 2 : 0);
            observability.RecordRestoredResponse();
            observability.RecordError();
        });

        Assert.Equal(new CredentialProtectionSnapshot(1000, 500, 1000, 1000, 1000), observability.Snapshot());
    }
}