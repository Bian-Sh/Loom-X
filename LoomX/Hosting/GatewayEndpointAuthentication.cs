using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using LoomX.Configuration;

namespace LoomX.Hosting;

internal static class GatewayEndpointAuthentication
{
    public static bool IsAuthorized(HttpContext context, ResolvedGatewayEndpointConfig endpoint)
    {
        if (!GatewayEndpointSettings.RequiresApiKey(endpoint.Key)) return true;
        if (string.IsNullOrWhiteSpace(endpoint.ApiKey)) return false;

        if (TryGetBearerToken(context.Request.Headers.Authorization, out var bearer)
            && SecureEquals(bearer, endpoint.ApiKey)) return true;

        return string.Equals(endpoint.Key, "azure", StringComparison.OrdinalIgnoreCase)
            && SecureEquals(context.Request.Headers["api-key"].ToString(), endpoint.ApiKey);
    }

    private static bool TryGetBearerToken(string? header, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(header)) return false;
        var separator = header.IndexOf(' ');
        if (separator <= 0 || !header[..separator].Equals("Bearer", StringComparison.OrdinalIgnoreCase)) return false;
        token = header[(separator + 1)..].Trim();
        return token.Length > 0;
    }

    private static bool SecureEquals(string? provided, string expected)
    {
        if (string.IsNullOrWhiteSpace(provided)) return false;
        var providedBytes = Encoding.UTF8.GetBytes(provided.Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
