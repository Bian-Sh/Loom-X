using LoomX.Configuration;

namespace LoomX.NodeGraph;

public static class RuntimeGraphProjection
{
    public static RuntimeGraphSnapshot Create(ResolvedAppConfig config, IReadOnlyList<ProviderResponse> providerResponses)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(providerResponses);

        var endpoints = config.GatewayEndpoints
            .OrderBy(endpoint => endpoint.Key, StringComparer.OrdinalIgnoreCase)
            .Select(endpoint => new RuntimeGraphNode(
                endpoint.Key,
                RuntimeGraphNodeKind.Endpoint,
                EndpointLabel(endpoint.Key),
                endpoint.Enabled,
                PublicPath: endpoint.PublicPath))
            .ToArray();
        var combos = config.GatewayCombos
            .OrderBy(combo => combo.SortOrder)
            .ThenBy(combo => combo.Name, StringComparer.OrdinalIgnoreCase)
            .Select(combo => new RuntimeGraphNode(
                RuntimeGraphIds.Combo(combo.Id),
                RuntimeGraphNodeKind.Combo,
                combo.Name,
                combo.Enabled,
                SortOrder: combo.SortOrder))
            .ToArray();

        var providers = providerResponses
            .GroupBy(provider => provider.BusinessId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var models = config.Models
            .GroupBy(model => RuntimeGraphIds.Model(model.ProviderId, model.ModelId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var model = group.First();
                providers.TryGetValue(model.ProviderId, out var provider);
                return new RuntimeGraphNode(
                    group.Key,
                    RuntimeGraphNodeKind.Model,
                    model.DisplayName,
                    true,
                    ProviderId: model.ProviderId,
                    ProviderDisplayName: provider?.DisplayName ?? model.ProviderId,
                    ProviderBaseUrl: provider?.BaseUrl ?? model.BaseUrl,
                    ProviderProtocol: provider?.ApiMode ?? model.ApiModes.FirstOrDefault(),
                    ModelId: model.ModelId);
            })
            .ToArray();
        var modelById = models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var comboById = config.GatewayCombos.ToDictionary(combo => RuntimeGraphIds.Combo(combo.Id), StringComparer.OrdinalIgnoreCase);
        var edgeMap = new Dictionary<string, RuntimeGraphEdge>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in config.GatewayEndpoints.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var binding in endpoint.ComboBindings.OrderBy(item => item.SortOrder))
            {
                var comboId = RuntimeGraphIds.Combo(binding.ComboId);
                if (!comboById.ContainsKey(comboId)) continue;
                AddEdge(edgeMap, new RuntimeGraphEdge(
                    RuntimeGraphIds.EndpointComboEdge(endpoint.Key, comboId),
                    RuntimeGraphEdgeKind.EndpointToCombo,
                    endpoint.Key,
                    comboId,
                    endpoint.Enabled && binding.Enabled && comboById[comboId].Enabled,
                    endpoint.Key,
                    ComboId: comboId));
            }
        }

        foreach (var combo in config.GatewayCombos)
        {
            var comboId = RuntimeGraphIds.Combo(combo.Id);
            foreach (var route in combo.Routes.OrderBy(item => item.SortOrder))
            {
                var modelId = RuntimeGraphIds.Model(route.Model.ProviderId, route.Model.ModelId);
                var modelExists = modelById.ContainsKey(modelId);
                AddEdge(edgeMap, new RuntimeGraphEdge(
                    RuntimeGraphIds.ComboModelEdge(comboId, route.Model.ProviderId, route.Model.ModelId),
                    RuntimeGraphEdgeKind.ComboToModel,
                    comboId,
                    modelId,
                    combo.Enabled && route.Enabled && modelExists,
                    string.Empty,
                    comboId,
                    route.Model.ProviderId,
                    route.Model.ModelId));
            }
        }

        var routes = new List<RuntimeGraphRoute>();
        foreach (var endpoint in config.GatewayEndpoints)
        {
            foreach (var binding in endpoint.ComboBindings.Where(item => item.Enabled))
            {
                var combo = config.GatewayCombos.FirstOrDefault(item => item.Id == binding.ComboId);
                if (combo is null) continue;
                var comboId = RuntimeGraphIds.Combo(combo.Id);
                foreach (var route in combo.Routes.Where(item => item.Enabled).OrderBy(item => item.SortOrder))
                {
                    var modelId = RuntimeGraphIds.Model(route.Model.ProviderId, route.Model.ModelId);
                    if (!endpoint.Enabled || !combo.Enabled || !modelById.ContainsKey(modelId)) continue;
                    routes.Add(new RuntimeGraphRoute(
                        RuntimeGraphIds.Route(endpoint.Key, comboId, route.Model.ProviderId, route.Model.ModelId),
                        endpoint.Key,
                        comboId,
                        combo.Name,
                        route.Model.ProviderId,
                        route.Model.ModelId,
                        [
                            RuntimeGraphIds.EndpointComboEdge(endpoint.Key, comboId),
                            RuntimeGraphIds.ComboModelEdge(comboId, route.Model.ProviderId, route.Model.ModelId)
                        ]));
                }
            }
        }

        return new RuntimeGraphSnapshot(endpoints, combos, models, edgeMap.Values.ToArray(), routes);
    }

    private static void AddEdge(IDictionary<string, RuntimeGraphEdge> edges, RuntimeGraphEdge edge)
    {
        if (edges.TryGetValue(edge.Id, out var existing))
        {
            if (existing.Enabled || !edge.Enabled) return;
            edges[edge.Id] = edge;
            return;
        }

        edges.Add(edge.Id, edge);
    }

    private static string EndpointLabel(string key) => key.ToLowerInvariant() switch
    {
        "openai" => "OpenAI",
        "ollama" => "Ollama",
        "azure" => "Azure",
        _ => key
    };
}
