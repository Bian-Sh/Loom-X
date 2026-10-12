using System.Text;
using System.Text.Json;

/// <summary>只生成本次隔离目录内的可信源码夹具，不创建 csproj，也不请求 Provider。</summary>
internal static class SourcePluginFixture
{
    public const string Id = "playground.source";
    public const string Request = "playground-source-request";
    public const string Response = "playground-source-response";
    public const string Sentinel = "sk-playground-private-sentinel-0123456789";
    public static string Write(string pluginRoot, string generation = "g1", bool failInitialize = false)
    {
        var directory = Path.Combine(pluginRoot, Id);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(directory, "i18n"));
        var manifest = JsonSerializer.Serialize(new
        {
            id = Id, version = generation == "g1" ? "1.0.0" : "2.0.0",
            runtime = new { kind = "csharp-source", entry_type = "PlaygroundSource.Plugin", sources = new[] { "src/Plugin.cs" } },
            host_capabilities = new[] { "storage.sqlite/v1", "logging/v1", "notifications/v1" },
            capabilities = Array.Empty<string>(),
            extensions = new[]
            {
                new { id = "source.request", kind = "request", pipeline = Request, failure_policy = "fail-closed", capabilities = Array.Empty<string>() },
                new { id = "source.response", kind = "response", pipeline = Response, failure_policy = "fail-closed", capabilities = Array.Empty<string>() },
            },
        });
        File.WriteAllText(Path.Combine(directory, "plugin.manifest.json"), manifest, new UTF8Encoding(false));
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            File.WriteAllText(Path.Combine(directory, "i18n", culture + ".json"), "{\"title\":\"源码验证夹具\"}", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "src", "Plugin.cs"), Source
            .Replace("__GENERATION__", generation, StringComparison.Ordinal)
            .Replace("__FAIL_INITIALIZE__", failInitialize ? "true" : "false", StringComparison.Ordinal), new UTF8Encoding(false));
        // 未声明的错误源码必须被忽略，防止隐式 glob 编译。
        File.WriteAllText(Path.Combine(directory, "src", "Ignored.cs"), "此文件未列入 sources，不得编译。", new UTF8Encoding(false));
        return directory;
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using LoomX.Plugins;
        namespace PlaygroundSource;
        public sealed class Plugin : ILoomXPlugin, IAsyncDisposable
        {
            private const string Generation = "__GENERATION__";
            private const string Sentinel = "sk-playground-private-sentinel-0123456789";
            private IPluginBridge bridge = null!;
            private IPluginSqliteDatabase db = null!;
            public string Id => "playground.source";
            public void Initialize(PluginInitializationContext context)
            {
                bridge = context.Bridge;
                db = bridge.Storage.OpenSqliteAsync("cache/state.db").AsTask().GetAwaiter().GetResult();
                db.ExecuteAsync(new("CREATE TABLE IF NOT EXISTS Probe(Id INTEGER PRIMARY KEY, Value TEXT NOT NULL UNIQUE, Optional TEXT, Score REAL, Bytes BLOB);")).AsTask().GetAwaiter().GetResult();
                if (__FAIL_INITIALIZE__) throw new InvalidOperationException(Sentinel);
                bridge.Log.WriteAsync(new(PluginLogLevel.Information, "源码插件初始化完成 {Generation}",
                    new[] { new PluginLogField("Generation", new(PluginLogValueKind.Text, Text: Generation)) })).AsTask().GetAwaiter().GetResult();
                bridge.Notifications.ShowAsync(new(PluginNotificationLevel.Success, "源码插件初始化完成")).AsTask().GetAwaiter().GetResult();
            }
            public IEnumerable<IPipelineExtension> CreateExtensions() => new IPipelineExtension[] { new Request(this), new Response() };
            public ValueTask DisposeAsync() => db == null ? ValueTask.CompletedTask : db.DisposeAsync();
            private static void Require(bool condition)
            {
                if (!condition) throw new InvalidOperationException("源码插件检查未通过。");
            }
            private static PluginSqlCommand Insert(long id, string value) => new(
                "INSERT INTO Probe(Id,Value) VALUES($id,$value);",
                new[] { new PluginSqlParameter("$id", PluginSqlValue.FromInteger(id)), new PluginSqlParameter("$value", PluginSqlValue.FromText(value)) });
            private async ValueTask<string> Run(string operation, CancellationToken token)
            {
                switch (operation)
                {
                    case "crud":
                        await db.ExecuteAsync(new("DELETE FROM Probe;"), token);
                        Require(await db.ExecuteAsync(new("INSERT INTO Probe(Id,Value,Optional,Score,Bytes) VALUES($id,$value,$null,$real,$blob);", new[]
                        {
                            new PluginSqlParameter("$id", PluginSqlValue.FromInteger(1)),
                            new PluginSqlParameter("$value", PluginSqlValue.FromText(Sentinel)),
                            new PluginSqlParameter("$null", PluginSqlValue.Null),
                            new PluginSqlParameter("$real", PluginSqlValue.FromReal(1.25)),
                            new PluginSqlParameter("$blob", PluginSqlValue.FromBlob(new byte[] { 1, 2, 3 })),
                        }), token) == 1);
                        var row = await db.QuerySingleAsync(new("SELECT Id,Value,Optional,Score,Bytes FROM Probe WHERE Id=1;"), token);
                        Require(row != null && row["Id"].Kind == PluginSqlValueKind.Integer && row["Id"].Integer == 1
                            && row["Value"].Kind == PluginSqlValueKind.Text && row["Value"].Text == Sentinel
                            && row["Optional"].Kind == PluginSqlValueKind.Null && row["Score"].Kind == PluginSqlValueKind.Real
                            && row["Score"].Real == 1.25 && row["Bytes"].Kind == PluginSqlValueKind.Blob && row["Bytes"].Blob!.Length == 3 && row["Bytes"].Blob![0] == 1 && row["Bytes"].Blob![1] == 2 && row["Bytes"].Blob![2] == 3);
                        Require(await db.ExecuteAsync(new("UPDATE Probe SET Value=$value WHERE Id=$id;", new[]
                        {
                            new PluginSqlParameter("$value", PluginSqlValue.FromText("更新完成")),
                            new PluginSqlParameter("$id", PluginSqlValue.FromInteger(1)),
                        }), token) == 1);
                        var rows = await db.QueryAsync(new("SELECT Value FROM Probe ORDER BY Id;"), token);
                        Require(rows.Count == 1 && rows[0][0].Text == "更新完成");
                        Require(await db.ExecuteAsync(new("DELETE FROM Probe WHERE Id=$id;", new[]
                        { new PluginSqlParameter("$id", PluginSqlValue.FromInteger(1)) }), token) == 1);
                        Require(await db.QuerySingleAsync(new("SELECT Id FROM Probe;"), token) == null);
                        return "crud-ok:" + Generation;
                    case "commit":
                        await db.ExecuteAsync(new("DELETE FROM Probe;"), token);
                        await using (var tx = await db.BeginTransactionAsync(token))
                        {
                            await tx.ExecuteAsync(Insert(10, "提交一"), token);
                            await tx.ExecuteAsync(Insert(11, "提交二"), token);
                            Require((await tx.QueryAsync(new("SELECT Id FROM Probe;"), token)).Count == 2);
                            Require((await tx.QuerySingleAsync(new("SELECT Value FROM Probe WHERE Id=10;"), token))?[0].Text == "提交一");
                            await tx.CommitAsync(token);
                            try { await tx.ExecuteAsync(Insert(12, "已完成"), token); throw new InvalidOperationException(); }
                            catch (PluginSqlException error) { Require(error.Error == PluginSqlError.Disposed); }
                        }
                        Require((await db.QueryAsync(new("SELECT Id FROM Probe;"), token)).Count == 2);
                        return "commit-ok:" + Generation;
                    case "rollback":
                    case "dispose-rollback":
                    case "constraint-rollback":
                        await db.ExecuteAsync(new("DELETE FROM Probe;"), token);
                        await using (var tx = await db.BeginTransactionAsync(token))
                        {
                            await tx.ExecuteAsync(Insert(20, "回滚"), token);
                            if (operation == "constraint-rollback")
                            {
                                try { await tx.ExecuteAsync(Insert(20, Sentinel), token); throw new InvalidOperationException(); }
                                catch (PluginSqlException error) { Require(error.Error == PluginSqlError.ConstraintViolation); }
                            }
                            if (operation != "dispose-rollback") await tx.RollbackAsync(token);
                        }
                        Require((await db.QueryAsync(new("SELECT Id FROM Probe;"), token)).Count == 0);
                        return operation + "-ok:" + Generation;
                    case "transaction-isolation":
                        await using (var tx = await db.BeginTransactionAsync(token))
                        {
                            try { await db.QueryAsync(new("SELECT Id FROM Probe;"), token); throw new InvalidOperationException(); }
                            catch (PluginSqlException error) { Require(error.Error == PluginSqlError.TransactionActive); }
                            await tx.RollbackAsync(token);
                        }
                        return "transaction-isolation-ok:" + Generation;
                    case "path-rejection":
                        try { await bridge.Storage.OpenSqliteAsync("../other-plugin/escape.db", token); throw new InvalidOperationException(); }
                        catch (PluginSqlException error) { Require(error.Error == PluginSqlError.InvalidPath); }
                        return "path-rejection-ok:" + Generation;
                    case "feedback-rejection":
                        try
                        {
                            await bridge.Log.WriteAsync(new(PluginLogLevel.Information, "源码插件检查完成", new[]
                            { new PluginLogField("Authorization", new(PluginLogValueKind.Text, Text: Sentinel)) }), token);
                            throw new InvalidOperationException();
                        }
                        catch (PluginBridgeException error) { Require(error.Error == PluginBridgeError.InvalidArgument); }
                        try { await bridge.Notifications.ShowAsync(new(PluginNotificationLevel.Info, Sentinel), token); throw new InvalidOperationException(); }
                        catch (PluginBridgeException error) { Require(error.Error == PluginBridgeError.InvalidArgument); }
                        return "feedback-rejection-ok:" + Generation;
                    case "storage":
                        await db.QuerySingleAsync(new("SELECT COUNT(*) FROM Probe;"), token);
                        return "storage-ok:" + Generation;
                    case "feedback-inactive-union":
                        await bridge.Log.WriteAsync(new(PluginLogLevel.Information, "源码插件检查完成 {StatusCode}", new[]
                        { new PluginLogField("StatusCode", new(PluginLogValueKind.Integer, Integer: 200, Text: Sentinel)) }), token);
                        return "feedback-inactive-union-ok:" + Generation;
                    case "notify":
                        await bridge.Log.WriteAsync(new(PluginLogLevel.Information, "源码插件检查完成 {StatusCode}", new[]
                        { new PluginLogField("StatusCode", new(PluginLogValueKind.Integer, Integer: 200)) }), token);
                        await bridge.Notifications.ShowAsync(new(PluginNotificationLevel.Success, "源码插件检查完成"), token);
                        return "notify-ok:" + Generation;
                    default: return operation + "|request:" + Generation;
                }
            }
            private sealed class Request(Plugin owner) : IRequestExtension
            {
                public string ExtensionId => "source.request";
                public ExtensionKind Kind => ExtensionKind.Request;
                public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
                public IReadOnlyList<string> Capabilities => Array.Empty<string>();
                public async ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken cancellationToken) =>
                    PipelineResult.Modify(await owner.Run(payload, cancellationToken));
            }
            private sealed class Response : IResponseExtension
            {
                public string ExtensionId => "source.response";
                public ExtensionKind Kind => ExtensionKind.Response;
                public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
                public IReadOnlyList<string> Capabilities => Array.Empty<string>();
                public ValueTask<PipelineResult> ProcessResponseAsync(PipelineContext context, string payload, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(PipelineResult.Modify(payload + "|response:" + Generation));
            }
        }
        """;
}
