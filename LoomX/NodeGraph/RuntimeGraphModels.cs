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

    public RuntimeGraphSnapshot ForEndpoint(string endpointId)
    {
        var endpoint = Endpoints.FirstOrDefault(item => string.Equals(item.Id, endpointId, StringComparison.OrdinalIgnoreCase));
        if (endpoint is null)
            return new RuntimeGraphSnapshot([], [], [], [], []);

        var combos = Combos
            .Where(item => string.Equals(item.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var routes = Routes
            .Where(item => string.Equals(item.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var providerIds = routes
            .Select(item => item.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modelIdsByProvider = routes
            .GroupBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => RuntimeGraphIds.Model(item.ProviderId, item.ModelId)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var providers = Providers
            .Where(item => providerIds.Contains(item.Id))
            .Select(provider => provider with
            {
                Models = provider.Models
                    .Where(model => modelIdsByProvider.GetValueOrDefault(provider.Id)?.Contains(model.Id) == true)
                    .ToArray()
            })
            .ToArray();
        var visibleModelIds = providers
            .SelectMany(item => item.Models)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleComboIds = combos
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = Edges
            .Where(edge =>
                edge.Kind == RuntimeGraphEdgeKind.EndpointToCombo
                    ? string.Equals(edge.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase)
                    : edge.Kind == RuntimeGraphEdgeKind.ComboToProvider
                        ? string.Equals(edge.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase)
                            && visibleComboIds.Contains(edge.SourceId)
                            && providerIds.Contains(edge.TargetId)
                        : visibleModelIds.Contains(edge.TargetId))
            .ToArray();

        return new RuntimeGraphSnapshot([endpoint], combos, providers, edges, routes);
    }

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
