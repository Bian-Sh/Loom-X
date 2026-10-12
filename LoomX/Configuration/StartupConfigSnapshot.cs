namespace LoomX.Configuration;

/// <summary>
/// 桌面启动所需的一次性配置快照。
/// </summary>
public sealed record StartupConfigSnapshot(
    ResolvedAppConfig Config,
    IReadOnlyList<ProviderResponse> Providers,
    AppSettingsResponse Settings,
    IReadOnlyList<GatewayEndpointResponse> GatewayEndpoints,
    IReadOnlyList<GatewayComboResponse> GatewayCombos,
    IReadOnlyList<GatewayModelSourceResponse> EnabledGatewayModels);
