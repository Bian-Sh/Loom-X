using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX;
using LoomX.Assistant;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Plugin.Storage.Sqlite;
using LoomX.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var root = Path.GetFullPath(args[0]);
var id = args[1];
var second = args.Length > 2;
var development = Path.Combine(root, "development");
var installed = Path.Combine(root, "installed");
var data = Path.Combine(AppDataPaths.RootDirectory, "plugins");
Directory.CreateDirectory(installed);
var cache = Path.Combine(root, "cache");
var composition = typeof(PluginDevelopmentService).Assembly.GetType("LoomX.Services.PluginDevelopmentComposition", true)!;
// 仅记录本次探针环境，不读取生产配置或凭据内容。
Console.WriteLine($"环境：PID={Environment.ProcessId}；阶段={(second ? "重启" : "闭环")}；AppBase={AppContext.BaseDirectory}；AppData={data}；状态根={root}");
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PATH")) || Environment.GetEnvironmentVariable("DOTNET_ROOT") != AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) || Environment.GetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP") != "0" || Environment.GetEnvironmentVariable("DOTNET_ROLL_FORWARD") != "Disable" || Directory.Exists(Path.Combine(AppContext.BaseDirectory, "sdk")))
    throw new InvalidOperationException("运行环境不满足发布副本无 SDK 约束");
var assertions = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; Console.WriteLine("[通过] " + message); }
(string? Stamp, long? Length) Stamp(string path) => File.Exists(path) ? (File.GetLastWriteTimeUtc(path).ToString("O"), new FileInfo(path).Length) : (null, null);
var before = new[] { AppDataPaths.DatabasePath, AppDataPaths.ActivityDatabasePath }.Select(Stamp).ToArray();
ServiceProvider? services = null;
PluginRuntime? runtime = null;
ToolRegistry? registry = null;
async Task Setup()
{
    if (runtime is not null) await runtime.DisposeAsync();
    if (services is not null) await services.DisposeAsync();
    var collection = new ServiceCollection();
    collection.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
    composition.GetMethod("RegisterServices")!.Invoke(null, [collection, development, cache]);
    var factory = new PluginHostBridgeFactory(data, new ToastService(), NullLoggerFactory.Instance);
    collection.AddSingleton(provider => PluginRuntime.Start(new() { PluginDirectory = installed, DataRootDirectory = data, SourceCompiler = provider.GetRequiredService<SourcePluginCompiler>(), BridgeFactory = factory.CreateScope }, NullLoggerFactory.Instance));
    services = collection.BuildServiceProvider();
    runtime = services.GetRequiredService<PluginRuntime>();
    registry = new ToolRegistry();
    composition.GetMethod("RegisterTools")!.Invoke(null, [registry, services]);
    Check(registry.All.Count == 6, "发布 DLL 的生产组合根注册六工具");
}
async Task<JsonObject> Tool(string operation, bool success, JsonObject? extra = null)
{
    Check(registry!.TryGet("loomx.plugin." + operation, out var tool), "找到工具 " + operation);
    var input = extra ?? new JsonObject(); input["plugin_id"] = id;
    var result = await tool!.Handler(input, CancellationToken.None);
    Console.WriteLine($"工具事件：{operation}；实际成功={result.Success}；预期成功={success}");
    Check(result.Success == success, "工具 " + operation + " 返回预期状态");
    return JsonNode.Parse(result.Content)!.AsObject();
}
void CopyPackage()
{
    var source = Path.Combine(development, id); var destination = Path.Combine(installed, id);
    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
    }
}
string Code(string version) => """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LoomX.Plugins;
namespace Development;
public sealed class Plugin : ILoomXPlugin
{
    public string Id => "PLUGIN_ID";
    public void Initialize(PluginInitializationContext context)
    {
        var database = context.Bridge.Storage.OpenSqliteAsync("probe.db").AsTask().GetAwaiter().GetResult();
        try
        {
            database.ExecuteAsync(new("CREATE TABLE IF NOT EXISTS Runs(Id INTEGER PRIMARY KEY, Count INTEGER NOT NULL)")).AsTask().GetAwaiter().GetResult();
            database.ExecuteAsync(new("INSERT INTO Runs VALUES(1,1) ON CONFLICT(Id) DO UPDATE SET Count=Count+1")).AsTask().GetAwaiter().GetResult();
        }
        finally { database.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    public IEnumerable<IPipelineExtension> CreateExtensions() => new IPipelineExtension[] { new Request() };
    public sealed class Request : IRequestExtension
    {
        public string ExtensionId => "request";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => System.Array.Empty<string>();
        public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken cancellationToken = default) => ValueTask.FromResult(PipelineResult.Modify("VERSION:" + payload));
    }
}
""".Replace("PLUGIN_ID", id).Replace("VERSION", version);
void Stage(string version)
{
    var package = Path.Combine(development, id);
    var manifestPath = Path.Combine(package, "plugin.manifest.json");
    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
    manifest["version"] = version;
    manifest["host_capabilities"] = new JsonArray("storage.sqlite/v1");
    manifest["extensions"] = JsonSerializer.SerializeToNode(new[] { new { id = "request", kind = "request", pipeline = "request", failure_policy = "continue-on-error" } });
    File.WriteAllText(manifestPath, manifest.ToJsonString());
    File.WriteAllText(Path.Combine(package, "src/Plugin.cs"), Code(version));
}
async Task<long> ReadCount()
{
    await using var storage = new PluginSqliteStorageBridge(id, data);
    await using var db = await storage.OpenSqliteAsync("probe.db");
    return (await db.QuerySingleAsync(new("SELECT Count FROM Runs WHERE Id=1")))![0].Integer;
}
try
{
    await Setup();
    if (!second)
    {
        Check(runtime!.PluginInfos.Count == 0, "不加载真实 Provider 或生产凭证插件");
        await Tool("create", true);
        await Tool("validate", true);
        var source = Path.Combine(development, id, "src/Plugin.cs");
        File.AppendAllText(source, "\n故意语法错误");
        var failed = await Tool("compile", false);
        Console.WriteLine("预期语法错误诊断：" + failed["diagnostics"]!.ToJsonString());
        Check(failed["diagnostics"]!.AsArray().Any(d => d!["stage"]!.GetValue<string>() == "compile" && d["line"] is not null), "语法错误返回带位置的编译诊断");
        await Tool("diagnostics", false);
        Stage("1");
        await Tool("compile", true);
        await Tool("test", false);
        var tested = await Tool("test", true, new JsonObject { ["trust_code"] = true, ["cases"] = JsonSerializer.SerializeToNode(new[] { new { pipeline = "request", payload = "安全样例", expected = "1:安全样例", outcome = "modified" } }) });
        Check(tested["tests_passed"]!.GetValue<int>() >= 2, "临时 Bridge 与 Pipeline、自测试执行成功");
        Check(!File.Exists(Path.Combine(data, id, "probe.db")), "test 不写真实数据根");
        await Tool("reload", false, new JsonObject { ["trust_code"] = true });
        CopyPackage(); await Setup();
        Check(runtime!.PluginInfos.Single().Version == "1", "源码首次经生产 Runtime 加载");
        await using var old = runtime.AcquireLease();
        Stage("2"); CopyPackage();
        await Tool("reload", true, new JsonObject { ["trust_code"] = true });
        Check((await old.GetPipeline("request")!.ExecuteAsync("安全样例")).Payload == "1:安全样例", "旧租约仍使用原代际");
        Check((await runtime.GetPipeline("request")!.ExecuteAsync("安全样例")).Payload == "2:安全样例", "新请求切换新代际");
        File.AppendAllText(Path.Combine(installed, id, "src/Plugin.cs"), "\n故意语法错误");
        await Tool("reload", false, new JsonObject { ["trust_code"] = true });
        Check(runtime.PluginInfos.Single().Version == "2", "失败更新保留当前实例");
        CopyPackage();
        Console.WriteLine("闭环数据库计数=" + await ReadCount());
        Check(await ReadCount() == 2, "SQLite 只记录成功加载及成功重载");
    }
    else
    {
        var compiled = await Tool("compile", true);
        Console.WriteLine("重启编译 cache_hit=" + compiled["cache_hit"]!.ToJsonString());
        Check(compiled["cache_hit"]!.GetValue<bool>(), "新进程重启后命中缓存");
        Check(runtime!.PluginInfos.Single().Version == "2", "重启后生产 Runtime 重新加载新代际");
        Console.WriteLine("重启数据库计数=" + await ReadCount());
        Check(await ReadCount() == 3, "重启后原插件 SQLite 数据仍在同一路径");
    }
    Check(File.Exists(Path.Combine(data, id, "probe.db")), "数据位于 AppDataPaths 对应 plugins 唯一子目录");
    Check(before.SequenceEqual(new[] { AppDataPaths.DatabasePath, AppDataPaths.ActivityDatabasePath }.Select(Stamp)), "配置及活动库时间戳与长度未被探针修改");
    // 记录真正加载的程序集与 native runtime 路径，供发布 Hash 交叉核对。
    var loaded = new {
        pid = Environment.ProcessId,
        phase = second ? "restart" : "closed-loop",
        assertions,
        database = Path.Combine(data, id, "probe.db"),
        configurationMetadataBefore = before,
        assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => new { name = a.GetName().Name, path = a.Location }).ToArray(),
        nativeModules = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Where(m => new[] { "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "e_sqlite3.dll" }.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase)).Select(m => new { name = m.ModuleName, path = m.FileName }).ToArray()
    };
    File.WriteAllText(Path.Combine(root, second ? "restart-loaded.json" : "closed-loop-loaded.json"), JsonSerializer.Serialize(loaded, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    Console.WriteLine($"结果：{assertions} 项断言通过；阶段：{(second ? "重启" : "闭环")}；PID={Environment.ProcessId}。");
}
finally { if (runtime is not null) await runtime.DisposeAsync(); if (services is not null) await services.DisposeAsync(); }
