using LoomX.Plugins;
using Microsoft.Data.Sqlite;

namespace LoomX.Plugin.Storage.Sqlite;

internal static class PluginSqliteErrors
{
    internal static PluginSqlException Create(PluginSqlError error, int? code = null) => new(error, error switch
    {
        PluginSqlError.InvalidPath => "插件数据库路径不合法。",
        PluginSqlError.InvalidSql => "插件数据库命令不合法或不被允许。",
        PluginSqlError.Disposed => "插件数据库资源已释放或事务已完成。",
        PluginSqlError.TransactionActive => "数据库存在活动事务，请使用所属事务句柄。",
        PluginSqlError.Cancelled => "插件数据库操作已取消。",
        PluginSqlError.ResourceLimit => "插件数据库操作超出资源上限。",
        PluginSqlError.ConstraintViolation => "插件数据库操作违反数据约束。",
        PluginSqlError.Busy or PluginSqlError.Locked => "插件数据库暂时被占用。",
        PluginSqlError.Corrupt => "插件数据库无法读取或已损坏。",
        PluginSqlError.ReadOnly => "插件数据库为只读状态。",
        PluginSqlError.PermissionDenied => "插件数据库访问被拒绝。",
        _ => "插件数据库操作失败。",
    }, code);

    internal static PluginSqlException FromCode(int code) => Create((code & 255) switch
    {
        1 or 21 or 25 => PluginSqlError.InvalidSql,
        3 or 14 or 23 => PluginSqlError.PermissionDenied,
        5 => PluginSqlError.Busy,
        6 => PluginSqlError.Locked,
        7 or 13 or 18 => PluginSqlError.ResourceLimit,
        8 => PluginSqlError.ReadOnly,
        9 => PluginSqlError.Cancelled,
        11 or 26 => PluginSqlError.Corrupt,
        19 => PluginSqlError.ConstraintViolation,
        _ => PluginSqlError.Unknown,
    }, code);

    internal static PluginSqlException Map(Exception exception) => exception switch
    {
        PluginSqlException safe => safe,
        OperationCanceledException => Create(PluginSqlError.Cancelled),
        SqliteException sqlite => FromCode(sqlite.SqliteExtendedErrorCode),
        UnauthorizedAccessException or System.Security.SecurityException => Create(PluginSqlError.PermissionDenied),
        ObjectDisposedException => Create(PluginSqlError.Disposed),
        ArgumentException or NotSupportedException or PathTooLongException => Create(PluginSqlError.InvalidPath),
        OutOfMemoryException => Create(PluginSqlError.ResourceLimit),
        _ => Create(PluginSqlError.Unknown),
    };
}
