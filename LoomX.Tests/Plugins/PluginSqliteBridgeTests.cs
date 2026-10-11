using System.Reflection;
using LoomX.Plugins;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginSqliteBridgeTests : SqliteBridgeFixture
{
    [Fact]
    public async Task 实现位于独立宿主程序集且契约不依赖原生库()
    {
        await using var scope = CreateStorage();
        Assert.Equal("LoomX.Plugin.Storage.Sqlite", scope.Storage.GetType().Assembly.GetName().Name);
        Assert.DoesNotContain(typeof(IPluginStorageBridge).Assembly.GetReferencedAssemblies(), x => x.Name!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("SQLitePCL"));
        Assert.DoesNotContain(Assembly.Load("LoomX.ToolResultCompression").GetReferencedAssemblies(), x => x.Name!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("SQLitePCL"));
    }

    [Fact]
    public async Task 参数化CRUD与所有值种类保留顺序和重复列名()
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("custom/vault.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(id INTEGER PRIMARY KEY, n, i, r, t, b)"));
        await db.ExecuteAsync(Sql("CREATE INDEX ix_data ON data(t)"));
        var parameters = new[] { new PluginSqlParameter("$n", PluginSqlValue.Null), new PluginSqlParameter("$i", PluginSqlValue.FromInteger(long.MaxValue)), new PluginSqlParameter("$r", PluginSqlValue.FromReal(1.25)), new PluginSqlParameter("$t", PluginSqlValue.FromText("中文;\0文本")), new PluginSqlParameter("$b", PluginSqlValue.FromBlob([0, 1, 255])) };
        Assert.Equal(1, await db.ExecuteAsync(new("INSERT INTO data VALUES(1,$n,$i,$r,$t,$b)", parameters)));
        var row = Assert.Single(await db.QueryAsync(Sql("SELECT n,i,r,t,b,i AS duplicate,t AS duplicate FROM data")));
        Assert.Equal(PluginSqlValueKind.Null, row[0].Kind);
        Assert.Equal(long.MaxValue, row[1].Integer);
        Assert.Equal(1.25, row[2].Real);
        Assert.Equal("中文;\0文本", row[3].Text);
        Assert.Equal(new byte[] { 0, 1, 255 }, row[4].Blob);
        Assert.Equal(long.MaxValue, row[5].Integer);
        Assert.Equal("中文;\0文本", row[6].Text);
        Assert.Equal(1, await db.ExecuteAsync(Sql("UPDATE data SET t='updated' WHERE id=1")));
        Assert.Equal(1, await db.ExecuteAsync(Sql("DELETE FROM data")));
        Assert.Null(await db.QuerySingleAsync(Sql("SELECT * FROM data")));
    }

    [Fact]
    public async Task 单行只返回第一行且空Blob不是Null()
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("single.db");
        var row = await db.QuerySingleAsync(Sql("SELECT 1, x'' UNION ALL SELECT 2, x'ff'"));
        Assert.Equal(1, row![0].Integer);
        Assert.Equal(PluginSqlValueKind.Blob, row[1].Kind);
        Assert.Empty(row[1].Blob!);
    }

    [Theory]
    [InlineData("SELECT ';' AS value; -- 注释 ;\n /* ; */", ";")]
    [InlineData("/* 注释 ; */ SELECT 'a;''b' AS value", "a;'b")]
    public async Task SQLite语法内的分号不是批处理(string text, string value)
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("syntax.db");
        Assert.Equal(value, (await db.QuerySingleAsync(Sql(text)))![0].Text);
    }

    [Theory]
    [InlineData("CREATE TABLE forbidden(x); INSERT INTO forbidden VALUES(1)")]
    [InlineData("SELECT 1; /* ; */ SELECT 2")]
    [InlineData("SELECT 1\0; SELECT 2")]
    [InlineData("-- 只有注释")]
    [InlineData("SELECT $missing")]
    public async Task 多语句空语句和缺参数拒绝且首语句不落盘(string text)
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("batch.db");
        await Error(PluginSqlError.InvalidSql, () => db.ExecuteAsync(Sql(text)).AsTask());
        Assert.Equal(0, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM sqlite_master WHERE name='forbidden'")))![0].Integer);
    }

    [Theory]
    [InlineData("ATTACH DATABASE 'outside.db' AS other")]
    [InlineData("VACUUM INTO 'outside.db'")]
    [InlineData("SELECT load_extension('SENSITIVE_SQL_SENTINEL')")]
    [InlineData("PRAGMA temp_store_directory='outside'")]
    [InlineData("PRAGMA data_store_directory='outside'")]
    [InlineData("PRAGMA writable_schema=ON")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    [InlineData("SAVEPOINT rogue")]
    [InlineData("PRAGMA journal_mode=OFF")]
    public async Task 文件逃逸扩展和裸事务被拒绝(string text)
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("policy.db");
        await Error(PluginSqlError.InvalidSql, () => db.ExecuteAsync(Sql(text)).AsTask());
        Assert.False(File.Exists(Path.Combine(Root, "outside.db")));
    }

    [Fact]
    public async Task 合法PRAGMA与触发器保持SQLite语义()
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("pragmas.db");
        foreach (var sql in new[] { "PRAGMA journal_mode=WAL", "PRAGMA synchronous=NORMAL", "PRAGMA busy_timeout=100", "PRAGMA user_version=7", "CREATE TABLE a(x)", "CREATE TABLE b(x)", "CREATE TRIGGER tr AFTER INSERT ON a BEGIN INSERT INTO b VALUES(new.x); INSERT INTO b VALUES(new.x+1); END", "INSERT INTO a VALUES(3)" }) await db.ExecuteAsync(Sql(sql));
        Assert.Equal(7, (await db.QuerySingleAsync(Sql("PRAGMA user_version")))![0].Integer);
        Assert.Equal(2, (await db.QueryAsync(Sql("SELECT * FROM b"))).Count);
    }

    [Fact]
    public async Task 事务提交释放回滚完成拒绝复用且非事务不混入()
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("transaction.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x UNIQUE)"));
        var tx = await db.BeginTransactionAsync();
        await tx.ExecuteAsync(Sql("INSERT INTO data VALUES(1)"));
        await Error(PluginSqlError.TransactionActive, () => db.ExecuteAsync(Sql("INSERT INTO data VALUES(99)")).AsTask());
        await Error(PluginSqlError.TransactionActive, () => db.BeginTransactionAsync().AsTask());
        await tx.CommitAsync();
        await Error(PluginSqlError.Disposed, () => tx.QueryAsync(Sql("SELECT * FROM data")).AsTask());
        await tx.DisposeAsync();
        await using (var abandoned = await db.BeginTransactionAsync())
        {
            await abandoned.ExecuteAsync(Sql("INSERT INTO data VALUES(2)"));
            await Error(PluginSqlError.ConstraintViolation, () => abandoned.ExecuteAsync(Sql("INSERT INTO data VALUES(2)")).AsTask());
        }
        Assert.Equal(1, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
        var rolledBack = await db.BeginTransactionAsync();
        await rolledBack.ExecuteAsync(Sql("INSERT INTO data VALUES(3)"));
        await rolledBack.RollbackAsync();
        await Error(PluginSqlError.Disposed, () => rolledBack.CommitAsync().AsTask());
    }

    [Fact]
    public async Task 多连接竞争映射Busy且无共享事务污染()
    {
        await using var scope = CreateStorage();
        await using var a = await scope.Storage.OpenSqliteAsync("busy.db");
        await using var b = await scope.Storage.OpenSqliteAsync("busy.db");
        await a.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        await b.ExecuteAsync(Sql("PRAGMA busy_timeout=30"));
        await using var tx = await a.BeginTransactionAsync();
        await tx.ExecuteAsync(Sql("INSERT INTO data VALUES(1)"));
        var ex = await Error(PluginSqlError.Busy, () => b.ExecuteAsync(Sql("INSERT INTO data VALUES(2)")).AsTask());
        Assert.True(ex.Retryable);
        await tx.RollbackAsync();
        Assert.Equal(1, await b.ExecuteAsync(Sql("INSERT INTO data VALUES(2)")));
    }

    [Fact]
    public async Task 预取消及执行中取消均为稳定错误且连接仍可使用()
    {
        await using var scope = CreateStorage();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Error(PluginSqlError.Cancelled, () => scope.Storage.OpenSqliteAsync("cancel.db", cancelled.Token).AsTask());
        await using var db = await scope.Storage.OpenSqliteAsync("cancel.db");
        await Error(PluginSqlError.Cancelled, () => db.QueryAsync(Sql("SELECT 1"), cancelled.Token).AsTask());
        await Error(PluginSqlError.Cancelled, () => db.BeginTransactionAsync(cancelled.Token).AsTask());
        using var running = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Error(PluginSqlError.Cancelled, () => db.QuerySingleAsync(Sql("WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<1000000000) SELECT sum(x) FROM n"), running.Token).AsTask());
        Assert.Equal(1, (await db.QuerySingleAsync(Sql("SELECT 1")))![0].Integer);
    }

    [Fact]
    public async Task 行值和总结果上限由真实SQL验证()
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("limits.db");
        await Error(PluginSqlError.ResourceLimit, () => db.QuerySingleAsync(Sql("SELECT zeroblob(2097152)")).AsTask());
        await Error(PluginSqlError.ResourceLimit, () => db.QueryAsync(Sql("WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<20000) SELECT x FROM n")).AsTask());
        await Error(PluginSqlError.ResourceLimit, () => db.QueryAsync(Sql("WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<20) SELECT zeroblob(900000) FROM n")).AsTask());
        await Error(PluginSqlError.ResourceLimit, () => db.ExecuteAsync(new("SELECT $blob", [new("$blob", PluginSqlValue.FromBlob(new byte[2097152]))])).AsTask());
    }

    [Fact]
    public async Task 损坏只读约束和语法错误不泄漏敏感数据()
    {
        var logger = new CaptureLogger();
        await using var scope = CreateStorage(logger: logger);
        Directory.CreateDirectory(Path.Combine(Root, "example.plugin"));
        await File.WriteAllTextAsync(Path.Combine(Root, "example.plugin", "corrupt.db"), "这不是数据库 SENSITIVE_SQL_SENTINEL");
        await Error(PluginSqlError.Corrupt, async () => { await using var corrupt = await scope.Storage.OpenSqliteAsync("corrupt.db"); await corrupt.QueryAsync(Sql("SELECT * FROM sqlite_master")); });
        await using var db = await scope.Storage.OpenSqliteAsync("errors.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x UNIQUE)"));
        await db.ExecuteAsync(new("INSERT INTO data VALUES($value)", [new("$value", PluginSqlValue.FromText("SENSITIVE_SQL_SENTINEL"))]));
        await Error(PluginSqlError.ConstraintViolation, () => db.ExecuteAsync(new("INSERT INTO data VALUES($value)", [new("$value", PluginSqlValue.FromText("SENSITIVE_SQL_SENTINEL"))])).AsTask());
        await Error(PluginSqlError.InvalidSql, () => db.ExecuteAsync(Sql("SELECT SENSITIVE_SQL_SENTINEL")).AsTask());
        await db.ExecuteAsync(Sql("PRAGMA query_only=ON"));
        await Error(PluginSqlError.ReadOnly, () => db.ExecuteAsync(Sql("INSERT INTO data VALUES(2)")).AsTask());
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, s => s.Contains("SENSITIVE_SQL_SENTINEL") || s.Contains(Root) || s.Contains("INSERT INTO"));
        Assert.Contains(logger.Levels, l => l == LogLevel.Information);
        Assert.Contains(logger.Levels, l => l == LogLevel.Warning || l == LogLevel.Error);
    }

    [Fact]
    public async Task Storage释放回滚全部关闭句柄且不影响另一插件()
    {
        var owner = CreateStorage();
        await using var other = CreateStorage("other.plugin");
        var db = await owner.Storage.OpenSqliteAsync("lifecycle.db");
        var second = await owner.Storage.OpenSqliteAsync("second.db");
        await using var independent = await other.Storage.OpenSqliteAsync("lifecycle.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        var tx = await db.BeginTransactionAsync(); await tx.ExecuteAsync(Sql("INSERT INTO data VALUES(1)"));
        await owner.DisposeAsync(); await owner.DisposeAsync();
        await Error(PluginSqlError.Disposed, () => db.QueryAsync(Sql("SELECT 1")).AsTask());
        await Error(PluginSqlError.Disposed, () => second.QueryAsync(Sql("SELECT 1")).AsTask());
        await Error(PluginSqlError.Disposed, () => tx.CommitAsync().AsTask());
        await Error(PluginSqlError.Disposed, () => owner.Storage.OpenSqliteAsync("recreated.db").AsTask());
        Assert.Equal(1, (await independent.QuerySingleAsync(Sql("SELECT 1")))![0].Integer);
        await using var reopened = CreateStorage();
        await using var check = await reopened.Storage.OpenSqliteAsync("lifecycle.db");
        Assert.Equal(0, (await check.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
        await db.DisposeAsync(); await second.DisposeAsync(); await tx.DisposeAsync();
        using var exclusive = new FileStream(Path.Combine(Root, "example.plugin", "second.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task 参数绑定保留空Text和空Blob而不是Null()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("empty-values.db");
        var row = await db.QuerySingleAsync(new("SELECT $text, $blob", [new("$text", PluginSqlValue.FromText("")), new("$blob", PluginSqlValue.FromBlob([]))]));
        Assert.Equal(PluginSqlValueKind.Text, row![0].Kind); Assert.Equal("", row[0].Text);
        Assert.Equal(PluginSqlValueKind.Blob, row[1].Kind); Assert.Empty(row[1].Blob!);
    }

    [Fact]
    public async Task 兼容既有Vault的五秒busy_timeout配置()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("vault-pragmas.db");
        await db.ExecuteAsync(Sql("PRAGMA journal_mode=WAL"));
        await db.ExecuteAsync(Sql("PRAGMA synchronous=NORMAL"));
        await db.ExecuteAsync(Sql("PRAGMA busy_timeout=5000"));
        Assert.Equal(5000, (await db.QuerySingleAsync(Sql("PRAGMA busy_timeout")))![0].Integer);
    }

    [Fact]
    public async Task 取消事务提交不提交且仍可显式回滚()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("cancel-commit.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x)")); var tx = await db.BeginTransactionAsync();
        await tx.ExecuteAsync(Sql("INSERT INTO data VALUES(1)"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Error(PluginSqlError.Cancelled, () => tx.CommitAsync(cancel.Token).AsTask());
        await Error(PluginSqlError.Cancelled, () => tx.RollbackAsync(cancel.Token).AsTask());
        await Error(PluginSqlError.Cancelled, () => db.ExecuteAsync(Sql("INSERT INTO data VALUES(99)"), cancel.Token).AsTask());
        await tx.RollbackAsync();
        Assert.Equal(0, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
    }

    [Fact]
    public async Task 句柄上限释放后可恢复且旧句柄不可复用()
    {
        await using var scope = CreateStorage();
        var handles = new List<IPluginSqliteDatabase>();
        for (var i = 0; i < 64; i++) handles.Add(await scope.Storage.OpenSqliteAsync($"limit-{i}.db"));
        await Error(PluginSqlError.ResourceLimit, () => scope.Storage.OpenSqliteAsync("over-limit.db").AsTask());
        await handles[0].DisposeAsync();
        await Error(PluginSqlError.Disposed, () => handles[0].BeginTransactionAsync().AsTask());
        await using var next = await scope.Storage.OpenSqliteAsync("recovered.db");
        foreach (var handle in handles) await handle.DisposeAsync();
    }

    [Fact]
    public async Task 被拒绝的PRAGMA批处理不得在prepare阶段改变连接()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("pragma-batch.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        await Error(PluginSqlError.InvalidSql, () => db.ExecuteAsync(Sql("PRAGMA query_only=ON; SELECT 1")).AsTask());
        Assert.Equal(1, await db.ExecuteAsync(Sql("INSERT INTO data VALUES(1)")));
        await Error(PluginSqlError.InvalidSql, () => db.ExecuteAsync(Sql("PRAGMA busy_timeout=900; SELECT 1")).AsTask());
        Assert.Equal(100, (await db.QuerySingleAsync(Sql("PRAGMA busy_timeout")))![0].Integer);
    }

    [Fact]
    public async Task 既有大页数据库复用原文件且磁盘上限按真实页大小计算()
    {
        Directory.CreateDirectory(Path.Combine(Root, "example.plugin"));
        var file = Path.Combine(Root, "example.plugin", "existing.db");
        using (var existing = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + file + ";Pooling=False"))
        {
            existing.Open(); using var command = existing.CreateCommand();
            command.CommandText = "PRAGMA page_size=65536; CREATE TABLE legacy(token TEXT, cipher BLOB); INSERT INTO legacy VALUES('opaque', x'010203')"; command.ExecuteNonQuery();
        }
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("existing.db");
        Assert.Equal(new byte[] { 1, 2, 3 }, (await db.QuerySingleAsync(Sql("SELECT cipher FROM legacy WHERE token='opaque'")))![0].Blob);
        var nativeConnection = (Microsoft.Data.Sqlite.SqliteConnection)db.GetType().GetField("connection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(db)!;
        using var limit = nativeConnection.CreateCommand();
        // 测试读取宿主固定策略，暂时撤掉 authorizer；恢复由该句柄释放保证，不交给插件。
        SQLitePCL.raw.sqlite3_set_authorizer(nativeConnection.Handle!, (SQLitePCL.strdelegate_authorizer?)null, null);
        limit.CommandText = "PRAGMA max_page_count";
        Assert.Equal(4096L, (long)limit.ExecuteScalar()!);
        Assert.Single(Directory.GetFiles(Root, "*.db", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task 中断写入后原生自动回滚不会留下幽灵事务()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("interrupt-write.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        var tx = await db.BeginTransactionAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        await Error(PluginSqlError.Cancelled, () => tx.ExecuteAsync(Sql("WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<100000000) INSERT INTO data SELECT x FROM n"), cancel.Token).AsTask());
        await Error(PluginSqlError.Disposed, () => tx.ExecuteAsync(Sql("INSERT INTO data VALUES(99)")).AsTask());
        Assert.Equal(0, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
        await tx.DisposeAsync();
    }

    [Fact]
    public async Task 文件占用访问拒绝使用稳定PermissionDenied()
    {
        await using var scope = CreateStorage();
        await using (var created = await scope.Storage.OpenSqliteAsync("permission.db")) await created.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        using var locked = new FileStream(Path.Combine(Root, "example.plugin", "permission.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Error(PluginSqlError.PermissionDenied, () => scope.Storage.OpenSqliteAsync("permission.db").AsTask());
    }

    [Theory]
    [InlineData(6, PluginSqlError.Locked)]
    [InlineData(10, PluginSqlError.Unknown)]
    [InlineData(1034, PluginSqlError.Unknown)]
    [InlineData(7, PluginSqlError.ResourceLimit)]
    [InlineData(23, PluginSqlError.PermissionDenied)]
    public void 无法稳定构造的原生错误码也采用安全映射(int code, PluginSqlError expected)
    {
        var type = Type.GetType("LoomX.Plugin.Storage.Sqlite.PluginSqliteErrors, LoomX.Plugin.Storage.Sqlite");
        Assert.NotNull(type);
        var result = (PluginSqlException)type.GetMethod("FromCode", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [code])!;
        Assert.Equal(expected, result.Error); Assert.Equal(code, result.SqliteErrorCode); Assert.Null(result.InnerException);
        Assert.DoesNotContain(Root, result.Message);
    }

    [Fact]
    public async Task 首审I1_RETURNING提交Busy不得先返回成功且失败后连接可用()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("returning-lock.db");
        await db.ExecuteAsync(Sql("PRAGMA journal_mode=DELETE"));
        await db.ExecuteAsync(Sql("CREATE TABLE data(x INTEGER PRIMARY KEY)"));
        await db.ExecuteAsync(Sql("INSERT INTO data VALUES(1)"));
        await using var readerConnection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "example.plugin", "returning-lock.db"), Pooling = false, Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Private }.ToString());
        await readerConnection.OpenAsync();
        await using var readerCommand = readerConnection.CreateCommand(); readerCommand.CommandText = "SELECT x FROM data";
        await using var reader = await readerCommand.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        var error = await Error(PluginSqlError.Busy, () => db.QuerySingleAsync(Sql("INSERT INTO data VALUES(2) RETURNING x")).AsTask());
        Assert.True(error.Retryable);
        await reader.DisposeAsync();
        Assert.Equal(1, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
        Assert.Equal(1, await db.ExecuteAsync(Sql("INSERT INTO data VALUES(3)")));
        Assert.Equal(2, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
    }

    [Fact]
    public async Task 首审I1_多行RETURNING单行只缓存首行但写入全部完成()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("returning-many.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        Assert.Equal(10, (await db.QuerySingleAsync(Sql("INSERT INTO data VALUES(10),(20),(30) RETURNING x")))![0].Integer);
        var rows = await db.QueryAsync(Sql("INSERT INTO data VALUES(40),(50),(60) RETURNING x"));
        Assert.Equal(new long[] { 40, 50, 60 }, rows.Select(r => r[0].Integer));
        await using var check = await scope.Storage.OpenSqliteAsync("returning-many.db");
        Assert.Equal(6, (await check.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
    }

    [Theory]
    [InlineData("query_only", "ON", 0, "extra")]
    [InlineData("busy_timeout", "999", 100, "extra")]
    [InlineData("query_only", "ON", 0, "invalid-kind")]
    [InlineData("busy_timeout", "999", 100, "invalid-kind")]
    [InlineData("query_only", "ON", 0, "too-many")]
    [InlineData("busy_timeout", "999", 100, "too-many")]
    public async Task 首审I2_非法PRAGMA参数在真实prepare前拒绝且不改变状态(string pragma, string setting, long original, string scenario)
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("pragma-arguments.db");
        IReadOnlyList<PluginSqlParameter> parameters = scenario == "too-many"
            ? Enumerable.Range(0, 257).Select(i => new PluginSqlParameter("$extra" + i, PluginSqlValue.FromInteger(i))).ToArray()
            : [new("$extra", scenario == "invalid-kind" ? new PluginSqlValue((PluginSqlValueKind)999) : PluginSqlValue.FromInteger(1))];
        await Error(scenario == "too-many" ? PluginSqlError.ResourceLimit : PluginSqlError.InvalidSql, () => db.ExecuteAsync(new("PRAGMA " + pragma + "=" + setting, parameters)).AsTask());
        Assert.Equal(original, (await db.QuerySingleAsync(Sql("PRAGMA " + pragma)))![0].Integer);
        await db.ExecuteAsync(Sql("CREATE TABLE still_writable(x)"));
    }

    [Theory]
    [InlineData("columns")][InlineData("variables")][InlineData("table")]
    public async Task 首审M1_原生列和变量策略上限确定性映射ResourceLimit(string scenario)
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("native-limit.db");
        var sql = scenario switch
        {
            "columns" => "SELECT " + string.Join(",", Enumerable.Range(0, 257).Select(i => "0 AS c" + i)),
            "variables" => "SELECT " + string.Join("+", Enumerable.Range(0, 257).Select(i => "$p" + i)),
            _ => "CREATE TABLE oversized(" + string.Join(",", Enumerable.Range(0, 257).Select(i => "c" + i + " INTEGER")) + ")",
        };
        var parameters = scenario == "variables" ? Enumerable.Range(0, 257).Select(i => new PluginSqlParameter("$p" + i, PluginSqlValue.FromInteger(i))).ToArray() : null;
        await Error(PluginSqlError.ResourceLimit, () => db.QueryAsync(new(sql, parameters)).AsTask());
        var syntax = await Error(PluginSqlError.InvalidSql, () => db.QueryAsync(Sql("SELECT FROM invalid_syntax")).AsTask());
        Assert.Equal(1, syntax.SqliteErrorCode);
        Assert.Equal(1, (await db.QuerySingleAsync(Sql("SELECT 1")))![0].Integer);
        Assert.Equal(0, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM sqlite_master WHERE name='oversized'")))![0].Integer);
    }

    [Fact]
    public async Task 首审M1_边界内256列和变量仍正常执行()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("native-limit-edge.db");
        Assert.Equal(256, (await db.QuerySingleAsync(Sql("SELECT " + string.Join(",", Enumerable.Repeat("1", 256)))))!.Columns.Count);
        var parameters = Enumerable.Range(1, 256).Select(i => new PluginSqlParameter("$p" + i, PluginSqlValue.FromInteger(i))).ToArray();
        Assert.Equal(32896, (await db.QuerySingleAsync(new("SELECT " + string.Join("+", parameters.Select(p => p.Name)), parameters)))![0].Integer);
    }

    [Fact]
    public async Task 并发调用经串行门且返回独立值()
    {
        await using var scope = CreateStorage(); await using var db = await scope.Storage.OpenSqliteAsync("serial.db");
        await db.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => db.ExecuteAsync(new("INSERT INTO data VALUES($i)", [new("$i", PluginSqlValue.FromInteger(i))])).AsTask()));
        Assert.Equal(100, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM data")))![0].Integer);
    }
}

public abstract class SqliteBridgeFixture : IDisposable
{
    protected string Root { get; } = Path.Combine(Path.GetTempPath(), "LoomX-SqliteBridge-" + Guid.NewGuid().ToString("N"));
    protected static PluginSqlCommand Sql(string sql) => new(sql);
    protected StorageScope CreateStorage(string id = "example.plugin", CaptureLogger? logger = null, string? root = null)
    {
        var type = Type.GetType("LoomX.Plugin.Storage.Sqlite.PluginSqliteStorageBridge, LoomX.Plugin.Storage.Sqlite");
        Assert.True(type is not null, "缺少独立 SQLite Bridge 服务；应由宿主实现公共存储契约。");
        var instance = Activator.CreateInstance(type!, [id, root ?? Root, logger is null ? null : logger.For(type!)]);
        Assert.IsAssignableFrom<IPluginStorageBridge>(instance);
        return new((IPluginStorageBridge)instance!, (IAsyncDisposable)instance!);
    }
    protected async Task<PluginSqlException> Error(PluginSqlError error, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PluginSqlException>(action);
        Assert.Equal(error, ex.Error); Assert.Null(ex.InnerException);
        Assert.DoesNotContain(Root, ex.Message); Assert.DoesNotContain("SENSITIVE_SQL_SENTINEL", ex.Message);
        return ex;
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    protected sealed record StorageScope(IPluginStorageBridge Storage, IAsyncDisposable Owner) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Owner.DisposeAsync(); }
    public sealed class CaptureLogger
    {
        public List<string> Messages { get; } = [];
        public List<LogLevel> Levels { get; } = [];
        public object For(Type owner) => Activator.CreateInstance(typeof(Adapter<>).MakeGenericType(owner), this)!;
        public sealed class Adapter<T>(CaptureLogger sink) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { lock (sink.Messages) { sink.Messages.Add(formatter(state, exception)); sink.Levels.Add(level); Assert.Null(exception); } }
        }
    }
}
