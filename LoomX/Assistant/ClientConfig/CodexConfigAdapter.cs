using LoomX.Assistant.Configuration;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// Codex CLI（ChatGPT）适配器：把 LoomX 写进 ~/.codex/config.toml。
/// 移植自 ai-switch CodexAdapter 的默认形态（bearer token，不改写 ChatGPT 登录态）。
/// TOML 编辑委托 ITomlDocumentService（保留注释、原子替换、自动备份）；
/// 另负责生成 Codex 启动时加载的 model catalog（loomx-model-catalog.json）。
/// </summary>
public sealed class CodexConfigAdapter(ITomlDocumentService tomlDocumentService) : IClientConfigAdapter
{
    public const string ProviderId = "loomx";
    public const string CatalogFileName = "loomx-model-catalog.json";

    private static readonly IReadOnlyList<string> DefaultReasoningLevels = ["minimal", "low", "medium", "high"];

    public ClientTargetDescriptor Descriptor { get; } =
        new("codex", "Codex CLI (ChatGPT)", RequiresModelList: false, RestartRequired: false, Platform: "codex");

    public FileInfo ResolvePath(string homeRoot) => new(Path.Combine(homeRoot, ".codex", "config.toml"));

    public async Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken)
    {
        var operations = await BuildOperations(path, existing, input, cancellationToken);
        var result = await tomlDocumentService.PatchAsync(path.FullName, operations, cancellationToken);
        if (!result.Success || result.Errors.Count > 0)
        {
            // 引擎错误是 LoomX 自产的诊断消息（不含文件内容与凭据），可直接透出。
            throw new ClientConfigException(
                "config.codex_write_failed",
                $"Codex 配置写入失败：{string.Join("; ", result.Errors)}");
        }

        var catalogPath = WriteModelCatalog(path, input);
        return new ClientAdapterWriteResult(null, result.BackupPath, result.Changed, catalogPath);
    }

    public ClientConfigInspection Inspect(FileInfo path, string? existing) =>
        throw new NotSupportedException("codex 走 InspectAsync。");

    public async Task<ClientConfigInspection> InspectAsync(FileInfo path, string? existing, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (existing is null)
        {
            return new ClientConfigInspection("missing", Managed: false);
        }

        try
        {
            var document = SyntaxParser.Parse(existing, "inspect-codex.toml", validate: true);
            if (document.HasErrors)
            {
                return new ClientConfigInspection("invalid", Managed: false);
            }

            var providerSelected = RootStringValue(document, "model_provider") == ProviderId;
            var providerDefined = HasProviderTable(document);
            var managed = providerSelected && providerDefined;
            return new ClientConfigInspection(managed ? "managed" : "unmanaged", managed);
        }
        catch (TomlException)
        {
            return new ClientConfigInspection("invalid", Managed: false);
        }
    }

    /// <summary>根级单段键的字符串值（不存在或非字符串返回 null）。</summary>
    private static string? RootStringValue(DocumentSyntax document, string key)
    {
        foreach (var keyValue in document.KeyValues)
        {
            if (keyValue?.Key is not { } keySyntax
                || !string.Equals(KeyName(keySyntax.Key), key, StringComparison.Ordinal)
                || keySyntax.DotKeys.ChildrenCount > 0)
            {
                continue;
            }

            return keyValue.Value is StringValueSyntax text ? text.Value : null;
        }

        return null;
    }

    /// <summary>model_providers.&lt;loomx&gt; 表存在性：点式表头或根级内联表两种形态都认。</summary>
    private static bool HasProviderTable(DocumentSyntax document)
    {
        foreach (var table in document.Tables)
        {
            if (table?.Name is not { } name || !string.Equals(KeyName(name.Key), "model_providers", StringComparison.Ordinal))
            {
                continue;
            }

            if (name.DotKeys.ChildrenCount == 1
                && string.Equals(KeyName(name.DotKeys.GetChild(0)?.Key), ProviderId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var keyValue in document.KeyValues)
        {
            if (keyValue?.Key is { } keySyntax
                && string.Equals(KeyName(keySyntax.Key), "model_providers", StringComparison.Ordinal)
                && keySyntax.DotKeys.ChildrenCount == 0
                && keyValue.Value is InlineTableSyntax inline
                && inline.Items.Any(item => item?.KeyValue?.Key is { } itemKey && string.Equals(KeyName(itemKey.Key), ProviderId, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static string? KeyName(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text,
        StringValueSyntax text => text.Value,
        _ => null,
    };

    private async Task<List<TomlPatchOperation>> BuildOperations(FileInfo path, string? existing, ClientWriteInput input, CancellationToken cancellationToken)
    {
        var operations = new List<TomlPatchOperation>
        {
            new(TomlPatchKind.Set, new TomlPath(["model_provider"]), TomlValue.FromObject(ProviderId)),
            new(TomlPatchKind.Set, new TomlPath(["model_catalog_json"]), TomlValue.FromObject(CatalogFileName)),
        };

        // model 保留用户已选且仍存在于目录中的值，否则落到第一个 Combo；顶层
        // reasoning effort 跟随所选模型的最高档，避免固定值压制更高档位。
        var selected = await SelectModel(path, existing, input, cancellationToken);
        if (selected is not null)
        {
            operations.Add(new TomlPatchOperation(TomlPatchKind.Set, new TomlPath(["model"]), TomlValue.FromObject(selected)));
            var levels = input.Models.First(item => item.Id == selected).ReasoningLevels;
            if (levels.Count > 0)
            {
                operations.Add(new TomlPatchOperation(TomlPatchKind.Set, new TomlPath(["model_reasoning_effort"]), TomlValue.FromObject(levels[^1])));
            }
            else
            {
                operations.Add(new TomlPatchOperation(TomlPatchKind.Delete, new TomlPath(["model_reasoning_effort"])));
            }
        }

        operations.AddRange(
        [
            new(TomlPatchKind.Set, new TomlPath(["model_providers", ProviderId, "name"]), TomlValue.FromObject("LoomX")),
            new(TomlPatchKind.Set, new TomlPath(["model_providers", ProviderId, "base_url"]), TomlValue.FromObject(ClientConfigContent.BaseUrlWithV1(input.BaseUrl))),
            new(TomlPatchKind.Set, new TomlPath(["model_providers", ProviderId, "wire_api"]), TomlValue.FromObject("responses")),
            new(TomlPatchKind.Set, new TomlPath(["model_providers", ProviderId, "experimental_bearer_token"]), TomlValue.FromObject(input.ApiKey)),
            // 历史形态清理：旧 api_key 写法与登录态模式残留，一并移除。
            new(TomlPatchKind.Delete, new TomlPath(["model_providers", ProviderId, "api_key"])),
            new(TomlPatchKind.Delete, new TomlPath(["model_providers", ProviderId, "requires_openai_auth"])),
            new(TomlPatchKind.Delete, new TomlPath(["model_providers", ProviderId, "supports_websockets"])),
            new(TomlPatchKind.Delete, new TomlPath(["model_providers", ProviderId, "http_headers", "x-ai-switch-platform"])),
        ]);
        return operations;
    }

    private async Task<string?> SelectModel(FileInfo path, string? existing, ClientWriteInput input, CancellationToken cancellationToken)
    {
        if (input.Models.Count == 0)
        {
            return null;
        }

        string? current = null;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            var result = await tomlDocumentService.GetAsync(path.FullName, new TomlPath(["model"]), cancellationToken);
            current = result.Found && result.Value is { Kind: TomlValueKind.String } value ? (string)value.Value : null;
        }

        current = current?.Trim();
        return !string.IsNullOrEmpty(current) && input.Models.Any(item => item.Id == current)
            ? current
            : input.Models[0].Id;
    }

    private static string WriteModelCatalog(FileInfo configPath, ClientWriteInput input)
    {
        var catalogPath = new FileInfo(Path.Combine(configPath.DirectoryName ?? ".", CatalogFileName));
        if (catalogPath.Directory is { } directory)
        {
            Directory.CreateDirectory(directory.FullName);
        }

        File.WriteAllText(catalogPath.FullName, CodexModelCatalog.Render(input), ClientConfigFiles.Utf8NoBom);
        return catalogPath.FullName;
    }

    /// <summary>codex 的 ComboModel 目录需要 reasoning 元数据；未声明档位时回落 LoomX Endpoint 的档位表。</summary>
    internal static IReadOnlyList<string> EffectiveReasoningLevels(ClientModelInfo model) =>
        model.ReasoningLevels.Count > 0 ? model.ReasoningLevels : DefaultReasoningLevels;
}

/// <summary>Codex 启动时加载的 model catalog（applied on startup only）。</summary>
public static class CodexModelCatalog
{
    private const string BaseInstructions =
        "You are Codex, a coding agent. You and the user share the same workspace and collaborate to achieve the user's goals.";

    public static string Render(ClientWriteInput input)
    {
        var models = new JsonArray();
        for (var index = 0; index < input.Models.Count; index++)
        {
            var model = input.Models[index];
            var levels = CodexConfigAdapter.EffectiveReasoningLevels(model);
            var contextWindow = model.ContextWindow > 0 ? model.ContextWindow : 128_000;
            models.Add(new JsonObject
            {
                ["additional_speed_tiers"] = new JsonArray(),
                ["availability_nux"] = null,
                ["base_instructions"] = BaseInstructions,
                ["context_window"] = contextWindow,
                ["default_reasoning_level"] = levels[^1],
                ["default_reasoning_summary"] = "none",
                ["description"] = $"LoomX Combo {model.Id}",
                ["display_name"] = model.Id,
                ["effective_context_window_percent"] = 95,
                ["experimental_supported_tools"] = new JsonArray(),
                ["input_modalities"] = model.SupportsImageInput
                    ? new JsonArray("text", "image")
                    : new JsonArray("text"),
                ["max_context_window"] = contextWindow,
                ["priority"] = index + 1,
                ["service_tiers"] = new JsonArray(),
                ["shell_type"] = "shell_command",
                ["slug"] = model.Id,
                ["support_verbosity"] = false,
                ["supported_in_api"] = true,
                ["supported_reasoning_levels"] = new JsonArray(levels.Select(level => (JsonNode?)new JsonObject
                {
                    ["effort"] = level,
                    ["description"] = ReasoningDescription(level),
                }).ToArray()),
                ["supports_image_detail_original"] = false,
                ["supports_parallel_tool_calls"] = false,
                ["supports_reasoning_summaries"] = true,
                ["supports_search_tool"] = false,
                ["truncation_policy"] = new JsonObject { ["mode"] = "bytes", ["limit"] = 10000 },
                ["upgrade"] = null,
                ["visibility"] = "list",
            });
        }

        return ClientConfigContent.ToPrettyJson(new JsonObject { ["models"] = models });
    }

    private static string ReasoningDescription(string effort) => effort switch
    {
        "low" => "Fast responses with lighter reasoning",
        "medium" => "Balances speed and reasoning depth for everyday tasks",
        "high" => "Greater reasoning depth for complex problems",
        "xhigh" => "Extra high reasoning depth for complex problems",
        "max" => "Maximum reasoning depth for the hardest problems",
        "ultra" => "Maximum reasoning with automatic task delegation",
        _ => "Reasoning effort",
    };
}
