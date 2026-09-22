using Microsoft.AspNetCore.Http;

namespace LoomX.Hosting;

internal static class GatewayEndpointRouting
{
    public static string? ResolveKey(PathString path) =>
        path.StartsWithSegments("/openai") ? "openai" :
        path.StartsWithSegments("/azure") ? "azure" :
        path.StartsWithSegments("/api") || path.StartsWithSegments("/v1/models") || path.StartsWithSegments("/v1/chat/completions") ? "ollama" : null;

    public static string ResolveLabel(PathString path) => ResolveKey(path) switch
    {
        "openai" => "OpenAI",
        "azure" => "Azure",
        "ollama" => "Ollama",
        _ => "Unknown"
    };
}
