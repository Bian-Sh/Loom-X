namespace OllamaHub.Configuration;

internal static class GatewayComboMatcher
{
    public static bool Matches(ResolvedGatewayComboConfig combo, string requestedModel)
    {
        var normalized = requestedModel.Trim();
        if (normalized.Length == 0) return false;
        return string.Equals(combo.Name, normalized, StringComparison.OrdinalIgnoreCase);
    }
}
