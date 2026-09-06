using System.Security.Cryptography;

namespace LoomX.Configuration;

public static class GatewayEndpointSettings
{
    public const string DefaultReasoningEffort = "medium";

    public static IReadOnlyList<string> ReasoningEfforts { get; } = ["minimal", "low", "medium", "high"];

    public static bool RequiresApiKey(string endpointKey) =>
        string.Equals(endpointKey, "openai", StringComparison.OrdinalIgnoreCase)
        || string.Equals(endpointKey, "azure", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeReasoningEffort(string? value) =>
        ReasoningEfforts.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? value!.Trim().ToLowerInvariant()
            : DefaultReasoningEffort;

    public static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "lx_" + Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
