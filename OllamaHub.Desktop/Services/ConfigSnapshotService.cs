using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OllamaHub.Configuration;

namespace OllamaHub.Desktop.Services;

public sealed class ConfigSnapshotService
{
    private readonly string databasePath = AppDataPaths.DatabasePath;
    private readonly ILogger<ConfigSnapshotService>? logger;
    private readonly FileSystemWatcher? databaseWatcher;

    public ConfigSnapshotService(ILogger<ConfigSnapshotService>? logger = null)
    {
        this.logger = logger;
        AppDataPaths.EnsureCreated();
        if (logger is not null)
        {
            databaseWatcher = new FileSystemWatcher(AppDataPaths.RootDirectory, Path.GetFileName(databasePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            databaseWatcher.Changed += OnDatabaseFileChanged;
            databaseWatcher.Created += OnDatabaseFileChanged;
            databaseWatcher.Renamed += OnDatabaseFileChanged;
        }
        LogRuntimeContext("配置服务创建");
    }

    private void OnDatabaseFileChanged(object sender, FileSystemEventArgs args)
    {
        logger?.LogInformation("配置库主文件发生变化，变更类型 {ChangeType}，路径 {FilePath}", args.ChangeType, args.FullPath);
        LogFileState("主库变更后", databasePath);
    }

    public ResolvedAppConfig Load()
    {
        try
        {
            LogRuntimeContext("配置快照同步读取开始");
            using var db = new ConfigurationDbContext(CreateReadOnlyOptions());
            EnsureDatabaseReadyForReadAsync(db).GetAwaiter().GetResult();
            LogDatabaseState(db, "配置数据库初始化完成");
            var provider = new DatabaseConfigurationProvider(db, CreateProviderLogger());
            provider.ReloadAsync().GetAwaiter().GetResult();
            var config = provider.Current;
            logger?.LogInformation("配置快照同步读取完成，Provider {ProviderCount}，模型 {ModelCount}，Endpoint {EndpointCount}", config.Providers.Count, config.Models.Count, config.GatewayEndpoints.Count);
            return config;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "配置快照同步读取失败");
            throw;
        }
    }

    public async Task<IReadOnlyList<ProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LogRuntimeContext("Provider 列表读取开始");
            await using var db = CreateContext(CreateReadOnlyOptions());
            await EnsureDatabaseReadyForReadAsync(db, cancellationToken);
            LogDatabaseState(db, "Provider 列表数据库初始化完成");
            var rawProviderCount = await db.Providers.AsNoTracking().CountAsync(cancellationToken);
            var rawModelCount = await db.Models.AsNoTracking().CountAsync(cancellationToken);
            var rawProviders = await db.Providers.AsNoTracking().OrderBy(item => item.SortOrder).Select(item => new { item.BusinessId, item.Enabled, ModelCount = item.Models.Count }).ToArrayAsync(cancellationToken);
            logger?.LogInformation("Provider 原始行摘要 {ProviderRows}", string.Join("; ", rawProviders.Select(item => $"{item.BusinessId}({item.Enabled},{item.ModelCount})")));
            var provider = new DatabaseConfigurationProvider(db, CreateProviderLogger());
            await provider.ReloadAsync(cancellationToken);
            var result = await new ConfigurationManagementService(new DesktopDbContextFactory(CreateReadOnlyOptions()), provider).ListProvidersAsync(cancellationToken);
            logger?.LogInformation("Provider 列表读取完成，原始 Provider {RawProviderCount}，原始模型 {RawModelCount}，快照 Provider {SnapshotProviderCount}，返回 Provider {ReturnedProviderCount}", rawProviderCount, rawModelCount, provider.Current.Providers.Count, result.Count);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Provider 列表读取已取消");
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Provider 列表读取失败");
            throw;
        }
    }

    public async Task<AppSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(CreateReadOnlyOptions());
        await EnsureDatabaseReadyForReadAsync(db, cancellationToken);
        var provider = new DatabaseConfigurationProvider(db, CreateProviderLogger());
        await provider.ReloadAsync(cancellationToken);
        return await new ConfigurationManagementService(new DesktopDbContextFactory(CreateReadOnlyOptions()), provider).GetSettingsAsync(cancellationToken);
    }

    public Task<AppSettingsResponse> UpdateSettingsAsync(AppSettingsInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateSettingsAsync(input, token), cancellationToken);

    public Task<ProviderResponse> CreateProviderAsync(ProviderInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateProviderAsync(input, token), cancellationToken);
    public Task<ProviderResponse> UpdateProviderAsync(Guid id, ProviderInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateProviderAsync(id, input, token), cancellationToken);
    public Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteProviderAsync(id, token); return true; }, cancellationToken);
    public Task<ModelResponse> CreateModelAsync(Guid providerId, ModelInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateModelAsync(providerId, input, token), cancellationToken);
    public Task<ModelResponse> UpdateModelAsync(Guid id, ModelInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateModelAsync(id, input, token), cancellationToken);
    public Task DeleteModelAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteModelAsync(id, token); return true; }, cancellationToken);
    public async Task<IReadOnlyList<GatewayModelSourceResponse>> ListEnabledGatewayModelsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        return await db.Models.AsNoTracking()
            .Where(model => model.Enabled && model.Provider.Enabled)
            .OrderBy(model => model.Provider.SortOrder)
            .ThenBy(model => model.SortOrder)
            .ThenBy(model => model.ModelId)
            .Select(model => new GatewayModelSourceResponse(model.Id, model.DisplayName, model.Provider.DisplayName))
            .ToArrayAsync(cancellationToken);
    }
    public Task<IReadOnlyList<GatewayEndpointResponse>> ListGatewayEndpointsAsync(CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.ListGatewayEndpointsAsync(token), cancellationToken);
    public Task<GatewayEndpointResponse> SetGatewayEndpointEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.SetGatewayEndpointEnabledAsync(key, enabled, token), cancellationToken);
    public Task<GatewayComboResponse> CreateGatewayComboAsync(string endpointKey, GatewayComboInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateGatewayComboAsync(endpointKey, input, token), cancellationToken);
    public Task<GatewayComboResponse> UpdateGatewayComboAsync(Guid id, GatewayComboInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateGatewayComboAsync(id, input, token), cancellationToken);
    public Task DeleteGatewayComboAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteGatewayComboAsync(id, token); return true; }, cancellationToken);
    public Task<GatewayRouteResponse> CreateGatewayRouteAsync(Guid comboId, GatewayRouteInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.CreateGatewayRouteAsync(comboId, input, token), cancellationToken);
    public Task<GatewayRouteResponse> UpdateGatewayRouteAsync(Guid id, GatewayRouteInput input, CancellationToken cancellationToken = default) => ExecuteManagementAsync((service, token) => service.UpdateGatewayRouteAsync(id, input, token), cancellationToken);
    public Task DeleteGatewayRouteAsync(Guid id, CancellationToken cancellationToken = default) => ExecuteManagementAsync(async (service, token) => { await service.DeleteGatewayRouteAsync(id, token); return true; }, cancellationToken);

    private async Task<TResult> ExecuteManagementAsync<TResult>(Func<ConfigurationManagementService, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        await using var probe = CreateContext(CreateReadOnlyOptions());
        await EnsureDatabaseReadyForReadAsync(probe, cancellationToken);
        using var initializationLock = ConfigurationDatabase.AcquireInitializationLock();
        await using var db = CreateContext();
        var provider = new DatabaseConfigurationProvider(db, CreateProviderLogger());
        await provider.ReloadAsync(cancellationToken);
        var service = new ConfigurationManagementService(new DesktopDbContextFactory(CreateOptions()), provider);
        return await operation(service, cancellationToken);
    }

    private ConfigurationDbContext CreateContext(DbContextOptions<ConfigurationDbContext>? options = null) => new(options ?? CreateOptions());
    private DbContextOptions<ConfigurationDbContext> CreateOptions()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        return new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite(connectionString).Options;
    }

    private DbContextOptions<ConfigurationDbContext> CreateReadOnlyOptions()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        return new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite(connectionString).Options;
    }
    private ILogger<DatabaseConfigurationProvider>? CreateProviderLogger() => logger is null ? null : new ProviderLoggerAdapter(logger);

    private async Task EnsureDatabaseReadyForReadAsync(ConfigurationDbContext db, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            logger?.LogInformation("配置库不存在，执行首次初始化 {DatabasePath}", databasePath);
            await using var writableDb = new ConfigurationDbContext(CreateOptions());
            await ConfigurationDatabase.InitializeAsync(writableDb, cancellationToken);
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('AppSettings', 'GatewayConfigurations', 'Providers', 'Models')";
            var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            await using var columnCommand = db.Database.GetDbConnection().CreateCommand();
            columnCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AppSettings') WHERE name IN ('TransparencyEnabled', 'TransparencyOpacity', 'BlurAmount', 'TransparencyAlgorithm')";
            var appearanceColumnCount = Convert.ToInt32(await columnCommand.ExecuteScalarAsync(cancellationToken));
            logger?.LogInformation("配置库只读预检完成，必要表 {RequiredTableCount}/4，外观字段 {AppearanceColumnCount}/4", tableCount, appearanceColumnCount);
            if (tableCount < 4 || appearanceColumnCount < 4)
            {
                logger?.LogWarning("配置库需要结构迁移，必要表 {RequiredTableCount}/4，外观字段 {AppearanceColumnCount}/4，执行初始化 {DatabasePath}", tableCount, appearanceColumnCount, databasePath);
                await db.Database.CloseConnectionAsync();
                await using var writableDb = new ConfigurationDbContext(CreateOptions());
                await ConfigurationDatabase.InitializeAsync(writableDb, cancellationToken);
            }
        }
        finally
        {
            if (db.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                await db.Database.CloseConnectionAsync();
        }
    }

    private void LogRuntimeContext(string operation)
    {
        if (logger is null) return;
        var process = Process.GetCurrentProcess();
        logger.LogInformation("{Operation}，进程 {ProcessId}，用户 {UserName}，会话 {SessionId}，进程路径 {ProcessPath}，基目录 {BaseDirectory}，工作目录 {CurrentDirectory}，配置库 {DatabasePath}", operation, Environment.ProcessId, Environment.UserName, process.SessionId, Environment.ProcessPath, AppContext.BaseDirectory, Environment.CurrentDirectory, databasePath);
        LogFileState("主库", databasePath);
        LogFileState("WAL", databasePath + "-wal");
        LogFileState("共享内存", databasePath + "-shm");
    }

    private void LogFileState(string label, string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                logger?.LogInformation("配置库文件状态 {FileLabel}，存在 False，路径 {FilePath}", label, path);
                return;
            }
            using var stream = file.OpenRead();
            var fingerprint = Convert.ToHexString(SHA256.HashData(stream))[..16];
            logger?.LogInformation("配置库文件状态 {FileLabel}，存在 True，大小 {Length} 字节，最后写入 UTC {LastWriteTimeUtc}，SHA256 前缀 {Fingerprint}", label, file.Length, file.LastWriteTimeUtc, fingerprint);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "读取配置库文件状态失败 {FileLabel} {FilePath}，异常类型 {ExceptionType}，异常消息 {ExceptionMessage}", label, path, exception.GetType().FullName, exception.Message);
        }
    }

    private void LogDatabaseState(ConfigurationDbContext db, string operation)
    {
        if (logger is null) return;
        try
        {
            var connection = db.Database.GetDbConnection();
            db.Database.OpenConnection();
            using var journalCommand = connection.CreateCommand();
            journalCommand.CommandText = "PRAGMA journal_mode";
            var journalMode = journalCommand.ExecuteScalar()?.ToString() ?? "未知";
            using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "PRAGMA schema_version";
            var schemaVersion = schemaCommand.ExecuteScalar()?.ToString() ?? "未知";
            logger.LogInformation("{Operation}，journal_mode {JournalMode}，schema_version {SchemaVersion}", operation, journalMode, schemaVersion);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取 SQLite 状态失败 {DatabasePath}，异常类型 {ExceptionType}，异常消息 {ExceptionMessage}", databasePath, exception.GetType().FullName, exception.Message);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    private sealed class ProviderLoggerAdapter(ILogger<ConfigSnapshotService> inner) : ILogger<DatabaseConfigurationProvider>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => inner.Log(logLevel, eventId, state, exception, formatter);
    }

    public void Dispose()
    {
        if (databaseWatcher is null) return;
        databaseWatcher.EnableRaisingEvents = false;
        databaseWatcher.Changed -= OnDatabaseFileChanged;
        databaseWatcher.Created -= OnDatabaseFileChanged;
        databaseWatcher.Renamed -= OnDatabaseFileChanged;
        databaseWatcher.Dispose();
    }
}

file sealed class DesktopDbContextFactory(DbContextOptions<ConfigurationDbContext> options) : IDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext() => new(options);
    public Task<ConfigurationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ConfigurationDbContext(options));
}
