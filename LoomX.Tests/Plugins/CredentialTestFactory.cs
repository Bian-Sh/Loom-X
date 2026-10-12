using LoomX.CredentialProtection;
using LoomX.Plugin.Storage.Sqlite;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace LoomX.Tests.Plugins;

/// <summary>测试侧接线真实宿主 Bridge；插件只接收公共契约，原测试目录和 Vault 路径不变。</summary>
internal sealed class CredentialTestFactory : IDisposable
{
    private readonly List<IAsyncDisposable> resources = [];

    internal IPluginStorageBridge CreateStorage(string dataDirectory, ILogger<PluginSqliteStorageBridge>? logger = null)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        // 测试目录名称作为绑定身份，Bridge 的最终插件根恰好等于原测试目录。
        var storage = new PluginSqliteStorageBridge(Path.GetFileName(directory), Path.GetDirectoryName(directory)!, logger);
        resources.Add(storage);
        return storage;
    }

    internal static IPluginProtectionBridge CreateProtection() =>
        new PluginProtectionBridge(CredentialProtectionPlugin.PluginId, NullLogger<PluginProtectionBridge>.Instance);

    internal CredentialTokenStore CreateStore(string dataDirectory)
    {
        var store = new CredentialTokenStore(CreateStorage(dataDirectory), CreateProtection());
        resources.Add(store);
        return store;
    }

    internal CredentialEngine CreateEngine(string dataDirectory) => CreateEngine(new SensitiveRuleStore(dataDirectory));
    internal CredentialEngine CreateEngine(SensitiveRuleStore rules) => new(rules, CreateStore(rules.DataDirectory));

    internal PluginInitializationContext CreateContext(string dataDirectory) => CreateContext(dataDirectory, CreateStorage(dataDirectory));

    internal PluginInitializationContext CreateContext(string dataDirectory, IPluginStorageBridge storage) => new(CredentialProtectionPlugin.PluginId, dataDirectory)
    {
        Bridge = new TestBridge(storage),
    };

    internal CredentialProtectionPlugin CreatePlugin(string dataDirectory)
    {
        var plugin = new CredentialProtectionPlugin();
        var context = CreateContext(dataDirectory);
        resources.Add(plugin);
        plugin.Initialize(context);
        plugin.CreateExtensions().ToArray();
        return plugin;
    }

    public void Dispose() => Task.Run(async () =>
    {
        foreach (var resource in resources.AsEnumerable().Reverse())
            await resource.DisposeAsync().ConfigureAwait(false);
        resources.Clear();
    }).GetAwaiter().GetResult();

    private sealed class TestBridge(IPluginStorageBridge storage) : IPluginBridge
    {
        public IPluginStorageBridge Storage => storage;
        public IPluginProtectionBridge Protection { get; } = CreateProtection();
        public IPluginLogBridge Log => PluginBridgeDefaults.Unavailable.Log;
        public IPluginNotificationBridge Notifications => PluginBridgeDefaults.Unavailable.Notifications;
    }
}
