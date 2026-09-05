namespace LoomX.Configuration;

internal static class GatewayComboCatalog
{
    public static IReadOnlyList<ResolvedGatewayComboConfig> ForEndpoint(ResolvedAppConfig config, string endpointKey)
    {
        return config.GatewayEndpoints
            .FirstOrDefault(endpoint => endpoint.Enabled && string.Equals(endpoint.Key, endpointKey, StringComparison.OrdinalIgnoreCase))?.Combos
            .Where(combo => combo.Enabled && combo.Routes.Any(route => route.Enabled))
            .OrderBy(combo => combo.SortOrder).ToArray() ?? [];
    }
}
