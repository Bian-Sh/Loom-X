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

        var combos = config.GatewayEndpoints
            .OrderBy(endpoint => endpoint.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(endpoint => endpoint.Combos
                .OrderBy(combo => combo.SortOrder)
                .ThenBy(combo => combo.Name, StringComparer.OrdinalIgnoreCase)
                .Select(combo => new RuntimeGraphNode(
                    RuntimeGraphIds.Combo(endpoint.Key, combo.Name),
                    RuntimeGraphNodeKind.Combo,
                    combo.Name,
                    combo.Enabled,
                    EndpointId: endpoint.Key,
                    SortOrder: combo.SortOrder)))
            .ToArray();

        var models = config.Models
            .GroupBy(model => RuntimeGraphIds.Model(model.ProviderId, model.ModelId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var modelById = models.ToDictionary(model => RuntimeGraphIds.Model(model.ProviderId, model.ModelId), StringComparer.OrdinalIgnoreCase);

        var providerSeeds = providerResponses
            .GroupBy(provider => provider.BusinessId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        foreach (var model in models)
        {
            if (providerSeeds.Any(provider => string.Equals(provider.BusinessId, model.ProviderId, StringComparison.OrdinalIgnoreCase))) continue;
            providerSeeds.Add(new ProviderResponse(
                Guid.Empty,
                model.ProviderId,
                model.ProviderId,
                model.BaseUrl,
                model.ApiModes.FirstOrDefault() ?? "unknown",
                true,
                model.UseProxy,
                false,
                0,
                "{}",
                []));
        }

        var providerModels = models
            .GroupBy(model => model.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(model => new RuntimeGraphNode(
                    RuntimeGraphIds.Model(model.ProviderId, model.ModelId),
                    RuntimeGraphNodeKind.Model,
                    model.DisplayName,
                    true,
                    ProviderId: model.ProviderId,
                    ModelId: model.ModelId)).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var providers = providerSeeds
            .Select(provider => new RuntimeGraphProviderGroup(
                provider.BusinessId,
                provider.DisplayName,
                provider.Enabled,
                provider.BaseUrl,
                provider.ApiMode,
                providerModels.GetValueOrDefault(provider.BusinessId) ?? []))
            .ToArray();

        var edgeMap = new Dictionary<string, RuntimeGraphEdge>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in config.GatewayEndpoints.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var combo in endpoint.Combos.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var comboId = RuntimeGraphIds.Combo(endpoint.Key, combo.Name);
                AddEdge(edgeMap, new RuntimeGraphEdge(
                    RuntimeGraphIds.EndpointComboEdge(comboId),
                    RuntimeGraphEdgeKind.EndpointToCombo,
                    endpoint.Key,
                    comboId,
                    endpoint.Enabled && combo.Enabled,
                    endpoint.Key,
                    ComboId: comboId));
            }
        }

        var routes = new List<RuntimeGraphRoute>();
        foreach (var endpoint in config.GatewayEndpoints.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var combo in endpoint.Combos.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var comboId = RuntimeGraphIds.Combo(endpoint.Key, combo.Name);
                foreach (var route in combo.Routes.OrderBy(item => item.SortOrder))
                {
                    var modelId = RuntimeGraphIds.Model(route.Model.ProviderId, route.Model.ModelId);
                    var modelExists = modelById.ContainsKey(modelId);
                    var comboProviderId = RuntimeGraphIds.ComboProviderEdge(comboId, route.Model.ProviderId);
                    AddEdge(edgeMap, new RuntimeGraphEdge(
                        comboProviderId,
                        RuntimeGraphEdgeKind.ComboToProvider,
                        comboId,
                        route.Model.ProviderId,
                        endpoint.Enabled && combo.Enabled && route.Enabled && modelExists,
                        endpoint.Key,
                        comboId,
                        route.Model.ProviderId,
                        route.Model.ModelId));

                    if (!endpoint.Enabled || !combo.Enabled || !route.Enabled || !modelExists) continue;

                    routes.Add(new RuntimeGraphRoute(
                        RuntimeGraphIds.Route(endpoint.Key, comboId, route.Model.ProviderId, route.Model.ModelId),
                        endpoint.Key,
                        comboId,
                        combo.Name,
                        route.Model.ProviderId,
                        route.Model.ModelId,
                        [
                            RuntimeGraphIds.EndpointComboEdge(comboId),
                            comboProviderId,
                            RuntimeGraphIds.ProviderModelEdge(route.Model.ProviderId, route.Model.ModelId)
                        ]));
                }
            }
        }

        foreach (var model in models)
        {
            AddEdge(edgeMap, new RuntimeGraphEdge(
                RuntimeGraphIds.ProviderModelEdge(model.ProviderId, model.ModelId),
                RuntimeGraphEdgeKind.ProviderToModel,
                model.ProviderId,
                RuntimeGraphIds.Model(model.ProviderId, model.ModelId),
                true,
                string.Empty,
                ProviderId: model.ProviderId,
                ModelId: model.ModelId));
        }

        return new RuntimeGraphSnapshot(endpoints, combos, providers, edgeMap.Values.ToArray(), routes);
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
