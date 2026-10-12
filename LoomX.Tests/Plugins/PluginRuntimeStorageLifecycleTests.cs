using System.Runtime.Loader;
using System.Text.Json.Nodes;
using LoomX.Plugin.Storage.Sqlite;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginRuntimeStorageLifecycleTests
{
    [Fact]
    public async Task 真实SQLite旧代际等待租约且退休回滚事务不影响另一插件()
    {
        // collectible ALC 仍可能等待 GC；只保留本夹具临时包，不强制删除已映射的 DLL。
        var package = new SourcePluginTestPackage();
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Directory, "plugin.manifest.json")))!;
        document["host_capabilities"] = new JsonArray("storage.sqlite/v1");
        package.Write("plugin.manifest.json", document.ToJsonString());
        var data = Path.Combine(package.Root, "data");
        List<PluginSqliteStorageBridge> stores = [];
        await using var runtime = PluginRuntime.Start(new()
        {
            PluginDirectory = package.Root, DataRootDirectory = data, SourceCompiler = package.Compiler,
            BridgeFactory = (manifest, _) =>
            {
                var storage = new PluginSqliteStorageBridge(manifest.Id, data);
                stores.Add(storage);
                return new(manifest.Id, manifest.HostCapabilities, storage: storage);
            },
        }, NullLoggerFactory.Instance);
        Assert.Single(runtime.PluginInfos);
        await using var database = await stores[0].OpenSqliteAsync("vault.db");
        await database.ExecuteAsync(new("CREATE TABLE Items(Id INTEGER)"));
        await using var transaction = await database.BeginTransactionAsync();
        await transaction.ExecuteAsync(new("INSERT INTO Items VALUES(1)"));
        await using var unrelated = new PluginSqliteStorageBridge("unrelated", data);
        await using var other = await unrelated.OpenSqliteAsync("vault.db");
        await other.ExecuteAsync(new("CREATE TABLE Items(Id INTEGER)"));
        var lease = runtime.AcquireLease();
        document["version"] = "2";
        package.Write("plugin.manifest.json", document.ToJsonString());
        Assert.True(await runtime.ReloadAsync("demo"));
        Assert.Equal(1, (await transaction.QuerySingleAsync(new("SELECT COUNT(*) FROM Items")))![0].Integer);
        await lease.DisposeAsync();
        var error = await Assert.ThrowsAsync<PluginSqlException>(async () => await database.QueryAsync(new("SELECT * FROM Items")));
        Assert.Equal(PluginSqlError.Disposed, error.Error);
        await using var reopened = await stores[1].OpenSqliteAsync("vault.db");
        Assert.Equal(0, (await reopened.QuerySingleAsync(new("SELECT COUNT(*) FROM Items")))![0].Integer);
        Assert.Equal(1, await other.ExecuteAsync(new("INSERT INTO Items VALUES(2)")));
        Assert.True(File.Exists(Path.Combine(data, "demo", "vault.db")));
    }
}