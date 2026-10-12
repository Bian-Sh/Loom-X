using System.Reflection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginHostBridgeFactoryTests
{
    [Fact]
    public async Task 多插件组合根隔离数据库与通知且单实例释放不影响另一实例()
    {
        var root = Path.Combine(Path.GetTempPath(), "loomx-bridge-factory-" + Guid.NewGuid().ToString("N"));
        var service = new ToastService();
        var notifications = new List<ToastNotification>();
        service.Requested += (_, value) => notifications.Add(value);
        var type = typeof(ToastService).Assembly.GetType("LoomX.Services.PluginHostBridgeFactory");
        Assert.NotNull(type);
        var factory = Activator.CreateInstance(type, [root, service, NullLoggerFactory.Instance]);
        PluginBridgeScope Create(string id)
        {
            var manifest = new PluginManifest(id, "1", "unused.dll", "Unused", [], [])
            { HostCapabilities = ["storage.sqlite/v1", "logging/v1", "notifications/v1", "protection/v1"] };
            return Assert.IsType<PluginBridgeScope>(type.GetMethod("CreateScope")!.Invoke(factory, [manifest, Path.Combine(root, id)]));
        }
        await using var first = Create("example.first");
        if (OperatingSystem.IsWindows())
        {
            var ciphertext = await first.Protection.EncryptAsync("组合点保护哨兵");
            Assert.Equal("组合点保护哨兵", await first.Protection.DecryptAsync(ciphertext));
        }
        await using var second = Create("example.second");
        await using var db = await first.Storage.OpenSqliteAsync("store.db");
        await db.ExecuteAsync(new("CREATE TABLE Events(Id INTEGER)"));
        await db.ExecuteAsync(new("INSERT INTO Events VALUES(1)"));
        await using var other = await second.Storage.OpenSqliteAsync("store.db");
        var row = await other.QuerySingleAsync(new("SELECT COUNT(*) FROM sqlite_master WHERE name='Events'"));
        Assert.Equal(0, row![0].Integer);
        await first.Notifications.ShowAsync(new(PluginNotificationLevel.Success, "插件操作完成"));
        Assert.Single(notifications);
        await first.DisposeAsync();
        await second.Log.WriteAsync(new(PluginLogLevel.Information, "另一插件仍可调用"));
        Assert.True(File.Exists(Path.Combine(root, "example.first", "store.db")));
        Assert.True(File.Exists(Path.Combine(root, "example.second", "store.db")));
    }
}