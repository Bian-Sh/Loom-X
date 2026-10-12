namespace LoomX.Plugins.Host;

/// <summary>宿主实际支持的精确能力版本，未知版本不得降级。</summary>
public static class PluginCapabilityCatalog
{
    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(new[]
    {
        "storage.sqlite/v1", "logging/v1", "notifications/v1", "protection/v1",
    });

    public static IReadOnlyList<PluginDiagnostic> Validate(string pluginId, IReadOnlyList<string> capabilities) =>
        capabilities.Where(capability => !Supported.Contains(capability, StringComparer.Ordinal))
            .Select(_ => new PluginDiagnostic(pluginId, PluginDiagnosticStage.Capability,
                PluginDiagnosticSeverity.Error, PluginDiagnosticCodes.CapabilityNotSupported,
                "宿主不支持插件声明的必需能力版本。"))
            .ToArray();
}

/// <summary>按插件实例绑定的授权代理；退休后的调用不能重新获取宿主资源。</summary>
public sealed class PluginBridgeScope : IPluginBridge, IPluginStorageBridge, IPluginLogBridge,
    IPluginNotificationBridge, IPluginProtectionBridge, IAsyncDisposable
{
    private readonly HashSet<string> capabilities;
    private readonly IPluginStorageBridge storage;
    private readonly IPluginLogBridge log;
    private readonly IPluginNotificationBridge notifications;
    private readonly IPluginProtectionBridge protection;
    private int disposed;

    // 保留旧五参数构造的元数据签名，兼容反射调用和已编译宿主。
    public PluginBridgeScope(string pluginId, IReadOnlyList<string> capabilities,
        IPluginStorageBridge? storage, IPluginLogBridge? log, IPluginNotificationBridge? notifications)
        : this(pluginId, capabilities, storage, log, notifications, null) { }

    public PluginBridgeScope(string pluginId, IReadOnlyList<string> capabilities,
        IPluginStorageBridge? storage = null, IPluginLogBridge? log = null,
        IPluginNotificationBridge? notifications = null, IPluginProtectionBridge? protection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (PluginCapabilityCatalog.Validate(pluginId, capabilities).Count > 0)
            throw new PluginBridgeException(PluginBridgeError.CapabilityNotSupported, "宿主不支持插件声明的必需能力版本。");
        if ((capabilities.Contains("storage.sqlite/v1", StringComparer.Ordinal) && storage is null)
            || (capabilities.Contains("logging/v1", StringComparer.Ordinal) && log is null)
            || (capabilities.Contains("notifications/v1", StringComparer.Ordinal) && notifications is null)
            || (capabilities.Contains("protection/v1", StringComparer.Ordinal) && protection is null))
            throw new PluginBridgeException(PluginBridgeError.CapabilityNotSupported, "宿主未提供插件必需能力的实现。");
        PluginId = pluginId;
        this.capabilities = new HashSet<string>(capabilities, StringComparer.Ordinal);
        DeclaredCapabilities = Array.AsReadOnly(this.capabilities.Order(StringComparer.Ordinal).ToArray());
        this.storage = storage ?? PluginBridgeDefaults.Unavailable.Storage;
        this.log = log ?? PluginBridgeDefaults.Unavailable.Log;
        this.notifications = notifications ?? PluginBridgeDefaults.Unavailable.Notifications;
        this.protection = protection ?? PluginBridgeDefaults.Unavailable.Protection;
    }

    public string PluginId { get; }
    public IReadOnlyList<string> DeclaredCapabilities { get; }
    public IPluginStorageBridge Storage => this;
    public IPluginLogBridge Log => this;
    public IPluginNotificationBridge Notifications => this;
    public IPluginProtectionBridge Protection => this;

    public ValueTask<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
    {
        Check("protection/v1", cancellationToken);
        return protection.EncryptAsync(plaintext, cancellationToken);
    }

    public ValueTask<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
    {
        Check("protection/v1", cancellationToken);
        return protection.DecryptAsync(ciphertext, cancellationToken);
    }

    public ValueTask<IPluginSqliteDatabase> OpenSqliteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        Check("storage.sqlite/v1", cancellationToken);
        return storage.OpenSqliteAsync(relativePath, cancellationToken);
    }

    public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default)
    {
        Check("logging/v1", cancellationToken);
        return log.WriteAsync(entry, cancellationToken);
    }

    public ValueTask ShowAsync(PluginNotification notification, CancellationToken cancellationToken = default)
    {
        Check("notifications/v1", cancellationToken);
        return notifications.ShowAsync(notification, cancellationToken);
    }

    private void Check(string capability, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref disposed) != 0)
            throw new PluginBridgeException(PluginBridgeError.Disposed, "插件实例的宿主能力已释放。");
        if (!capabilities.Contains(capability))
            throw new PluginBridgeException(PluginBridgeError.CapabilityNotDeclared, "插件未声明所需宿主能力。");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // 同一适配器可实现多个能力，只释放一次；不销毁任何插件数据。
        var providers = new object[] { storage, log, notifications, protection }.Distinct(ReferenceEqualityComparer.Instance);
        List<Exception>? failures = null;
        foreach (var provider in providers)
        {
            try
            {
                if (provider is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (provider is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception)
            {
                (failures ??= []).Add(new PluginBridgeException(PluginBridgeError.OperationFailed, "插件能力资源释放失败。"));
            }
        }
        if (failures is not null) throw new AggregateException("插件能力资源释放失败。", failures);
    }
}
