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
    ComboToModel
}

public sealed record RuntimeGraphNode(
    string Id,
    RuntimeGraphNodeKind Kind,
    string DisplayName,
    bool Enabled,
    string? EndpointId = null,
    string? ProviderId = null,
    string? ProviderDisplayName = null,
    string? ProviderBaseUrl = null,
    string? ProviderProtocol = null,
    string? ModelId = null,
    string? PublicPath = null,
    int SortOrder = 0);

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
    IReadOnlyList<RuntimeGraphNode> Models,
    IReadOnlyList<RuntimeGraphEdge> Edges,
    IReadOnlyList<RuntimeGraphRoute> Routes)
{
    public RuntimeGraphSnapshot ForEndpoint(string endpointId)
    {
        var endpoint = Endpoints.FirstOrDefault(item => string.Equals(item.Id, endpointId, StringComparison.OrdinalIgnoreCase));
        if (endpoint is null)
            return new RuntimeGraphSnapshot([], [], [], [], []);

        var visibleComboIds = Edges
            .Where(edge => edge.Kind == RuntimeGraphEdgeKind.EndpointToCombo
                && string.Equals(edge.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.TargetId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routes = Routes
            .Where(item => string.Equals(item.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var visibleModelIds = routes
            .Select(item => RuntimeGraphIds.Model(item.ProviderId, item.ModelId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var combos = Combos.Where(item => visibleComboIds.Contains(item.Id)).ToArray();
        var models = Models.Where(item => visibleModelIds.Contains(item.Id)).ToArray();
        var edges = Edges
            .Where(edge => edge.Kind == RuntimeGraphEdgeKind.EndpointToCombo
                ? string.Equals(edge.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase)
                : visibleComboIds.Contains(edge.SourceId) && visibleModelIds.Contains(edge.TargetId))
            .ToArray();

        return new RuntimeGraphSnapshot([endpoint], combos, models, edges, routes);
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
    public static string Combo(Guid comboId) => $"combo|{comboId:N}";

    public static string Model(string providerId, string modelId) => $"model|{providerId}|{modelId}";

    public static string EndpointComboEdge(string endpointId, string comboId) => $"endpoint-combo|{endpointId}|{comboId}";

    public static string ComboModelEdge(string comboId, string providerId, string modelId) => $"combo-model|{comboId}|{providerId}|{modelId}";

    public static string Route(string endpointId, string comboId, string providerId, string modelId) =>
        $"route|{endpointId}|{comboId}|{providerId}|{modelId}";
}
