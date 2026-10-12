using System.Net.Http.Headers;
using System.Text.Json;
using LoomX.Configuration;

namespace LoomX.Services;

public sealed record RemoteProviderModelDescriptor(string ModelId, string? OwnedBy, string? Family, int? ContextLength, int? MaxTokens, bool? Vision);
public sealed record ProviderModelCatalogResult(int StatusCode, bool Parsed, IReadOnlyList<RemoteProviderModelDescriptor> Models);

/// <summary>桌面模型获取与助手共享的请求、解析和元数据更新规则。</summary>
public static class ProviderModelCatalog
{
    public static Uri BuildEndpoint(string baseUrl, string? modelListUrl)
    {
        var value = string.IsNullOrWhiteSpace(modelListUrl) ? baseUrl.TrimEnd('/') + "/models" : modelListUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new ArgumentException("模型列表地址必须是无内嵌凭据的 HTTP/HTTPS 绝对地址。");
        return endpoint;
    }

    public static async Task<ProviderModelCatalogResult> FetchAsync(HttpClient client, Uri endpoint, string? apiKey,
        IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return new((int)response.StatusCode, false, []);
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            var parsed = root.ValueKind == JsonValueKind.Array || root.ValueKind == JsonValueKind.Object
                && (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                    || root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array);
            return new((int)response.StatusCode, parsed, Extract(root).GroupBy(item => item.ModelId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray());
        }
        catch (JsonException) { return new((int)response.StatusCode, false, []); }
    }

    public static ModelInput CreateInput(string apiMode, RemoteProviderModelDescriptor model) =>
        new(model.ModelId, model.ModelId, null, model.Family ?? "unknown", null, apiMode,
            model.ContextLength ?? 128000, model.MaxTokens ?? 4096, model.Vision ?? false,
            null, null, true, null, false, null, null, model.OwnedBy, model.Family, model.ContextLength, model.MaxTokens, model.Vision);

    public static ModelInput ApplyMetadata(ModelInput current, RemoteProviderModelDescriptor model) => current with
    {
        OwnedBy = model.OwnedBy, RemoteFamily = model.Family, RemoteContextLength = model.ContextLength,
        RemoteMaxTokens = model.MaxTokens, RemoteVision = model.Vision,
    };

    public static IEnumerable<RemoteProviderModelDescriptor> Extract(JsonElement root)
    {
        var items = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                ? data
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var models)
                    ? models
                    : default;
        if (items.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var modelId = ReadString(item, "id") ?? ReadString(item, "name") ?? ReadString(item, "model");
            if (string.IsNullOrWhiteSpace(modelId)) continue;
            var topProvider = item.TryGetProperty("top_provider", out var top) && top.ValueKind == JsonValueKind.Object ? top : default;
            var contextLength = ReadPositiveInt(item, "context_length") ?? ReadPositiveInt(topProvider, "context_length") ?? ReadPositiveInt(item, "inputTokenLimit");
            var maxTokens = ReadPositiveInt(topProvider, "max_completion_tokens") ?? ReadPositiveInt(item, "max_completion_tokens") ?? ReadPositiveInt(item, "max_output_tokens") ?? ReadPositiveInt(item, "outputTokenLimit");
            var family = ReadString(item, "family");
            var ownedBy = ReadString(item, "owned_by") ?? ReadString(item, "ownedBy");
            var vision = ReadNullableBool(item, "vision") ?? ReadCapabilitiesVision(item);
            yield return new RemoteProviderModelDescriptor(modelId.Trim(), ownedBy, family, contextLength, maxTokens, vision);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;
    private static int? ReadPositiveInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) && number > 0) return number;
        return null;
    }
    private static bool? ReadNullableBool(JsonElement element, string propertyName) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static bool? ReadCapabilitiesVision(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("vision", out _)) return ReadNullableBool(element, "vision");
        if (element.TryGetProperty("capabilities", out var capabilities))
        {
            if (capabilities.ValueKind == JsonValueKind.Array) return capabilities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && IsVisionCapability(item.GetString()));
            if (capabilities.ValueKind == JsonValueKind.Object) return ReadNullableBool(capabilities, "vision");
        }

        if (element.TryGetProperty("input_modalities", out var inputModalities) && inputModalities.ValueKind == JsonValueKind.Array)
            return inputModalities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "image", StringComparison.OrdinalIgnoreCase));
        if (element.TryGetProperty("architecture", out var architecture) && architecture.ValueKind == JsonValueKind.Object && architecture.TryGetProperty("input_modalities", out var architectureModalities) && architectureModalities.ValueKind == JsonValueKind.Array)
            return architectureModalities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "image", StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private static bool IsVisionCapability(string? value) => value is not null && (string.Equals(value, "vision", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "image", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "multimodal", StringComparison.OrdinalIgnoreCase));

}
