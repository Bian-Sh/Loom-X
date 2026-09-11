namespace LoomX.Services;

public enum ConfigurationChangeSource
{
    /// <summary>应用启动时的首次加载。</summary>
    Initialization,
    /// <summary>全量刷新快照（手动刷新、网关重启等）。</summary>
    Snapshot,
    /// <summary>桌面端本机编辑落库产生的事件；编辑中的页面无需重建 UI。</summary>
    LocalSave
}

public enum ConfigurationChangeKind
{
    Snapshot,
    Settings,
    Provider,
    Model,
    GatewayEndpoint,
    GatewayCombo,
    GatewayRoute
}

/// <summary>描述一次配置变更的来源与实体类型，供各页面决定是否需要重建编辑区。</summary>
public sealed record ConfigurationChangedEventArgs(
    ConfigurationChangeSource Source,
    ConfigurationChangeKind Kind,
    Guid? EntityId = null);
