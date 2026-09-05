using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LoomX;

public interface IApplicationDataMigration
{
    Task EnsureMigratedAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationDataMigration : IApplicationDataMigration
{
    private static readonly string[] ConfigurationTables = ["AppSettings", "GatewayConfigurations", "Providers", "Models"];
    private static readonly string[] ActivityTables = ["Events"];
    private readonly string legacyRoot;
    private readonly string targetRoot;
    private readonly ILogger<ApplicationDataMigration> logger;

    public ApplicationDataMigration(ILogger<ApplicationDataMigration> logger)
        : this(AppDataPaths.LegacyRootDirectory, AppDataPaths.RootDirectory, logger)
    {
    }

    internal ApplicationDataMigration(
        string legacyRoot,
        string targetRoot,
        ILogger<ApplicationDataMigration> logger)
    {
        this.legacyRoot = Path.GetFullPath(legacyRoot);
        this.targetRoot = Path.GetFullPath(targetRoot);
        this.logger = logger;
    }

    public async Task EnsureMigratedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetRoot);
        await using var migrationLock = await AcquireMigrationLockAsync(cancellationToken);
        CleanupTemporaryFiles();

        await MigrateFileAsync(
            Path.Combine(legacyRoot, "OllamaHub.db"),
            Path.Combine(targetRoot, "LoomX.db"),
            "配置库",
            ConfigurationTables,
            cancellationToken);
        await MigrateFileAsync(
            Path.Combine(legacyRoot, "Activity.db"),
            Path.Combine(targetRoot, "LoomX.Activity.db"),
            "活动库",
            ActivityTables,
            cancellationToken);
    }

    private async Task MigrateFileAsync(
        string sourcePath,
        string targetPath,
        string databaseKind,
        IReadOnlyList<string> requiredTables,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            logger.LogInformation("目标{DatabaseKind}已存在，跳过旧库迁移 {TargetPath}", databaseKind, targetPath);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            logger.LogInformation("未找到旧{DatabaseKind}，跳过迁移 {SourcePath}", databaseKind, sourcePath);
            return;
        }

        var temporaryPath = Path.Combine(
            targetRoot,
            $"{Path.GetFileName(targetPath)}.migrating.{Guid.NewGuid():N}.tmp");
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("开始迁移{DatabaseKind} {SourcePath} -> {TargetPath}", databaseKind, sourcePath, targetPath);

        try
        {
            try
            {
                await CreateSnapshotAsync(sourcePath, temporaryPath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ApplicationDataMigrationException(databaseKind, "快照", sourcePath, targetPath, exception);
            }

            try
            {
                await ValidateSnapshotAsync(temporaryPath, requiredTables, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ApplicationDataMigrationException(databaseKind, "完整性检查", sourcePath, targetPath, exception);
            }

            if (File.Exists(targetPath))
            {
                TryDeleteTemporaryFile(temporaryPath, databaseKind);
                logger.LogInformation("迁移期间目标{DatabaseKind}已出现，保留已有目标 {TargetPath}", databaseKind, targetPath);
                return;
            }

            try
            {
                File.Move(temporaryPath, targetPath);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ApplicationDataMigrationException(databaseKind, "原子提交", sourcePath, targetPath, exception);
            }

            logger.LogInformation(
                "迁移{DatabaseKind}完成 {SourcePath} -> {TargetPath}，源大小 {SourceBytes} 字节，目标大小 {TargetBytes} 字节，耗时 {ElapsedMs}ms",
                databaseKind,
                sourcePath,
                targetPath,
                new FileInfo(sourcePath).Length,
                new FileInfo(targetPath).Length,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporaryFile(temporaryPath, databaseKind);
            throw;
        }
        catch (ApplicationDataMigrationException exception)
        {
            TryDeleteTemporaryFile(temporaryPath, databaseKind);
            logger.LogError(
                exception,
                "应用数据迁移失败 {DatabaseKind} {Stage} {SourcePath} -> {TargetPath}",
                exception.DatabaseKind,
                exception.Stage,
                sourcePath,
                targetPath);
            throw;
        }
        catch (Exception exception)
        {
            TryDeleteTemporaryFile(temporaryPath, databaseKind);
            logger.LogError(
                exception,
                "应用数据迁移失败 {DatabaseKind} {Stage} {SourcePath} -> {TargetPath}",
                databaseKind,
                "快照或提交",
                sourcePath,
                targetPath);
            throw new ApplicationDataMigrationException(databaseKind, "快照或提交", sourcePath, targetPath, exception);
        }
    }

    private static async Task CreateSnapshotAsync(string sourcePath, string temporaryPath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(CreateReadOnlyConnectionString(sourcePath));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"VACUUM INTO '{EscapeSqliteLiteral(temporaryPath)}'";
        command.CommandTimeout = 5;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateSnapshotAsync(
        string temporaryPath,
        IReadOnlyList<string> requiredTables,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            throw new InvalidDataException("SQLite 临时快照为空。");

        await using var connection = new SqliteConnection(CreateReadOnlyConnectionString(temporaryPath));
        await connection.OpenAsync(cancellationToken);

        await using (var integrityCommand = connection.CreateCommand())
        {
            integrityCommand.CommandText = "PRAGMA integrity_check";
            var result = Convert.ToString(await integrityCommand.ExecuteScalarAsync(cancellationToken));
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SQLite 完整性检查未通过。");
        }

        foreach (var table in requiredTables)
        {
            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table)";
            tableCommand.Parameters.AddWithValue("$table", table);
            if (Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new InvalidDataException($"SQLite 必要表缺失：{table}。");
        }
    }

    private async Task<FileStream> AcquireMigrationLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(targetRoot, "LoomX.data-migration.lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("应用数据迁移锁等待超时。");
        }
    }

    private void CleanupTemporaryFiles()
    {
        if (!Directory.Exists(targetRoot)) return;
        foreach (var targetName in new[] { "LoomX.db", "LoomX.Activity.db" })
        {
            foreach (var path in Directory.EnumerateFiles(targetRoot, $"{targetName}.migrating.*.tmp", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(path);
                    logger.LogInformation("清理未完成的{DatabaseKind}临时快照 {TemporaryPath}", targetName == "LoomX.db" ? "配置库" : "活动库", path);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "清理迁移临时文件失败 {TemporaryPath}", path);
                }
            }
        }
    }

    private void TryDeleteTemporaryFile(string path, string databaseKind)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception exception) { logger.LogWarning(exception, "清理{DatabaseKind}临时文件失败 {TemporaryPath}", databaseKind, path); }
    }

    private static string CreateReadOnlyConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Private,
        Pooling = false,
        DefaultTimeout = 5
    }.ToString();

    private static string EscapeSqliteLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class ApplicationDataMigrationException : InvalidOperationException
{
    public ApplicationDataMigrationException(
        string databaseKind,
        string stage,
        string sourcePath,
        string targetPath,
        Exception innerException)
        : base($"{databaseKind}迁移失败，阶段：{stage}。请关闭旧版应用并检查数据目录权限。", innerException)
    {
        DatabaseKind = databaseKind;
        Stage = stage;
        SourcePath = sourcePath;
        TargetPath = targetPath;
    }

    public string DatabaseKind { get; }
    public string Stage { get; }
    public string SourcePath { get; }
    public string TargetPath { get; }
}
