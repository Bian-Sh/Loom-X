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

[Flags]
public enum ConfigurationChangeFields
{
    None = 0,
    SettingsLanguage = 1 << 0,
    SettingsTheme = 1 << 1,
    SettingsProxy = 1 << 2,
    SettingsUpdates = 1 << 3,
    SettingsDiagnostics = 1 << 4,
    SettingsLogging = 1 << 5,
    SettingsAppearance = 1 << 6,
    ProviderIdentity = 1 << 7,
    ProviderConnection = 1 << 8,
    ProviderAvailability = 1 << 9,
    ProviderCredentials = 1 << 10,
    ProviderHeaders = 1 << 11,
    ModelIdentity = 1 << 12,
    ModelParameters = 1 << 13,
    ModelAvailability = 1 << 14,
    ModelCredentials = 1 << 15,
    ModelHeaders = 1 << 16,
    ModelMetadata = 1 << 17,
    EndpointAvailability = 1 << 18,
    EndpointCredentials = 1 << 19,
    EndpointReasoning = 1 << 20,
    EndpointBindings = 1 << 21,
    ComboIdentity = 1 << 22,
    ComboAvailability = 1 << 23,
    ComboOrder = 1 << 24,
    RouteModel = 1 << 25,
    RouteAvailability = 1 << 26,
    RouteOrder = 1 << 27,

    SettingsDiagnosticsEnabled = SettingsDiagnostics,
    SettingsTransparency = SettingsAppearance,
    ProviderEnabled = ProviderAvailability,
    ModelEnabled = ModelAvailability,
    EndpointEnabled = EndpointAvailability,
    ComboEnabled = ComboAvailability,
    RouteEnabled = RouteAvailability,
    DiagnosticsEnabled = SettingsDiagnostics
}

/// <summary>描述一次配置变更的来源与实体类型，供各页面决定是否需要重建编辑区。</summary>
public sealed record ConfigurationChangedEventArgs(
    ConfigurationChangeSource Source,
    ConfigurationChangeKind Kind,
    Guid? EntityId = null,
    string? EntityKey = null,
    Guid? ParentEntityId = null,
    ConfigurationChangeFields Fields = ConfigurationChangeFields.None);
