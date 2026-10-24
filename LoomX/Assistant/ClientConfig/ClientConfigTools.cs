using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using LoomX.Services;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// clientconfig.* 工具组：把 LoomX 网关身份写进 AI Agent 客户端自己的配置文件。
/// Endpoint API Key 在服务端内部解析，工具结果只回报写入了哪些文件与模型，
/// 不回显凭据。知识层在 Skills/clients/*，本工具组是其确定性的落盘通道。
/// </summary>
public static class ClientConfigTools
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void RegisterAll(
        ToolRegistry registry,
        ClientConfigService service,
        ConfigurationManagementService configuration,
        IDatabaseConfigurationProvider configurationProvider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configurationProvider);

        registry.Register(new ToolDefinition
        {
            Name = "clientconfig.list_targets",
            Description = "列出支持一键接入 LoomX 网关的 AI Agent 客户端：标识、显示名、配置文件路径、是否需要模型列表、是否需重启。",
            ParametersSchema = Schema("""{"type":"object","properties":{}}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (_, _) => Task.FromResult(Ok(new JsonObject
            {
                ["default_home_root"] = ClientConfigService.DefaultHomeRoot,
                ["targets"] = new JsonArray(service.Targets.Select(target => (JsonNode?)new JsonObject
                {
                    ["target_key"] = target.TargetKey,
                    ["display_name"] = target.DisplayName,
                    ["platform"] = target.Platform,
                    ["requires_model_list"] = target.RequiresModelList,
                    ["restart_required"] = target.RestartRequired,
                    ["config_path"] = service.ResolvePath(target.TargetKey, null).FullName,
                }).ToArray()),
            }.ToJsonString(OutputJsonOptions))),
        });

        registry.Register(new ToolDefinition
        {
            Name = "clientconfig.inspect",
            Description = "检查客户端配置文件的当前状态：missing（不存在）/ unmanaged（存在但未接入）/ managed（已接入 LoomX）/ invalid（解析失败）。不返回文件内容与凭据。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "client":{"type":"string","description":"客户端标识，见 clientconfig.list_targets"},
                  "home_root":{"type":"string","description":"用户主目录覆盖，仅测试或特殊安装使用"}
                },"required":["client"]}
                """),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var inspection = await service.InspectAsync(RequireString(args, "client"), GetString(args, "home_root"), cancellationToken);
                var path = service.ResolvePath(RequireString(args, "client"), GetString(args, "home_root"));
                return Ok(new JsonObject
                {
                    ["client"] = RequireString(args, "client"),
                    ["config_path"] = path.FullName,
                    ["file_status"] = inspection.FileStatus,
                    ["managed"] = inspection.Managed,
                }.ToJsonString(OutputJsonOptions));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "clientconfig.write",
            Description = "把 LoomX 网关写入客户端配置（一键接入）：指向指定 Endpoint 并绑定 Combo 模型。Endpoint API Key 在服务端解析，不回显；既有配置非法时拒绝覆写。写入前自动备份。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "client":{"type":"string","description":"客户端标识，见 clientconfig.list_targets"},
                  "endpoint":{"type":"string","description":"网关 Endpoint key：openai（默认）/ azure / ollama"},
                  "combo_ids":{"type":"array","items":{"type":"string"},"description":"要暴露给客户端的 Combo Guid 列表；缺省使用该 Endpoint 绑定的启用 Combo"},
                  "models":{"type":"array","items":{"type":"object","properties":{
                    "model_id":{"type":"string"},"context_length":{"type":"integer"},"max_tokens":{"type":"integer"},
                    "reasoning_levels":{"type":"array","items":{"type":"string"}},"vision":{"type":"boolean"}
                  },"required":["model_id"]},"description":"显式模型列表，优先于 combo_ids"},
                  "base_url":{"type":"string","description":"覆盖网关地址（一般不填，默认从监听地址+Endpoint 公开路径推导）"},
                  "api_key":{"type":"string","description":"显式提供 Endpoint API Key；默认从 Endpoint 配置解析"},
                  "home_root":{"type":"string","description":"用户主目录覆盖，仅测试或特殊安装使用；默认真实用户主目录"}
                },"required":["client"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            SafeArgumentsProjector = args => new JsonObject
            {
                ["client"] = GetString(args, "client"),
                ["endpoint"] = GetString(args, "endpoint") ?? "openai",
                ["combo_id_count"] = args?["combo_ids"] is JsonArray combos ? combos.Count : 0,
                ["model_count"] = args?["models"] is JsonArray models ? models.Count : 0,
                ["home_root_overridden"] = args?["home_root"] is not null,
            },
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var descriptor = service.Find(RequireString(args, "client"))
                    ?? throw new ArgumentException($"未知客户端标识。可先用 clientconfig.list_targets 查询。");
                var endpoint = await ResolveEndpointAsync(configuration, args, cancellationToken);
                var baseUrl = GetString(args, "base_url")
                    ?? DeriveLoopbackBaseUrl(configurationProvider, endpoint.PublicPath);
                var apiKey = GetString(args, "api_key") ?? endpoint.ApiKey;
                if (GatewayEndpointSettings.RequiresApiKey(endpoint.Key) && string.IsNullOrWhiteSpace(apiKey))
                {
                    return ToolResult.SafeFail("该 Endpoint 尚未配置 API Key。请先在 Endpoint 设置里生成/轮换 Key，或通过 api_key 参数显式提供。");
                }

                var models = await ResolveModelsAsync(configuration, endpoint, args, cancellationToken);
                if (models.Count == 0)
                {
                    return ToolResult.SafeFail("没有可写入的模型：请先用 loomx.create_combo / loomx.add_combo_route 建立 Combo 路由并绑定到该 Endpoint（或显式传入 models）。");
                }

                var outcome = await service.WriteAsync(descriptor.TargetKey, GetString(args, "home_root"), new ClientWriteInput(baseUrl, apiKey!, models), cancellationToken);
                return Ok(new JsonObject
                {
                    ["client"] = outcome.TargetKey,
                    ["display_name"] = descriptor.DisplayName,
                    ["config_path"] = outcome.ConfigPath,
                    ["catalog_path"] = outcome.CatalogPath,
                    ["backup_path"] = outcome.BackupPath,
                    ["changed"] = outcome.Changed,
                    ["restart_required"] = outcome.RestartRequired,
                    ["models_written"] = new JsonArray(outcome.ModelsWritten.Select(model => (JsonNode?)model).ToArray()),
                    ["note"] = "Endpoint API Key 已写入客户端配置文件；文件包含本机凭据，请勿外传。该 Key 不回显。",
                }.ToJsonString(OutputJsonOptions));
            }),
        });
    }

    private static async Task<GatewayEndpointResponse> ResolveEndpointAsync(
        ConfigurationManagementService configuration,
        JsonNode? args,
        CancellationToken cancellationToken)
    {
        var key = GetString(args, "endpoint") ?? "openai";
        var endpoints = await configuration.ListGatewayEndpointsAsync(cancellationToken);
        return endpoints.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Endpoint '{key}' 不存在。可用：openai / azure / ollama。");
    }

    /// <summary>监听地址 + Endpoint 公开路径 → 客户端要的网关根地址（回环优先）。</summary>
    private static string DeriveLoopbackBaseUrl(IDatabaseConfigurationProvider configurationProvider, string publicPath)
    {
        var urls = configurationProvider.Current.Server.Urls;
        if (urls.Count == 0)
        {
            throw new InvalidOperationException("LoomX 服务器未配置监听地址，无法推导网关地址。");
        }

        var root = urls.FirstOrDefault(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == "http"
                && uri.Host is "127.0.0.1" or "localhost")
            ?? FirstLoopbackRewrite(urls)
            ?? urls[0];
        var trimmedRoot = root.TrimEnd('/');
        var trimmedPath = publicPath.TrimEnd('/');
        return trimmedPath.Length == 0 ? trimmedRoot : $"{trimmedRoot}{trimmedPath}";
    }

    /// <summary>0.0.0.0 / * / + 只对本机客户端等价于 127.0.0.1；保留端口改写主机。</summary>
    private static string? FirstLoopbackRewrite(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host is not ("0.0.0.0" or "*" or "+"))
            {
                continue;
            }

            var builder = new UriBuilder(uri) { Host = "127.0.0.1" };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return null;
    }

    private static async Task<IReadOnlyList<ClientModelInfo>> ResolveModelsAsync(
        ConfigurationManagementService configuration,
        GatewayEndpointResponse endpoint,
        JsonNode? args,
        CancellationToken cancellationToken)
    {
        if (args?["models"] is JsonArray explicitModels)
        {
            var models = new List<ClientModelInfo>();
            foreach (var item in explicitModels)
            {
                if (item is not JsonObject model)
                {
                    throw new ArgumentException("models 数组元素必须是对象。");
                }

                var reasoningLevels = model["reasoning_levels"] is JsonArray levels
                    ? levels.Select(node => node?.GetValue<string>() ?? string.Empty).Where(level => level.Length > 0).ToArray()
                    : [];
                models.Add(new ClientModelInfo(
                    RequireString(model, "model_id"),
                    GetInt(model, "context_length", 0),
                    GetInt(model, "max_tokens", 0),
                    reasoningLevels,
                    GetBool(model, "vision", false)));
            }

            return models;
        }

        var combos = await configuration.ListGatewayCombosAsync(cancellationToken);
        var selectedIds = args?["combo_ids"] is JsonArray comboIds
            ? comboIds.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray()
            : endpoint.Combos
                .Where(binding => binding.Enabled && binding.ComboEnabled)
                .Select(binding => binding.ComboId.ToString())
                .ToArray();

        var selected = new List<GatewayComboResponse>();
        foreach (var id in selectedIds)
        {
            var combo = Guid.TryParse(id, out var guid)
                ? combos.FirstOrDefault(item => item.Id == guid)
                : combos.FirstOrDefault(item => string.Equals(item.Name, id, StringComparison.OrdinalIgnoreCase));
            if (combo is null)
            {
                throw new ArgumentException($"Combo '{id}' 不存在。可用 loomx.list_combos 查询。");
            }

            if (!combo.Enabled || combo.IsDeleted)
            {
                continue;
            }

            selected.Add(combo);
        }

        // 模型元数据（上下文窗口/输出上限/视觉）来自 Provider 模型页；按 Combo 顺序去重。
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        var modelMeta = providers.SelectMany(provider => provider.Models)
            .Where(model => model.Enabled)
            .GroupBy(model => model.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var result = new List<ClientModelInfo>();
        var seen = new HashSet<Guid>();
        foreach (var combo in selected)
        {
            foreach (var route in combo.Routes.Where(route => route.Enabled))
            {
                if (!seen.Add(route.ModelId) || !modelMeta.TryGetValue(route.ModelId, out var meta))
                {
                    continue;
                }

                result.Add(new ClientModelInfo(
                    meta.ModelId,
                    meta.ContextLength,
                    meta.MaxTokens,
                    [],
                    meta.Vision));
            }
        }

        return result;
    }

    private static ToolResult Ok(string json) => ToolResult.Ok(json);

    private static async Task<ToolResult> GuardAsync(Func<Task<ToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ClientConfigException exception) when (exception.Code == ClientConfigException.ExistingInvalidCode)
        {
            return ToolResult.SafeFail(ClientConfigService.InvalidExistingMessage);
        }
        catch (ClientConfigException exception)
        {
            return ToolResult.SafeFail(exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            return ToolResult.SafeFail(exception.Message);
        }
    }

    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;

    private static string RequireString(JsonNode? args, string name) =>
        args is JsonObject obj && obj[name]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"缺少必填参数 '{name}'。");

    private static string? GetString(JsonNode? args, string name) =>
        args?[name]?.GetValue<string>() is { Length: > 0 } value ? value : null;

    private static bool GetBool(JsonNode? args, string name, bool fallback) =>
        args?[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static int GetInt(JsonNode? args, string name, int fallback) =>
        args?[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;
}
