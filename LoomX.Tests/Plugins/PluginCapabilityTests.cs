using System.Reflection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginCapabilityTests
{
    [Fact]
    public async Task 旧上下文不默认授予任何宿主能力()
    {
        var bridge = new PluginInitializationContext("example.safe", "目录").Bridge;
        var storage = await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.Storage.OpenSqliteAsync("settings.db"));
        Assert.Equal(PluginBridgeError.CapabilityNotDeclared, storage.Error);
        var log = await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.Log.WriteAsync(new(PluginLogLevel.Information, "测试事件")));
        Assert.Equal(PluginBridgeError.CapabilityNotDeclared, log.Error);
        var notification = await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.Notifications.ShowAsync(new(PluginNotificationLevel.Success, "完成")));
        Assert.Equal(PluginBridgeError.CapabilityNotDeclared, notification.Error);
    }

    [Fact]
    public async Task 声明与未声明能力按实例隔离()
    {
        var providers = new CountingProviders();
        var permitted = CreateScope(["logging/v1", "notifications/v1"], providers);
        var denied = CreateScope([], providers);
        await permitted.Log.WriteAsync(new(PluginLogLevel.Information, "安全完成事件"));
        await permitted.Notifications.ShowAsync(new(PluginNotificationLevel.Info, "安全通知"));
        Assert.Equal(1, providers.LogCalls);
        Assert.Equal(1, providers.NotificationCalls);
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await denied.Log.WriteAsync(new(PluginLogLevel.Information, "不应调用")));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await permitted.Storage.OpenSqliteAsync("settings.db"));
        Assert.Equal(1, providers.LogCalls);
    }

    [Fact]
    public void 不兼容能力在构造作用域时被拒绝()
    {
        var type = ScopeType();
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Activator.CreateInstance(type, ["example.safe", new[] { "storage.sqlite/v2" }, null, null, null]));
        var error = Assert.IsType<PluginBridgeException>(exception.InnerException);
        Assert.Equal(PluginBridgeError.CapabilityNotSupported, error.Error);
    }

    [Fact]
    public async Task 作用域释放后所有调用均拒绝且释放底层资源()
    {
        var providers = new CountingProviders();
        var scope = CreateScope(["logging/v1", "notifications/v1"], providers);
        await Assert.IsAssignableFrom<IAsyncDisposable>(scope).DisposeAsync();
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await scope.Log.WriteAsync(new(PluginLogLevel.Information, "不应调用")));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await scope.Notifications.ShowAsync(new(PluginNotificationLevel.Info, "不应调用")));
        Assert.Equal(1, providers.Disposals);
        Assert.Equal(0, providers.LogCalls);
    }

    private static Type ScopeType()
    {
        var type = typeof(ManifestParser).Assembly.GetType("LoomX.Plugins.Host.PluginBridgeScope");
        Assert.NotNull(type);
        return type;
    }

    private static IPluginBridge CreateScope(IReadOnlyList<string> capabilities, CountingProviders providers) =>
        Assert.IsAssignableFrom<IPluginBridge>(Activator.CreateInstance(ScopeType(),
            ["example.safe", capabilities, null, providers, providers]));

    private sealed class CountingProviders : IPluginLogBridge, IPluginNotificationBridge, IAsyncDisposable
    {
        public int LogCalls { get; private set; }
        public int NotificationCalls { get; private set; }
        public int Disposals { get; private set; }
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default) { LogCalls++; return ValueTask.CompletedTask; }
        public ValueTask ShowAsync(PluginNotification notification, CancellationToken cancellationToken = default) { NotificationCalls++; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
