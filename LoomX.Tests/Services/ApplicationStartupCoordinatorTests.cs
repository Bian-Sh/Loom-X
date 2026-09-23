using LoomX.Configuration;
using LoomX.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class ApplicationStartupCoordinatorTests
{
    [Fact]
    public async Task RestoreAsyncStartsGatewayOnlyWhenPersistedIntentIsRunning()
    {
        var windows = new RecordingWindowsStartupService();
        var endpoints = new List<string>();
        var coordinator = new ApplicationStartupCoordinator(
            windows,
            (endpoint, _) => { endpoints.Add(endpoint); return Task.CompletedTask; },
            NullLogger<ApplicationStartupCoordinator>.Instance);

        await coordinator.RestoreAsync(CreateSettings(startWithWindows: true, gatewayRunning: false), "http://127.0.0.1:19001");
        await coordinator.RestoreAsync(CreateSettings(startWithWindows: false, gatewayRunning: true), "http://127.0.0.1:19002");

        Assert.Equal([true, false], windows.AppliedValues);
        Assert.Equal(["http://127.0.0.1:19002"], endpoints);
    }

    [Fact]
    public async Task RestoreAsyncKeepsRunningIntentWhenGatewayStartFails()
    {
        var settings = CreateSettings(startWithWindows: false, gatewayRunning: true);
        var coordinator = new ApplicationStartupCoordinator(
            new RecordingWindowsStartupService(),
            (_, _) => throw new InvalidOperationException("启动失败"),
            NullLogger<ApplicationStartupCoordinator>.Instance);

        await coordinator.RestoreAsync(settings, "http://127.0.0.1:19003");

        Assert.True(settings.GatewayRunning);
    }

    private static AppSettingsResponse CreateSettings(bool startWithWindows, bool gatewayRunning) => new(
        1, "zh-CN", "system", "direct", "http://127.0.0.1", 7890, null, false,
        true, "stable", false, 30, false, true, 86, 24, "acrylic", true,
        startWithWindows, gatewayRunning);

    private sealed class RecordingWindowsStartupService : IWindowsStartupService
    {
        public List<bool> AppliedValues { get; } = [];
        public void Apply(bool enabled) => AppliedValues.Add(enabled);
    }
}
