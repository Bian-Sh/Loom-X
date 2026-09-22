using LoomX;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class InstanceActivationTests
{
    [Fact]
    public void RetriesActivationUntilTransportAccepts()
    {
        var attempts = 0;

        var activated = InstanceActivationClient.TryActivateExistingInstance(
            send: _ => ++attempts == 2,
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero);

        Assert.True(activated);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void AcceptsOnlyTheActivationCommand()
    {
        Assert.True(InstanceActivationClient.IsActivationCommand("activate"));
        Assert.True(InstanceActivationClient.IsActivationCommand(" activate "));
        Assert.False(InstanceActivationClient.IsActivationCommand("activate-window"));
        Assert.False(InstanceActivationClient.IsActivationCommand(null));
    }

    [Fact]
    public void StopsAfterConfiguredAttemptsWhenTransportFails()
    {
        var attempts = 0;

        var activated = InstanceActivationClient.TryActivateExistingInstance(
            send: _ =>
            {
                attempts++;
                throw new IOException("模拟首实例尚未提供激活管道");
            },
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero);

        Assert.False(activated);
        Assert.Equal(3, attempts);
    }
}
