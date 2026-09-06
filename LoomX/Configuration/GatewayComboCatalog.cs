namespace LoomX.Configuration;

internal static class GatewayComboCatalog
{
    public static IReadOnlyList<ResolvedGatewayComboConfig> ForEndpoint(ResolvedAppConfig config, string endpointKey)
    {
        var endpoint = config.GatewayEndpoints
            .FirstOrDefault(item => item.Enabled && string.Equals(item.Key, endpointKey, StringComparison.OrdinalIgnoreCase));
        if (endpoint is null) return [];
        var comboIds = endpoint.ComboBindings
            .Where(binding => binding.Enabled)
            .Select(binding => binding.ComboId)
            .ToHashSet();
        return config.GatewayCombos
            .Where(combo => comboIds.Contains(combo.Id) && combo.Enabled && combo.Routes.Any(route => route.Enabled))
            .OrderBy(combo => combo.SortOrder).ToArray() ?? [];
    }
}
