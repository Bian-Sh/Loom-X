using System.Reflection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginProtectionContractTests
{
    [Fact]
    public void 保护契约只有两个可传输异步方法且旧Bridge默认不可用()
    {
        var type = typeof(IPluginBridge).Assembly.GetType("LoomX.Plugins.IPluginProtectionBridge");
        Assert.NotNull(type);
        Assert.Equal(new[] { "DecryptAsync", "EncryptAsync" }, type.GetMethods().Select(m => m.Name).Order().ToArray());
        foreach (var method in type.GetMethods())
        {
            Assert.Equal(typeof(ValueTask<string>), method.ReturnType);
            Assert.Equal(new[] { typeof(string), typeof(CancellationToken) }, method.GetParameters().Select(p => p.ParameterType));
        }
        var property = typeof(IPluginBridge).GetProperty("Protection");
        Assert.NotNull(property);
        Assert.Equal(type, property.PropertyType);
        Assert.NotNull(property.GetValue(new LegacyBridge()));
    }

    [Fact]
    public void 能力目录必须精确支持保护v1且拒绝未知版本()
    {
        Assert.Contains("protection/v1", PluginCapabilityCatalog.Supported);
        Assert.Empty(PluginCapabilityCatalog.Validate("example.safe", ["protection/v1"]));
        Assert.Single(PluginCapabilityCatalog.Validate("example.safe", ["protection/v2"]));
        Assert.Throws<PluginBridgeException>(() => new PluginBridgeScope("example.safe", ["protection/v1"]));
    }

    // 旧实现不增加Protection成员，保证默认接口成员保持源码兼容。
    private sealed class LegacyBridge : IPluginBridge
    {
        public IPluginStorageBridge Storage => PluginBridgeDefaults.Unavailable.Storage;
        public IPluginLogBridge Log => PluginBridgeDefaults.Unavailable.Log;
        public IPluginNotificationBridge Notifications => PluginBridgeDefaults.Unavailable.Notifications;
    }
}