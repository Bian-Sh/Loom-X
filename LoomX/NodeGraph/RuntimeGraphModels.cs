namespace LoomX.NodeGraph;

public enum RuntimeGraphNodeKind
{
    Endpoint,
    Combo,
    Model
}

public enum RuntimeGraphEdgeKind
{
    EndpointToCombo,
    ComboToProvider,
    ProviderToModel
}

public sealed record RuntimeGraphNode(
    string Id,
    RuntimeGraphNodeKind Kind,
    string DisplayName,
    bool Enabled,
    string? EndpointId = null,
    string? ProviderId = null,
    string? ModelId = null,
    string? PublicPath = null,
    int SortOrder = 0);

public sealed record RuntimeGraphProviderGroup(
    string Id,
    string DisplayName,
    bool Enabled,
    string? BaseUrl,
    string? Protocol,
    IReadOnlyList<RuntimeGraphNode> Models);

public sealed record RuntimeGraphEdge(
    string Id,
    RuntimeGraphEdgeKind Kind,
    string SourceId,
    string TargetId,
    bool Enabled,
    string EndpointId,
    string? ComboId = null,
    string? ProviderId = null,
    string? ModelId = null);

public sealed record RuntimeGraphRoute(
    string Id,
    string EndpointId,
    string ComboId,
    string ComboName,
    string ProviderId,
    string ModelId,
    IReadOnlyList<string> EdgeIds);

public sealed record RuntimeGraphSnapshot(
    IReadOnlyList<RuntimeGraphNode> Endpoints,
    IReadOnlyList<RuntimeGraphNode> Combos,
    IReadOnlyList<RuntimeGraphProviderGroup> Providers,
    IReadOnlyList<RuntimeGraphEdge> Edges,
    IReadOnlyList<RuntimeGraphRoute> Routes)
{
    public IReadOnlyList<RuntimeGraphNode> Models => Providers.SelectMany(provider => provider.Models).ToArray();

    public RuntimeGraphRoute? FindRoute(string endpointId, string? comboName, string? providerId, string? modelId) =>
        Routes.FirstOrDefault(route =>
            string.Equals(route.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(comboName) || string.Equals(route.ComboName, comboName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(providerId) || string.Equals(route.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(modelId) || string.Equals(route.ModelId, modelId, StringComparison.OrdinalIgnoreCase)));
}

public static class RuntimeGraphIds
{
    public static string Combo(string endpointId, string comboName) => $"{endpointId}|{comboName}";

    public static string Model(string providerId, string modelId) => $"{providerId}|{modelId}";

    public static string EndpointComboEdge(string comboId) => $"endpoint-combo|{comboId}";

    public static string ComboProviderEdge(string comboId, string providerId) => $"combo-provider|{comboId}|{providerId}";

    public static string ProviderModelEdge(string providerId, string modelId) => $"provider-model|{providerId}|{modelId}";

    public static string Route(string endpointId, string comboId, string providerId, string modelId) =>
        $"route|{endpointId}|{comboId}|{providerId}|{modelId}";
}
