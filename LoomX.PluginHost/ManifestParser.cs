using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoomX.Plugins.Host;

/// <summary>
/// 插件 Manifest 解析与验证。Runtime 必备字段非法时拒绝插件；
/// 可选 UI 声明非法时只忽略对应 Contribution，并通过 diagnostics 返回安全诊断。
/// </summary>
public static class ManifestParser
{
    public const string ManifestFileName = "plugin.manifest.json";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static PluginManifest? Parse(
        string json,
        IList<string> errors,
        IList<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(errors);

        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, ManifestJsonOptions);
        }
        catch (JsonException exception)
        {
            errors.Add($"Manifest 不是合法 JSON：{exception.Message}");
            return null;
        }

        if (dto is null)
        {
            errors.Add("Manifest 内容为空。");
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.Id)) errors.Add("Manifest 缺少必备字段 id。");
        if (string.IsNullOrWhiteSpace(dto.Version)) errors.Add("Manifest 缺少必备字段 version。");
        if (dto.Capabilities is null || dto.Capabilities.Count == 0)
            errors.Add("Manifest 缺少必备字段 capabilities。");
        if (dto.Extensions is null || dto.Extensions.Count == 0)
            errors.Add("Manifest 缺少必备字段 extensions。");
        if (string.IsNullOrWhiteSpace(dto.Assembly)) errors.Add("Manifest 缺少加载入口 assembly。");
        if (string.IsNullOrWhiteSpace(dto.PluginType)) errors.Add("Manifest 缺少加载入口 plugin_type。");
        if (errors.Count > 0) return null;

        var extensions = new List<PluginManifestExtension>(dto.Extensions!.Count);
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in dto.Extensions!)
        {
            if (string.IsNullOrWhiteSpace(extension.Id))
            {
                errors.Add("Extension 缺少 id。");
                continue;
            }
            if (!extensionIds.Add(extension.Id))
            {
                errors.Add($"Extension id 重复：{extension.Id}");
                continue;
            }
            if (!TryParseKind(extension.Kind, out var kind))
            {
                errors.Add($"Extension {extension.Id} 的 kind 非法：{extension.Kind ?? "(空)"}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(extension.Pipeline))
            {
                errors.Add($"Extension {extension.Id} 缺少 pipeline 归属。");
                continue;
            }
            if (!TryParseFailurePolicy(extension.FailurePolicy, out var failurePolicy))
            {
                errors.Add($"Extension {extension.Id} 的 failure_policy 非法：{extension.FailurePolicy ?? "(空)"}");
                continue;
            }

            extensions.Add(new PluginManifestExtension(
                extension.Id,
                kind,
                extension.Pipeline,
                failurePolicy,
                extension.Capabilities?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                    ?? []));
        }

        if (errors.Count > 0) return null;

        PluginManifestUi? ui = null;
        if (dto.Ui is not null)
        {
            var contributions = new List<PluginManifestUiContribution>();
            var contributionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contribution in dto.Ui.Contributions ?? [])
            {
                if (string.IsNullOrWhiteSpace(contribution.Id))
                {
                    diagnostics?.Add("UI Contribution 缺少 id，已忽略。");
                    continue;
                }
                if (!contributionIds.Add(contribution.Id))
                {
                    diagnostics?.Add($"UI Contribution id 重复：{contribution.Id}，已忽略重复项。");
                    continue;
                }
                if (!TryParseUiSlot(contribution.Slot, out var slot))
                {
                    diagnostics?.Add($"UI Contribution {contribution.Id} 的 slot 非法：{contribution.Slot ?? "(空)"}，已忽略。");
                    continue;
                }
                contributions.Add(new PluginManifestUiContribution(contribution.Id, slot));
            }
            ui = new PluginManifestUi(contributions);
        }

        return new PluginManifest(
            dto.Id!,
            dto.Version!,
            dto.Assembly!,
            dto.PluginType!,
            dto.Capabilities!.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            extensions,
            ui);
    }

    private static bool TryParseKind(string? value, out ExtensionKind kind)
    {
        kind = default;
        switch (value)
        {
            case "request": kind = ExtensionKind.Request; return true;
            case "response": kind = ExtensionKind.Response; return true;
            default: return false;
        }
    }

    private static bool TryParseFailurePolicy(string? value, out ExtensionFailurePolicy failurePolicy)
    {
        failurePolicy = default;
        switch (value)
        {
            case null:
            case "continue-on-error": failurePolicy = ExtensionFailurePolicy.ContinueOnError; return true;
            case "fail-closed": failurePolicy = ExtensionFailurePolicy.FailClosed; return true;
            default: return false;
        }
    }

    private static bool TryParseUiSlot(string? value, out PluginUiSlot slot)
    {
        slot = default;
        switch (value)
        {
            case "card-body": slot = PluginUiSlot.CardBody; return true;
            case "detail-body": slot = PluginUiSlot.DetailBody; return true;
            default: return false;
        }
    }

    private sealed class ManifestDto
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string? Assembly { get; set; }
        [JsonPropertyName("plugin_type")]
        public string? PluginType { get; set; }
        public List<string>? Capabilities { get; set; }
        public List<ExtensionDto>? Extensions { get; set; }
        public UiDto? Ui { get; set; }
    }

    private sealed class ExtensionDto
    {
        public string? Id { get; set; }
        public string? Kind { get; set; }
        public string? Pipeline { get; set; }
        [JsonPropertyName("failure_policy")]
        public string? FailurePolicy { get; set; }
        public List<string>? Capabilities { get; set; }
    }

    private sealed class UiDto
    {
        public List<UiContributionDto>? Contributions { get; set; }
    }

    private sealed class UiContributionDto
    {
        public string? Id { get; set; }
        public string? Slot { get; set; }
    }
}
