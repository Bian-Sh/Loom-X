using Xunit;
using LoomX.Assistant.Browser;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

public sealed class BrowserBridgeLeaseManagerTests
{
    [Fact]
    public async Task 多会话共享Bridge_最后一个租约释放时才停止()
    {
        var lifecycle = new FakeBrowserBridgeLifecycle();
        var manager = new BrowserBridgeLeaseManager(lifecycle, NullLogger<BrowserBridgeLeaseManager>.Instance);

        await manager.AcquireAsync("session-a");
        await manager.AcquireAsync("session-a");
        await manager.AcquireAsync("session-b");

        Assert.Equal(1, lifecycle.StartCount);
        Assert.Equal(["session-a", "session-b"], manager.ActiveSessionIds.Order(StringComparer.Ordinal));

        await manager.ReleaseAsync("session-a");
        Assert.Equal(0, lifecycle.StopCount);
        Assert.True(lifecycle.IsListening);

        await manager.ReleaseAsync("session-b");
        Assert.Equal(1, lifecycle.StopCount);
        Assert.False(lifecycle.IsListening);
        Assert.Empty(manager.ActiveSessionIds);
    }

    [Fact]
    public async Task 首次启动失败_不会遗留Session租约()
    {
        var lifecycle = new FakeBrowserBridgeLifecycle
        {
            StartException = new BrowserBridgeException("bridge_port_unavailable", "端口不可用。"),
        };
        var manager = new BrowserBridgeLeaseManager(lifecycle, NullLogger<BrowserBridgeLeaseManager>.Instance);

        var exception = await Assert.ThrowsAsync<BrowserBridgeException>(() => manager.AcquireAsync("session-a"));

        Assert.Equal("bridge_port_unavailable", exception.Code);
        Assert.Empty(manager.ActiveSessionIds);
        Assert.False(lifecycle.IsListening);
    }

    [Fact]
    public async Task 释放未知Session保持幂等()
    {
        var lifecycle = new FakeBrowserBridgeLifecycle();
        var manager = new BrowserBridgeLeaseManager(lifecycle, NullLogger<BrowserBridgeLeaseManager>.Instance);

        await manager.ReleaseAsync("missing");

        Assert.Equal(0, lifecycle.StopCount);
        Assert.Empty(manager.ActiveSessionIds);
    }

    private sealed class FakeBrowserBridgeLifecycle : IBrowserBridgeLifecycle
    {
        public bool IsListening { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public Exception? StartException { get; init; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (StartException is not null) throw StartException;
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            IsListening = false;
            return Task.CompletedTask;
        }
    }
}
