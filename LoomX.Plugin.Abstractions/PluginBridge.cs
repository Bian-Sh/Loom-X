namespace LoomX.Plugins;

/// <summary>插件只经公共能力契约调用宿主；本接口不是进程安全沙箱。</summary>
public interface IPluginBridge
{
    IPluginStorageBridge Storage { get; }
    IPluginLogBridge Log { get; }
    IPluginNotificationBridge Notifications { get; }
}

public interface IPluginStorageBridge
{
    ValueTask<IPluginSqliteDatabase> OpenSqliteAsync(string relativePath, CancellationToken cancellationToken = default);
}

public interface IPluginSqliteDatabase : IAsyncDisposable
{
    ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<IPluginSqliteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IPluginSqliteTransaction : IAsyncDisposable
{
    ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

public enum PluginSqlValueKind { Null, Integer, Real, Text, Blob }

/// <summary>可确定性序列化的 SQLite 值；禁止任意 object 载荷。</summary>
public sealed record PluginSqlValue(
    PluginSqlValueKind Kind,
    long Integer = 0,
    double Real = 0,
    string? Text = null,
    byte[]? Blob = null)
{
    public static PluginSqlValue Null { get; } = new(PluginSqlValueKind.Null);
    public static PluginSqlValue FromInteger(long value) => new(PluginSqlValueKind.Integer, Integer: value);
    public static PluginSqlValue FromReal(double value) => new(PluginSqlValueKind.Real, Real: value);
    public static PluginSqlValue FromText(string value) => new(PluginSqlValueKind.Text, Text: value);
    public static PluginSqlValue FromBlob(byte[] value) => new(PluginSqlValueKind.Blob, Blob: value);
}

public sealed record PluginSqlParameter(string Name, PluginSqlValue Value);
public sealed record PluginSqlCommand(string Text, IReadOnlyList<PluginSqlParameter>? Parameters = null);

/// <summary>保留列顺序，重复列名应按索引读取。</summary>
public sealed record PluginSqlRow(IReadOnlyList<string> Columns, IReadOnlyList<PluginSqlValue> Values)
{
    public PluginSqlValue this[int index] => Values[index];
    public PluginSqlValue this[string name]
    {
        get
        {
            for (var index = 0; index < Columns.Count; index++)
                if (string.Equals(Columns[index], name, StringComparison.OrdinalIgnoreCase)) return Values[index];
            throw new KeyNotFoundException("查询结果不包含所需列。");
        }
    }
}

public enum PluginSqlError
{
    ConstraintViolation, Busy, Locked, Corrupt, ReadOnly, PermissionDenied, InvalidPath,
    InvalidSql, Cancelled, ResourceLimit, Unknown, Disposed, TransactionActive,
}

/// <summary>稳定安全错误，不附带原 SQL、参数、路径或提供程序异常对象。</summary>
public sealed class PluginSqlException(PluginSqlError error, string message, int? sqliteErrorCode = null) : Exception(message)
{
    public PluginSqlError Error { get; } = error;
    public int? SqliteErrorCode { get; } = sqliteErrorCode;
    public bool Retryable => Error is PluginSqlError.Busy or PluginSqlError.Locked;
}

public enum PluginBridgeError
{
    CapabilityNotDeclared, CapabilityNotSupported, InvalidArgument, ResourceLimit, Disposed, OperationFailed,
}

public sealed class PluginBridgeException(PluginBridgeError error, string message) : Exception(message)
{
    public PluginBridgeError Error { get; } = error;
}

public enum PluginLogLevel { Debug, Information, Warning, Error }
public enum PluginLogValueKind { Null, Boolean, Integer, Real, Text }
public sealed record PluginLogValue(
    PluginLogValueKind Kind, bool Boolean = false, long Integer = 0, double Real = 0, string? Text = null);
public sealed record PluginLogField(string Name, PluginLogValue Value);
public sealed record PluginLogEntry(PluginLogLevel Level, string MessageTemplate, IReadOnlyList<PluginLogField>? Fields = null);

public interface IPluginLogBridge
{
    ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default);
}

public enum PluginNotificationLevel { Success, Info, Warning, Error }
public sealed record PluginNotification(PluginNotificationLevel Level, string Message);

public interface IPluginNotificationBridge
{
    ValueTask ShowAsync(PluginNotification notification, CancellationToken cancellationToken = default);
}

public enum PluginDiagnosticStage
{
    Manifest, SourceRead, Compile, Cache, Capability, Load, Initialize, Bridge, Test, Reload,
}
public enum PluginDiagnosticSeverity { Information, Warning, Error }
public sealed record PluginDiagnostic(
    string PluginId, PluginDiagnosticStage Stage, PluginDiagnosticSeverity Severity,
    string Code, string Message, string? RelativePath = null, int? Line = null, int? Column = null);

public static class PluginDiagnosticCodes
{
    public const string CapabilityNotSupported = "LX_CAPABILITY_UNSUPPORTED";
    public const string InvalidManifest = "LX_MANIFEST_INVALID";
    public const string InvalidPath = "LX_PATH_INVALID";
    public const string CompilationFailed = "LX_COMPILE_FAILED";
    public const string InvalidCache = "LX_CACHE_INVALID";
    public const string InvalidEntry = "LX_ENTRY_INVALID";
    public const string InitializationFailed = "LX_INITIALIZE_FAILED";
    public const string ReloadFailed = "LX_RELOAD_FAILED";
}

/// <summary>旧初始化构造没有隐式授权；读取属性可用，但未声明调用确定性拒绝。</summary>
public static class PluginBridgeDefaults
{
    public static IPluginBridge Unavailable { get; } = new UnavailableBridge();

    private sealed class UnavailableBridge : IPluginBridge, IPluginStorageBridge, IPluginLogBridge, IPluginNotificationBridge
    {
        public IPluginStorageBridge Storage => this;
        public IPluginLogBridge Log => this;
        public IPluginNotificationBridge Notifications => this;
        private static PluginBridgeException Denied() => new(PluginBridgeError.CapabilityNotDeclared, "插件未声明所需宿主能力。");
        public ValueTask<IPluginSqliteDatabase> OpenSqliteAsync(string relativePath, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IPluginSqliteDatabase>(Denied());
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.FromException(Denied());
        public ValueTask ShowAsync(PluginNotification notification, CancellationToken cancellationToken = default) => ValueTask.FromException(Denied());
    }
}
