using System.Text.Json.Nodes;

namespace OllamaHub.Configuration;

public sealed class ResolvedModelConfig
{
    public required string ModelId { get; init; }

    public required string OllamaModelName { get; init; }

    public required string DisplayName { get; init; }

    public required string ProviderId { get; init; }

    public IReadOnlyList<string> ApiModes { get; init; } = [];

    public string EndpointFormat { get; init; } = "responses";

    public required string BaseUrl { get; init; }

    public required string ApiKey { get; init; }

    public required string AnthropicModel { get; init; }

    public bool UseProxy { get; init; }

    public string Family { get; init; } = "claude";

    public int ContextLength { get; init; } = 128000;

    public int MaxTokens { get; init; } = 4096;

    public bool Vision { get; init; }

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, JsonNode?> Extra { get; init; } = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

    public bool SupportsApiMode(string apiMode) =>
        ApiModes.Any(mode => string.Equals(mode, apiMode, StringComparison.OrdinalIgnoreCase));
}

public sealed class ResolvedServerConfig
{
    public IReadOnlyList<string> Urls { get; init; } = [];
}

public sealed class ResolvedProviderConfig
{
    public required string Id { get; init; }
    public string? BaseUrl { get; init; }
    public IReadOnlyList<string> ApiModes { get; init; } = [];
    public string EndpointFormat { get; init; } = "responses";
    public bool HasApiKey { get; init; }
    public bool UseProxy { get; init; }
}

public sealed class ResolvedAppSettings
{
    public string Language { get; init; } = "zh-CN";
    public string Theme { get; init; } = "system";
    public bool OpenControlCenterOnStartup { get; init; } = true;
    public string ProxyMode { get; init; } = "direct";
    public string ProxyHost { get; init; } = "http://127.0.0.1";
    public int ProxyPort { get; init; } = 7890;
    public string? ProxyUsername { get; init; }
    public bool HasProxyPassword { get; init; }
    public bool AutoCheckUpdates { get; init; } = true;
    public string UpdateChannel { get; init; } = "stable";
    public bool DiagnosticsEnabled { get; init; }
    public int LogRetentionDays { get; init; } = 30;
}

public sealed class ResolvedAppConfig
{
    public ResolvedServerConfig Server { get; init; } = new();

    public ResolvedAppSettings Settings { get; init; } = new();

    public IReadOnlyList<ResolvedProviderConfig> Providers { get; init; } = [];

    public IReadOnlyList<ResolvedModelConfig> Models { get; init; } = [];

    public IReadOnlyList<ResolvedGatewayEndpointConfig> GatewayEndpoints { get; init; } = [];
}

public sealed class ResolvedGatewayEndpointConfig
{
    public required string Key { get; init; }
    public required string PublicPath { get; init; }
    public bool Enabled { get; init; }
    public IReadOnlyList<ResolvedGatewayComboConfig> Combos { get; init; } = [];
}

public sealed class ResolvedGatewayComboConfig
{
    public required string Name { get; init; }
    public bool Enabled { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<ResolvedGatewayRouteConfig> Routes { get; init; } = [];
}

public sealed class ResolvedGatewayRouteConfig
{
    public required ResolvedModelConfig Model { get; init; }
    public bool Enabled { get; init; }
    public int SortOrder { get; init; }
}
