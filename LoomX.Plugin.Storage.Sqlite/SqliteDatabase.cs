using System.Text;
using LoomX.Plugins;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace LoomX.Plugin.Storage.Sqlite;

internal sealed class SqliteDatabase : IPluginSqliteDatabase
{
    internal const long MaximumDatabaseBytes = 256L * 1024 * 1024;
    internal const int MaximumValueBytes = 1024 * 1024;
    internal const int MaximumResultBytes = 8 * 1024 * 1024;
    internal const int MaximumRows = 10000;
    private const int MaximumSqlBytes = 256 * 1024;
    private readonly PluginSqliteStorageBridge owner;
    private readonly PluginStoragePaths paths;
    private readonly string path;
    private readonly SqliteConnection connection;
    private readonly sqlite3 handle;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly strdelegate_authorizer authorizer;
    private readonly delegate_progress progress;
    private CancellationToken operationToken;
    private SqliteTransaction? active;
    private bool internalControl;
    private bool policyDenied;
    private bool suppressPragma;
    private bool sawPragma;
    private bool disposed;
    private static readonly HashSet<string> SafePragmas = new(StringComparer.OrdinalIgnoreCase)
    {
        "journal_mode", "synchronous", "busy_timeout", "user_version", "application_id", "foreign_keys", "defer_foreign_keys", "query_only",
        "table_info", "table_xinfo", "table_list", "index_info", "index_xinfo", "index_list", "foreign_key_list", "foreign_key_check",
        "integrity_check", "quick_check", "page_count", "freelist_count", "schema_version", "encoding", "compile_options", "database_list",
    };

    internal SqliteDatabase(PluginSqliteStorageBridge owner, PluginStoragePaths paths, string path)
    {
        this.owner = owner; this.paths = paths; this.path = path;
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, Cache = SqliteCacheMode.Private, DefaultTimeout = 1 }.ToString());
        authorizer = Authorize;
        progress = _ => operationToken.IsCancellationRequested ? 1 : 0;
        try
        {
            connection.Open();
            handle = connection.Handle!;
            Check(raw.sqlite3_enable_load_extension(handle, 0));
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LENGTH, MaximumValueBytes);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_SQL_LENGTH, MaximumSqlBytes);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COLUMN, 256);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_VARIABLE_NUMBER, 256);
            Check(raw.sqlite3_busy_timeout(handle, 100));
            // 内部固定磁盘上限，插件不能通过 PRAGMA 取消；不改既有库名、表或内容。
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA page_size";
                var pageSize = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                command.CommandText = "PRAGMA max_page_count=" + (MaximumDatabaseBytes / pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
                command.ExecuteNonQuery();
            }
            Check(raw.sqlite3_set_authorizer(handle, authorizer, null));
            raw.sqlite3_progress_handler(handle, 1000, progress, null);
        }
        catch { connection.Dispose(); throw; }
    }

    public ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => Execute(command, null, cancellationToken);
    public ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => QuerySingle(command, null, cancellationToken);
    public ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => Query(command, null, cancellationToken);
    internal ValueTask<int> Execute(PluginSqlCommand command, SqliteTransaction? transaction, CancellationToken token) => Run("执行", transaction, token, () =>
    {
        var before = raw.sqlite3_total_changes(handle);
        Evaluate(command, false, false);
        return raw.sqlite3_total_changes(handle) == before ? 0 : raw.sqlite3_changes(handle);
    });
    internal ValueTask<PluginSqlRow?> QuerySingle(PluginSqlCommand command, SqliteTransaction? transaction, CancellationToken token) => Run("单行查询", transaction, token, () => Evaluate(command, true, true).FirstOrDefault());
    internal ValueTask<IReadOnlyList<PluginSqlRow>> Query(PluginSqlCommand command, SqliteTransaction? transaction, CancellationToken token) => Run<IReadOnlyList<PluginSqlRow>>("查询", transaction, token, () => Evaluate(command, true, false));

    public ValueTask<IPluginSqliteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Run<IPluginSqliteTransaction>("开始事务", null, cancellationToken, () =>
    {
        Control("BEGIN");
        active = new SqliteTransaction(this);
        return active;
    });

    internal async ValueTask Finish(SqliteTransaction transaction, bool commit, CancellationToken token)
    {
        await Run("结束事务", transaction, token, () =>
        {
            Control(commit ? "COMMIT" : "ROLLBACK");
            transaction.Completed = true;
            active = null;
            return 0;
        }).ConfigureAwait(false);
    }

    private async ValueTask<T> Run<T>(string operation, SqliteTransaction? transaction, CancellationToken token, Func<T> action)
    {
        var entered = false;
        owner.LogStart(operation);
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false); entered = true;
            if (disposed || transaction?.Completed == true) throw PluginSqliteErrors.Create(PluginSqlError.Disposed);
            if (!ReferenceEquals(active, transaction)) throw PluginSqliteErrors.Create(PluginSqlError.TransactionActive);
            token.ThrowIfCancellationRequested();
            operationToken = token;
            var result = await Task.Run(() =>
            {
                paths.Validate(path);
                token.ThrowIfCancellationRequested();
                // 注册范围完全位于门内；释放注册后才允许关闭连接或运行下个命令。
                using var registration = token.Register(() => raw.sqlite3_interrupt(handle));
                return action();
            }, CancellationToken.None).ConfigureAwait(false);
            owner.LogComplete(operation);
            return result;
        }
        catch (Exception ex) { var safe = PluginSqliteErrors.Map(ex); owner.LogFailure(operation, safe); throw safe; }
        finally
        {
            if (entered)
            {
                // SQLITE_INTERRUPT、FULL、IOERR 等可能让 SQLite 自行结束事务，不能保留幽灵句柄。
                if (!disposed && active is not null && raw.sqlite3_get_autocommit(handle) != 0)
                {
                    active.Completed = true; active = null;
                    owner.LogFailure("原生结束事务", PluginSqliteErrors.Create(PluginSqlError.Disposed));
                }
                operationToken = default; gate.Release();
            }
        }
    }

    private List<PluginSqlRow> Evaluate(PluginSqlCommand command, bool collect, bool single)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Text) || command.Text.Contains('\0')) throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql);
        if (Encoding.UTF8.GetByteCount(command.Text) > MaximumSqlBytes) throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
        using (var statement = Prepare(command.Text, command.Parameters?.ToArray()))
        {
            int code;
            var readOnly = raw.sqlite3_stmt_readonly(statement) != 0;
            var count = raw.sqlite3_column_count(statement);
            var columns = Enumerable.Range(0, count).Select(i => raw.sqlite3_column_name(statement, i).utf8_to_string()).ToArray();
            long total = columns.Sum(c => (long)Encoding.UTF8.GetByteCount(c));
            var rows = new List<PluginSqlRow>();
            while (true)
            {
                operationToken.ThrowIfCancellationRequested();
                code = raw.sqlite3_step(statement);
                if (code == raw.SQLITE_DONE) break;
                if (code != raw.SQLITE_ROW) { Check(code); break; }
                if (!collect || (single && rows.Count != 0)) continue;
                if (rows.Count >= MaximumRows) throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
                var values = new PluginSqlValue[count];
                for (var i = 0; i < count; i++)
                {
                    var kind = raw.sqlite3_column_type(statement, i);
                    var bytes = kind is raw.SQLITE_TEXT or raw.SQLITE_BLOB ? raw.sqlite3_column_bytes(statement, i) : 8;
                    total += bytes;
                    if (bytes > MaximumValueBytes || total > MaximumResultBytes) throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
                    values[i] = kind switch
                    {
                        raw.SQLITE_NULL => PluginSqlValue.Null,
                        raw.SQLITE_INTEGER => PluginSqlValue.FromInteger(raw.sqlite3_column_int64(statement, i)),
                        raw.SQLITE_FLOAT => PluginSqlValue.FromReal(raw.sqlite3_column_double(statement, i)),
                        // 按实际字节数解码，保留 TEXT 中合法的 NUL；不走零结尾转换。
                        raw.SQLITE_TEXT => PluginSqlValue.FromText(Encoding.UTF8.GetString(raw.sqlite3_column_blob(statement, i))),
                        raw.SQLITE_BLOB => PluginSqlValue.FromBlob(raw.sqlite3_column_blob(statement, i).ToArray()),
                        _ => throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql),
                    };
                }
                rows.Add(new(Array.AsReadOnly(columns), Array.AsReadOnly(values)));
                // 写语句的首行不是提交成功，必须消费到 DONE 并检查提交/锁错误。
                if (single && readOnly) break;
            }
            return rows;
        }
    }

    private sqlite3_stmt Prepare(string text, IReadOnlyList<PluginSqlParameter>? parameters)
    {
        policyDenied = false;
        suppressPragma = true; sawPragma = false;
        sqlite3_stmt? statement = null;
        try
        {
            int code; string tail;
            try { code = PrepareInitial(text, out statement, out tail); }
            finally { suppressPragma = false; }
            Check(code);
            if (statement is null || statement.IsInvalid || !OnlyTrivia(tail)) throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql);
            // 首轮 PRAGMA 被 authorizer IGNORE，参数数量/名称/类型/资源校验必须先完成。
            Bind(statement, parameters);
            if (sawPragma)
            {
                // 只有尾部与参数均合法才真实 prepare；参数快照避免两轮读取不同列表。
                statement.Dispose(); statement = null;
                Check(raw.sqlite3_prepare_v2(handle, text, out statement, out tail));
                if (statement is null || statement.IsInvalid || !OnlyTrivia(tail)) throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql);
                Bind(statement, parameters);
            }
            return statement;
        }
        catch { statement?.Dispose(); throw; }
    }

    private int PrepareInitial(string text, out sqlite3_stmt statement, out string tail)
    {
        var code = raw.sqlite3_prepare_v2(handle, text, out statement, out tail);
        if (code != raw.SQLITE_ERROR || policyDenied) return code;
        statement?.Dispose();
        // SQLITE_ERROR 本身不能说明超限。仅改变两个本服务收紧的限额，再做无副作用 prepare。
        // 探测成功才证明触发了策略限额；探测失败仍按原始错误分类，不读取异常正文。
        var columns = raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COLUMN, int.MaxValue);
        var variables = raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_VARIABLE_NUMBER, int.MaxValue);
        sqlite3_stmt? probe = null;
        try
        {
            var probeCode = raw.sqlite3_prepare_v2(handle, text, out probe, out string probeTail);
            if (probeCode == raw.SQLITE_OK && probe is not null && !probe.IsInvalid && OnlyTrivia(probeTail))
                throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit, code);
            return code;
        }
        finally
        {
            probe?.Dispose();
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COLUMN, columns);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_VARIABLE_NUMBER, variables);
        }
    }

    private void Bind(sqlite3_stmt statement, IReadOnlyList<PluginSqlParameter>? parameters)
    {
        var bound = new HashSet<int>();
        long total = 0;
        if (parameters is not null)
        {
            if (parameters.Count > 256) throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
            foreach (var parameter in parameters)
            {
                if (parameter is null || string.IsNullOrEmpty(parameter.Name) || parameter.Name.Contains('\0') || parameter.Name[0] is not ('$' or '@' or ':') || parameter.Value is null)
                    throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql);
                var index = raw.sqlite3_bind_parameter_index(statement, parameter.Name);
                if (index == 0 || !bound.Add(index)) throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql);
                var value = parameter.Value;
                var bytes = value.Kind switch { PluginSqlValueKind.Text when value.Text is not null => Encoding.UTF8.GetByteCount(value.Text), PluginSqlValueKind.Blob when value.Blob is not null => value.Blob.Length, _ => 8 };
                total += bytes;
                if (bytes > MaximumValueBytes || total > MaximumResultBytes) throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
                Check(value.Kind switch
                {
                    PluginSqlValueKind.Null => raw.sqlite3_bind_null(statement, index),
                    PluginSqlValueKind.Integer => raw.sqlite3_bind_int64(statement, index, value.Integer),
                    PluginSqlValueKind.Real when double.IsFinite(value.Real) => raw.sqlite3_bind_double(statement, index, value.Real),
                    PluginSqlValueKind.Text when value.Text is not null => raw.sqlite3_bind_text(statement, index, Encoding.UTF8.GetBytes(value.Text)),
                    PluginSqlValueKind.Blob when value.Blob is not null => raw.sqlite3_bind_blob(statement, index, value.Blob),
                    _ => throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql),
                });
            }
        }
        if (bound.Count != raw.sqlite3_bind_parameter_count(statement)) throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql);
    }

    private int Authorize(object state, int action, string? first, string? second, string? database, string? trigger)
    {
        if (action is raw.SQLITE_ATTACH or raw.SQLITE_DETACH) return Deny();
        if (action is raw.SQLITE_TRANSACTION or raw.SQLITE_SAVEPOINT) return internalControl ? raw.SQLITE_OK : Deny();
        if (action == raw.SQLITE_FUNCTION && (string.Equals(second, "load_extension", StringComparison.OrdinalIgnoreCase) || string.Equals(second, "writefile", StringComparison.OrdinalIgnoreCase) || string.Equals(second, "readfile", StringComparison.OrdinalIgnoreCase))) return Deny();
        // 虚拟表模块可自行读取外部文件；普通表、索引、触发器不受影响。
        if (action is raw.SQLITE_CREATE_VTABLE or raw.SQLITE_DROP_VTABLE) return Deny();
        if (action != raw.SQLITE_PRAGMA) return raw.SQLITE_OK;
        if (first is null || !SafePragmas.Contains(first)) return Deny();
        if (first.Equals("journal_mode", StringComparison.OrdinalIgnoreCase) && second is not null && !new[] { "wal", "delete", "truncate", "persist", "memory" }.Contains(second, StringComparer.OrdinalIgnoreCase)) return Deny();
        if (first.Equals("busy_timeout", StringComparison.OrdinalIgnoreCase) && second is not null && (!int.TryParse(second, out var ms) || ms < 0 || ms > 5000)) return Deny();
        if (second is not null && new[] { "schema_version", "encoding", "page_count", "freelist_count", "compile_options", "database_list" }.Contains(first, StringComparer.OrdinalIgnoreCase)) return Deny();
        sawPragma = true;
        return suppressPragma ? raw.SQLITE_IGNORE : raw.SQLITE_OK;
    }

    private int Deny() { policyDenied = true; return raw.SQLITE_DENY; }

    private static bool OnlyTrivia(string? tail)
    {
        // prepare 已经确定第一条语句的边界，此处仅验证尾部没有第二条语句。
        if (tail is null) return true;
        for (var i = 0; i < tail.Length;)
        {
            if (char.IsWhiteSpace(tail[i])) { i++; continue; }
            if (i + 1 < tail.Length && tail[i] == '-' && tail[i + 1] == '-')
            { i += 2; while (i < tail.Length && tail[i] != '\n') i++; continue; }
            if (i + 1 < tail.Length && tail[i] == '/' && tail[i + 1] == '*')
            { var end = tail.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) return false; i = end + 2; continue; }
            return false;
        }
        return true;
    }

    private void Control(string sql)
    {
        internalControl = true;
        try { Evaluate(new(sql), false, false); }
        finally { internalControl = false; }
    }

    private void Check(int code)
    {
        if (code == raw.SQLITE_OK) return;
        operationToken.ThrowIfCancellationRequested();
        if (policyDenied) throw PluginSqliteErrors.Create(PluginSqlError.InvalidSql, code);
        var extended = raw.sqlite3_extended_errcode(handle);
        throw PluginSqliteErrors.FromCode((extended & 255) == (code & 255) ? extended : code);
    }

    internal async ValueTask DisposeTransaction(SqliteTransaction transaction)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (transaction.Completed) return;
            if (!disposed && ReferenceEquals(active, transaction))
            {
                try { Control("ROLLBACK"); owner.LogComplete("释放事务回滚"); }
                catch (Exception ex) { var safe = PluginSqliteErrors.Map(ex); owner.LogFailure("释放事务回滚", safe); throw safe; }
                finally { transaction.Completed = true; active = null; }
            }
            else transaction.Completed = true;
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (active is not null)
                {
                    try { Control("ROLLBACK"); }
                    finally { active.Completed = true; active = null; }
                }
            }
            catch (Exception ex) { var safe = PluginSqliteErrors.Map(ex); owner.LogFailure("释放数据库回滚", safe); throw safe; }
            finally
            {
                connection.Dispose();
                owner.Remove(this);
                owner.LogComplete("释放数据库");
            }
        }
        finally { gate.Release(); }
    }
}

internal sealed class SqliteTransaction(SqliteDatabase database) : IPluginSqliteTransaction
{
    internal bool Completed { get; set; }
    public ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => database.Execute(command, this, cancellationToken);
    public ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => database.QuerySingle(command, this, cancellationToken);
    public ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default) => database.Query(command, this, cancellationToken);
    public ValueTask CommitAsync(CancellationToken cancellationToken = default) => database.Finish(this, true, cancellationToken);
    public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => database.Finish(this, false, cancellationToken);
    public ValueTask DisposeAsync() => database.DisposeTransaction(this);
}
