using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using LoomX.Services;

namespace LoomX.Assistant;

/// <summary>
/// LoomX 工具组（loomx.* + skill.*）：小助手配置、诊断与连接能力的统一入口。
/// 所有输出经过 SecretBoundary，绝不包含 API Key 明文。
/// </summary>
public static class LoomXTools
{
    private static readonly JsonNode EmptyObjectSchema = JsonNode.Parse("""{"type":"object","properties":{}}""")!;

    // 工具输出面向模型与用户：不转义非 ASCII，保持中文可读、节省 token
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void RegisterAll(
        ToolRegistry registry,
        ConfigurationManagementService configuration,
        IDatabaseConfigurationProvider configurationProvider,
        AssistantTester tester,
        SkillStore skillStore,
        Browser.BrowserSecretVault? secretVault = null,
        GatewayStateHub? gatewayStateHub = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configurationProvider);
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(skillStore);

        RegisterStatusTools(registry, configuration, configurationProvider, gatewayStateHub);
        RegisterProviderTools(registry, configuration, secretVault);
        RegisterModelTools(registry, configuration);
        RegisterComboTools(registry, configuration);
        RegisterEndpointTools(registry, configuration);
        RegisterTestTools(registry, tester);
        RegisterSkillTools(registry, skillStore);
    }

    /// <summary>
    /// 注册 loomx.diagnose：对 Provider 调起 Diagnostic Subagent 做分层诊断。
    /// 助手模型未配置时返回明确错误，不假装诊断。
    /// </summary>
    public static void RegisterDiagnosticTool(
        ToolRegistry registry,
        DiagnosticSubagent subagent,
        Func<CancellationToken, Task<IModelClient?>> modelClientFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(subagent);
        ArgumentNullException.ThrowIfNull(modelClientFactory);

        registry.Register(new ToolDefinition
        {
            Name = "loomx.diagnose",
            Description = "对指定 Provider 调起诊断工人做分层排查（DNS/TCP/TLS/HTTP/Auth/Models/Chat），返回结构化诊断结论。test_provider 失败后用。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"Provider 的 Guid 或 business_id"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Timeout = TimeSpan.FromMinutes(5),
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var modelClient = await modelClientFactory(cancellationToken);
                if (modelClient is null)
                {
                    return ToolResult.Fail(new JsonObject
                    {
                        ["error"] = "assistant_model_not_configured",
                        ["message"] = "AI 助手模型尚未配置，无法调起诊断工人。请在 LoomX 中启用一个 openai 兼容的 Provider 与模型。",
                    }.ToJsonString(OutputJsonOptions));
                }

                var report = await subagent.DiagnoseProviderAsync(modelClient, RequireString(args, "id"), cancellationToken);
                return Ok(report.ToJson());
            }),
        });
    }

    // ---------- loomx.get_status ----------

    private static void RegisterStatusTools(ToolRegistry registry, ConfigurationManagementService configuration, IDatabaseConfigurationProvider configurationProvider, GatewayStateHub? gatewayStateHub)
    {
        registry.Register(new ToolDefinition
        {
            Name = "loomx.get_status",
            Description = "获取 LoomX 整体状态：网关运行状态（running/state/error）、Provider/Model/Endpoint/Combo 数量与启用情况、监听地址、版本。",
            ParametersSchema = EmptyObjectSchema.DeepClone(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (_, cancellationToken) => Ok(await BuildStatusJsonAsync(configuration, configurationProvider, gatewayStateHub, cancellationToken)),
        });
    }

    private static async Task<JsonObject> BuildStatusJsonAsync(ConfigurationManagementService configuration, IDatabaseConfigurationProvider configurationProvider, GatewayStateHub? gatewayStateHub, CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        var endpoints = await configuration.ListGatewayEndpointsAsync(cancellationToken);
        var combos = await configuration.ListGatewayCombosAsync(cancellationToken);
        return new JsonObject
        {
            ["version"] = AppVersion.Current,
            ["listen_urls"] = new JsonArray(configurationProvider.Current.Server.Urls.Select(url => (JsonNode?)JsonValue.Create(url)).ToArray()),
            ["gateway"] = gatewayStateHub is null
                ? new JsonObject
                {
                    ["running"] = null,
                    ["state"] = "unknown",
                }
                : new JsonObject
                {
                    ["running"] = gatewayStateHub.State == GatewayState.Running,
                    ["state"] = gatewayStateHub.State.ToString().ToLowerInvariant(),
                    ["error"] = string.IsNullOrWhiteSpace(gatewayStateHub.Error) ? null : gatewayStateHub.Error,
                },
            ["providers"] = new JsonObject
            {
                ["total"] = providers.Count,
                ["enabled"] = providers.Count(item => item.Enabled),
            },
            ["models"] = new JsonObject
            {
                ["total"] = providers.Sum(item => item.ModelCount),
                ["enabled"] = providers.Sum(item => item.Models.Count(model => model.Enabled)),
            },
            ["endpoints"] = new JsonArray(endpoints.Select(endpoint => (JsonNode?)new JsonObject
            {
                ["key"] = endpoint.Key,
                ["enabled"] = endpoint.Enabled,
                ["combos_bound"] = endpoint.Combos.Count,
            }).ToArray()),
            ["combos"] = new JsonObject
            {
                ["total"] = combos.Count,
                ["enabled"] = combos.Count(item => item.Enabled),
            },
        };
    }

    // ---------- loomx.*_provider ----------

    private static void RegisterProviderTools(ToolRegistry registry, ConfigurationManagementService configuration, Browser.BrowserSecretVault? secretVault)
    {
        registry.Register(new ToolDefinition
        {
            Name = "loomx.list_providers",
            Description = "列出全部 Provider（含模型摘要）。API Key 只返回 secret_ref，不返回明文。",
            ParametersSchema = EmptyObjectSchema.DeepClone(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (_, cancellationToken) => Ok(new JsonObject
            {
                ["providers"] = new JsonArray((await configuration.ListProvidersAsync(cancellationToken)).Select(item => (JsonNode?)ToSafeJson(item, includeModels: false)).ToArray()),
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.get_provider",
            Description = "按 id 或 business_id 获取单个 Provider 详情（含模型列表）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"Provider 的 Guid 或 business_id"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var provider = await FindProviderAsync(configuration, RequireString(args, "id"), cancellationToken);
                return Ok(ToSafeJson(provider, includeModels: true));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.create_provider",
            Description = "创建 Provider。api_key 只会加密存入本地（DPAPI），不会回显。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "business_id":{"type":"string"},"display_name":{"type":"string"},
                  "base_url":{"type":"string","description":"HTTP/HTTPS 绝对地址"},
                  "api_mode":{"type":"string","enum":["openai","anthropic","ollama"]},
                  "enabled":{"type":"boolean"},"api_key":{"type":"string"},
                  "use_proxy":{"type":"boolean"},"model_list_url":{"type":"string"},
                  "endpoint_format":{"type":"string","enum":["responses","chat_completions"]},
                  "headers":{"type":"object","description":"自定义请求头"}
                },"required":["business_id","display_name","base_url","api_mode"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var provider = await configuration.CreateProviderAsync(ReadProviderInput(args, secretVault), cancellationToken);
                return Ok(ToSafeJson(provider, includeModels: false));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.update_provider",
            Description = "更新 Provider（整体替换语义）。clear_api_key=true 时清除已保存的 Key；可用 api_key_secret_ref 引用浏览器收割的 Key。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "id":{"type":"string","description":"Provider Guid"},
                  "business_id":{"type":"string"},"display_name":{"type":"string"},
                  "base_url":{"type":"string"},"api_mode":{"type":"string","enum":["openai","anthropic","ollama"]},
                  "enabled":{"type":"boolean"},"api_key":{"type":"string"},"clear_api_key":{"type":"boolean"},
                  "api_key_secret_ref":{"type":"string","description":"secret://browser/... 形式的引用"},
                  "use_proxy":{"type":"boolean"},"model_list_url":{"type":"string"},
                  "endpoint_format":{"type":"string","enum":["responses","chat_completions"]},
                  "headers":{"type":"object"}
                },"required":["id","business_id","display_name","base_url","api_mode"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var provider = await configuration.UpdateProviderAsync(RequireGuid(args, "id"), ReadProviderInput(args, secretVault), cancellationToken);
                return Ok(ToSafeJson(provider, includeModels: false));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.delete_provider",
            Description = "删除 Provider。仍有模型时需先删除模型（高风险，不可恢复）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"Provider Guid"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Destructive,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var id = RequireGuid(args, "id");
                await configuration.DeleteProviderAsync(id, cancellationToken);
                return Ok(new JsonObject { ["deleted"] = true, ["id"] = JsonValue.Create(id) });
            }),
        });
    }

    // ---------- loomx.*_model ----------

    private static void RegisterModelTools(ToolRegistry registry, ConfigurationManagementService configuration)
    {
        registry.Register(new ToolDefinition
        {
            Name = "loomx.list_models",
            Description = "列出模型，可选按 provider_id 过滤。",
            ParametersSchema = Schema("""{"type":"object","properties":{"provider_id":{"type":"string","description":"Provider Guid 或 business_id，可空"}}}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var providers = await configuration.ListProvidersAsync(cancellationToken);
                var providerFilter = GetString(args, "provider_id");
                var models = providers
                    .Where(provider => providerFilter is null || provider.Id.ToString() == providerFilter || string.Equals(provider.BusinessId, providerFilter, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(provider => provider.Models.Select(model => (JsonNode?)ToSafeJson(model)))
                    .ToArray();
                return Ok(new JsonObject { ["models"] = new JsonArray(models) });
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.get_model",
            Description = "按 id 获取单个模型详情。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"模型 Guid"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var model = await FindModelAsync(configuration, RequireGuid(args, "id"), cancellationToken);
                return Ok(ToSafeJson(model));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.create_model",
            Description = "在指定 Provider 下创建模型。api_key 只会加密存入本地，不会回显。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "provider_id":{"type":"string","description":"Provider Guid"},
                  "model_id":{"type":"string","description":"上游模型 ID，如 gpt-4o"},
                  "display_name":{"type":"string"},"family":{"type":"string"},
                  "context_length":{"type":"integer"},"max_tokens":{"type":"integer"},
                  "vision":{"type":"boolean"},"temperature":{"type":"number"},"top_p":{"type":"number"},
                  "enabled":{"type":"boolean"},"api_key":{"type":"string"},
                  "base_url":{"type":"string"},"api_mode":{"type":"string"},
                  "sort_order":{"type":"integer"}
                },"required":["provider_id","model_id","display_name","family","context_length","max_tokens"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var model = await configuration.CreateModelAsync(RequireGuid(args, "provider_id"), ReadModelInput(args), cancellationToken);
                return Ok(ToSafeJson(model));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.update_model",
            Description = "更新模型（整体替换语义）。clear_api_key=true 时清除已保存的 Key。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "id":{"type":"string","description":"模型 Guid"},
                  "model_id":{"type":"string"},"display_name":{"type":"string"},"family":{"type":"string"},
                  "context_length":{"type":"integer"},"max_tokens":{"type":"integer"},
                  "vision":{"type":"boolean"},"temperature":{"type":"number"},"top_p":{"type":"number"},
                  "enabled":{"type":"boolean"},"api_key":{"type":"string"},"clear_api_key":{"type":"boolean"},
                  "base_url":{"type":"string"},"api_mode":{"type":"string"},"sort_order":{"type":"integer"}
                },"required":["id","model_id","display_name","family","context_length","max_tokens"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var model = await configuration.UpdateModelAsync(RequireGuid(args, "id"), ReadModelInput(args), cancellationToken);
                return Ok(ToSafeJson(model));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.delete_model",
            Description = "删除模型。仍被 Combo 路由引用时需先移除（高风险，不可恢复）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"模型 Guid"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Destructive,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var id = RequireGuid(args, "id");
                await configuration.DeleteModelAsync(id, cancellationToken);
                return Ok(new JsonObject { ["deleted"] = true, ["id"] = JsonValue.Create(id) });
            }),
        });
    }

    // ---------- loomx.*_combo ----------

    private static void RegisterComboTools(ToolRegistry registry, ConfigurationManagementService configuration)
    {
        registry.Register(new ToolDefinition
        {
            Name = "loomx.list_combos",
            Description = "列出全部网关 Combo（含路由与 Endpoint 绑定）。",
            ParametersSchema = EmptyObjectSchema.DeepClone(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (_, cancellationToken) => Ok(new JsonObject
            {
                ["combos"] = new JsonArray((await configuration.ListGatewayCombosAsync(cancellationToken)).Select(item => (JsonNode?)ToSafeJson(item)).ToArray()),
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.get_combo",
            Description = "按 id 或名称获取单个 Combo 详情。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"Combo Guid 或名称"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var combo = await FindComboAsync(configuration, RequireString(args, "id"), cancellationToken);
                return Ok(ToSafeJson(combo));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.create_combo",
            Description = "创建网关 Combo，可同时用 model_ids 挂接路由。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "name":{"type":"string"},"enabled":{"type":"boolean"},"sort_order":{"type":"integer"},
                  "model_ids":{"type":"array","items":{"type":"string"},"description":"按顺序挂接的模型 Guid"}
                },"required":["name"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var combo = await configuration.CreateGatewayComboAsync(
                    new GatewayComboInput(RequireString(args, "name"), GetBool(args, "enabled", true), GetInt(args, "sort_order", 0)),
                    cancellationToken);
                var modelIds = GetGuidArray(args, "model_ids");
                for (var index = 0; index < modelIds.Count; index++)
                {
                    await configuration.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(modelIds[index], true, index), cancellationToken);
                }
                var result = modelIds.Count > 0
                    ? await FindComboAsync(configuration, combo.Id.ToString(), cancellationToken)
                    : combo;
                return Ok(ToSafeJson(result));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.update_combo",
            Description = "更新 Combo 的名称、启用状态与排序。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "id":{"type":"string","description":"Combo Guid"},
                  "name":{"type":"string"},"enabled":{"type":"boolean"},"sort_order":{"type":"integer"}
                },"required":["id","name"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var combo = await configuration.UpdateGatewayComboAsync(
                    RequireGuid(args, "id"),
                    new GatewayComboInput(RequireString(args, "name"), GetBool(args, "enabled", true), GetInt(args, "sort_order", 0)),
                    cancellationToken);
                return Ok(ToSafeJson(combo));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.delete_combo",
            Description = "删除 Combo 及其路由（高风险，不可恢复）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"Combo Guid"}},"required":["id"]}"""),
            RiskLevel = ToolRiskLevel.Destructive,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var id = RequireGuid(args, "id");
                await configuration.DeleteGatewayComboAsync(id, cancellationToken);
                return Ok(new JsonObject { ["deleted"] = true, ["id"] = JsonValue.Create(id) });
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.add_combo_route",
            Description = "向 Combo 追加一个模型路由。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "combo_id":{"type":"string","description":"Combo Guid"},
                  "model_id":{"type":"string","description":"模型 Guid"},
                  "enabled":{"type":"boolean"},"sort_order":{"type":"integer"}
                },"required":["combo_id","model_id"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var route = await configuration.CreateGatewayRouteAsync(
                    RequireGuid(args, "combo_id"),
                    new GatewayRouteInput(RequireGuid(args, "model_id"), GetBool(args, "enabled", true), GetInt(args, "sort_order", 0)),
                    cancellationToken);
                return Ok(ToSafeJson(route));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.remove_combo_route",
            Description = "从 Combo 中移除一个路由。",
            ParametersSchema = Schema("""{"type":"object","properties":{"route_id":{"type":"string","description":"路由 Guid"}},"required":["route_id"]}"""),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var routeId = RequireGuid(args, "route_id");
                await configuration.DeleteGatewayRouteAsync(routeId, cancellationToken);
                return Ok(new JsonObject { ["deleted"] = true, ["id"] = JsonValue.Create(routeId) });
            }),
        });
    }

    // ---------- loomx.*_endpoint ----------

    private static void RegisterEndpointTools(ToolRegistry registry, ConfigurationManagementService configuration)
    {
        registry.Register(new ToolDefinition
        {
            Name = "loomx.list_endpoints",
            Description = "列出网关 Endpoint（ollama/openai/azure，含 Combo 绑定）。",
            ParametersSchema = EmptyObjectSchema.DeepClone(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (_, cancellationToken) => Ok(new JsonObject
            {
                ["endpoints"] = new JsonArray((await configuration.ListGatewayEndpointsAsync(cancellationToken)).Select(item => (JsonNode?)ToSafeJson(item)).ToArray()),
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.get_endpoint",
            Description = "按 key 获取单个 Endpoint 详情（ollama/openai/azure）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"key":{"type":"string","description":"Endpoint key，如 openai"}},"required":["key"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var endpoint = await FindEndpointAsync(configuration, RequireString(args, "key"), cancellationToken);
                return Ok(ToSafeJson(endpoint));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.create_endpoint",
            Description = "创建自定义 Endpoint。当前版本 Endpoint 为系统预置，调用会返回说明。",
            ParametersSchema = Schema("""{"type":"object","properties":{"key":{"type":"string"}}}"""),
            RiskLevel = ToolRiskLevel.Write,
            Handler = (_, _) => Task.FromResult(ToolResult.Fail(
                "LoomX Endpoint 为系统预置（ollama/openai/azure），不支持创建自定义 Endpoint。可用 loomx.update_endpoint 启停、调整 Reasoning Effort 或 Combo 绑定。")),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.update_endpoint",
            Description = "更新 Endpoint：启停、Reasoning Effort（仅 ollama）、Combo 绑定。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "key":{"type":"string","description":"Endpoint key"},
                  "enabled":{"type":"boolean"},
                  "reasoning_effort":{"type":"string","enum":["minimal","low","medium","high"]},
                  "combo_ids":{"type":"array","items":{"type":"string"},"description":"按顺序绑定的 Combo Guid"}
                },"required":["key"]}
                """),
            RiskLevel = ToolRiskLevel.Write,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var key = RequireString(args, "key");
                var current = await FindEndpointAsync(configuration, key, cancellationToken);
                if (GetBool(args, "enabled", current.Enabled) != current.Enabled)
                {
                    current = await configuration.SetGatewayEndpointEnabledAsync(key, !current.Enabled, cancellationToken);
                }
                if (GetString(args, "reasoning_effort") is { } reasoningEffort)
                {
                    current = await configuration.UpdateGatewayEndpointReasoningEffortAsync(key, reasoningEffort, cancellationToken);
                }
                if (args?["combo_ids"] is JsonArray comboIds)
                {
                    current = await configuration.UpdateGatewayEndpointComboBindingsAsync(
                        key,
                        new GatewayEndpointComboSelectionInput(comboIds.Select(item => RequireGuidValue(item, "combo_ids")).ToArray()),
                        cancellationToken);
                }
                return Ok(ToSafeJson(current));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.delete_endpoint",
            Description = "删除 Endpoint。当前版本 Endpoint 为系统预置，调用会返回说明。",
            ParametersSchema = Schema("""{"type":"object","properties":{"key":{"type":"string"}}}"""),
            RiskLevel = ToolRiskLevel.Destructive,
            Handler = (_, _) => Task.FromResult(ToolResult.Fail(
                "LoomX Endpoint 为系统预置（ollama/openai/azure），不可删除。如需停用请用 loomx.update_endpoint 将 enabled 设为 false。")),
        });
    }

    // ---------- loomx.test_* ----------

    private static void RegisterTestTools(ToolRegistry registry, AssistantTester tester)
    {
        registry.Register(new ToolDefinition
        {
            Name = "loomx.test_provider",
            Description = "测试 Provider 连通性：请求其模型列表接口，返回 reachability/鉴权/模型数/耗时与结构化诊断结论。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"Provider Guid 或 business_id"}},"required":["id"]}"""),
            Timeout = TimeSpan.FromSeconds(30),
            RiskLevel = ToolRiskLevel.External,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok(await tester.TestProviderAsync(RequireString(args, "id"), cancellationToken))),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.test_model",
            Description = "测试单个模型：按其协议发起一次最小 Chat 请求，返回成功与否、状态码、耗时与诊断结论。",
            ParametersSchema = Schema("""{"type":"object","properties":{"id":{"type":"string","description":"模型 Guid"}},"required":["id"]}"""),
            Timeout = TimeSpan.FromSeconds(30),
            RiskLevel = ToolRiskLevel.External,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok(await tester.TestModelAsync(RequireGuid(args, "id"), cancellationToken))),
        });

        registry.Register(new ToolDefinition
        {
            Name = "loomx.test_endpoint",
            Description = "检查网关 Endpoint 的结构健康度：启用状态、Combo 绑定、API Key 配置，返回诊断结论。",
            ParametersSchema = Schema("""{"type":"object","properties":{"key":{"type":"string","description":"Endpoint key，如 openai"}},"required":["key"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok(await tester.TestEndpointAsync(RequireString(args, "key"), cancellationToken))),
        });
    }

    // ---------- skill.* ----------

    private static void RegisterSkillTools(ToolRegistry registry, SkillStore skillStore)
    {
        registry.Register(new ToolDefinition
        {
            Name = "skill.list",
            Description = "列出可用 Skill（知识包：providers/relays/clients/loomx）。",
            ParametersSchema = EmptyObjectSchema.DeepClone(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (_, _) => Task.FromResult(Ok(new JsonObject
            {
                ["skills"] = new JsonArray(skillStore.List().Select(manifest => (JsonNode?)new JsonObject
                {
                    ["category"] = manifest.Category,
                    ["name"] = manifest.Name,
                    ["description"] = manifest.Description,
                    ["when_to_use"] = manifest.WhenToUse,
                }).ToArray()),
            })),
        });

        registry.Register(new ToolDefinition
        {
            Name = "skill.load",
            Description = "加载指定 Skill 的知识内容（识别、配置路径、修改/备份/恢复/验证/测试方法）。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "category":{"type":"string","description":"如 providers、relays、clients"},
                  "name":{"type":"string","description":"如 generic-openai-compatible、new-api、codex"}
                },"required":["category","name"]}
                """),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (args, _) => Task.FromResult(Guard(() =>
            {
                var document = skillStore.Load(RequireString(args, "category"), RequireString(args, "name"))
                    ?? throw new KeyNotFoundException("Skill 不存在，可先用 skill.list 查看可用列表。");
                return Ok(new JsonObject
                {
                    ["category"] = document.Manifest.Category,
                    ["name"] = document.Manifest.Name,
                    ["description"] = document.Manifest.Description,
                    ["content"] = document.Content,
                });
            })),
        });
    }

    // ---------- 安全序列化（SecretBoundary 出口） ----------

    private static JsonObject ToSafeJson(ProviderResponse provider, bool includeModels)
    {
        var json = new JsonObject
        {
            ["id"] = JsonValue.Create(provider.Id),
            ["business_id"] = provider.BusinessId,
            ["display_name"] = provider.DisplayName,
            ["base_url"] = provider.BaseUrl,
            ["api_mode"] = provider.ApiMode,
            ["enabled"] = provider.Enabled,
            ["use_proxy"] = provider.UseProxy,
            ["endpoint_format"] = provider.EndpointFormat,
            ["model_list_url"] = provider.ModelListUrl,
            ["model_count"] = provider.ModelCount,
            ["api_key"] = SecretBoundary.Describe(provider.HasApiKey, SecretBoundary.ProviderApiKeyRef(provider.BusinessId)),
        };
        if (includeModels)
        {
            json["models"] = new JsonArray(provider.Models.Select(model => (JsonNode?)ToSafeJson(model)).ToArray());
        }
        return json;
    }

    private static JsonObject ToSafeJson(ModelResponse model) => new()
    {
        ["id"] = JsonValue.Create(model.Id),
        ["provider_id"] = model.ProviderId,
        ["model_id"] = model.ModelId,
        ["display_name"] = model.DisplayName,
        ["family"] = model.Family,
        ["base_url"] = model.BaseUrl,
        ["api_mode"] = model.ApiMode,
        ["context_length"] = model.ContextLength,
        ["max_tokens"] = model.MaxTokens,
        ["vision"] = model.Vision,
        ["temperature"] = model.Temperature is double temperature ? JsonValue.Create(temperature) : null,
        ["top_p"] = model.TopP is double topP ? JsonValue.Create(topP) : null,
        ["enabled"] = model.Enabled,
        ["sort_order"] = model.SortOrder,
        ["api_key"] = SecretBoundary.Describe(model.HasApiKey, SecretBoundary.ModelApiKeyRef(model.Id)),
    };

    private static JsonObject ToSafeJson(GatewayComboResponse combo) => new()
    {
        ["id"] = JsonValue.Create(combo.Id),
        ["name"] = combo.Name,
        ["enabled"] = combo.Enabled,
        ["sort_order"] = combo.SortOrder,
        ["routes"] = new JsonArray(combo.Routes.Select(route => (JsonNode?)ToSafeJson(route)).ToArray()),
        ["endpoints"] = new JsonArray(combo.Endpoints.Select(endpoint => (JsonNode?)new JsonObject
        {
            ["key"] = endpoint.EndpointKey,
            ["display_name"] = endpoint.DisplayName,
            ["enabled"] = endpoint.Enabled,
            ["sort_order"] = endpoint.SortOrder,
        }).ToArray()),
    };

    private static JsonObject ToSafeJson(GatewayRouteResponse route) => new()
    {
        ["id"] = JsonValue.Create(route.Id),
        ["combo_id"] = JsonValue.Create(route.ComboId),
        ["model_id"] = JsonValue.Create(route.ModelId),
        ["model_name"] = route.ModelName,
        ["provider_name"] = route.ProviderName,
        ["enabled"] = route.Enabled,
        ["sort_order"] = route.SortOrder,
    };

    private static JsonObject ToSafeJson(GatewayEndpointResponse endpoint) => new()
    {
        ["key"] = endpoint.Key,
        ["display_name"] = endpoint.DisplayName,
        ["public_path"] = endpoint.PublicPath,
        ["enabled"] = endpoint.Enabled,
        ["reasoning_effort"] = endpoint.ReasoningEffort,
        ["api_key"] = SecretBoundary.Describe(!string.IsNullOrWhiteSpace(endpoint.ApiKey), SecretBoundary.EndpointApiKeyRef(endpoint.Key)),
        ["combos"] = new JsonArray(endpoint.Combos.Select(combo => (JsonNode?)new JsonObject
        {
            ["combo_id"] = JsonValue.Create(combo.ComboId),
            ["name"] = combo.Name,
            ["combo_enabled"] = combo.ComboEnabled,
            ["enabled"] = combo.Enabled,
            ["sort_order"] = combo.SortOrder,
        }).ToArray()),
    };

    // ---------- 参数读取与错误处理 ----------

    private static ProviderInput ReadProviderInput(JsonNode? args, Browser.BrowserSecretVault? secretVault = null)
    {
        // api_key 直传（用户场景）；api_key_secret_ref 由浏览器收割入库后引用（中转站场景），
        // 两种途径的明文都只在服务端内部流转，绝不回显到模型上下文。
        var apiKey = GetString(args, "api_key");
        var secretRef = GetString(args, "api_key_secret_ref");
        if (apiKey is null && secretRef is not null)
        {
            if (secretVault is null || !secretVault.TryResolve(secretRef, out apiKey!))
            {
                throw new ArgumentException($"secret_ref 无法解析（不存在或已失效）：{secretRef}");
            }
        }

        return new ProviderInput(
            RequireString(args, "business_id"),
            RequireString(args, "display_name"),
            RequireString(args, "base_url"),
            RequireString(args, "api_mode"),
            GetBool(args, "enabled", true),
            apiKey,
            GetBool(args, "clear_api_key", false),
            GetStringDictionary(args, "headers"),
            GetBool(args, "use_proxy", false),
            GetString(args, "model_list_url"),
            GetString(args, "endpoint_format") ?? "responses");
    }

    private static ModelInput ReadModelInput(JsonNode? args) => new(
        RequireString(args, "model_id"),
        RequireString(args, "display_name"),
        null,
        RequireString(args, "family"),
        GetString(args, "base_url"),
        GetString(args, "api_mode"),
        GetInt(args, "context_length", 0),
        GetInt(args, "max_tokens", 0),
        GetBool(args, "vision", false),
        GetDouble(args, "temperature"),
        GetDouble(args, "top_p"),
        GetBool(args, "enabled", true),
        GetString(args, "api_key"),
        GetBool(args, "clear_api_key", false),
        GetStringDictionary(args, "headers"),
        null,
        SortOrder: GetNullableInt(args, "sort_order"));

    private static async Task<ProviderResponse> FindProviderAsync(ConfigurationManagementService configuration, string idOrBusinessId, CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        return Guid.TryParse(idOrBusinessId, out var id)
            ? providers.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException($"Provider '{idOrBusinessId}' 不存在。")
            : providers.FirstOrDefault(item => string.Equals(item.BusinessId, idOrBusinessId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Provider '{idOrBusinessId}' 不存在。");
    }

    private static async Task<ModelResponse> FindModelAsync(ConfigurationManagementService configuration, Guid id, CancellationToken cancellationToken)
    {
        var providers = await configuration.ListProvidersAsync(cancellationToken);
        return providers.SelectMany(item => item.Models).FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException("模型不存在。");
    }

    private static async Task<GatewayComboResponse> FindComboAsync(ConfigurationManagementService configuration, string idOrName, CancellationToken cancellationToken)
    {
        var combos = await configuration.ListGatewayCombosAsync(cancellationToken);
        return Guid.TryParse(idOrName, out var id)
            ? combos.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException($"Combo '{idOrName}' 不存在。")
            : combos.FirstOrDefault(item => string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Combo '{idOrName}' 不存在。");
    }

    private static async Task<GatewayEndpointResponse> FindEndpointAsync(ConfigurationManagementService configuration, string key, CancellationToken cancellationToken)
    {
        var endpoints = await configuration.ListGatewayEndpointsAsync(cancellationToken);
        return endpoints.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Endpoint '{key}' 不存在。");
    }

    private static ToolResult Ok(JsonObject json) => ToolResult.Ok(json.ToJsonString(OutputJsonOptions));

    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;

    private static async Task<ToolResult> GuardAsync(Func<Task<ToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException or InvalidOperationException or JsonException or FormatException)
        {
            return ToolResult.Fail(exception.Message);
        }
    }

    private static ToolResult Guard(Func<ToolResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException or InvalidOperationException or JsonException or FormatException)
        {
            return ToolResult.Fail(exception.Message);
        }
    }

    private static string RequireString(JsonNode? args, string name) =>
        GetString(args, name) ?? throw new ArgumentException($"缺少必填参数 '{name}'。");

    private static string? GetString(JsonNode? args, string name) =>
        args?[name]?.GetValue<string>() is { Length: > 0 } value ? value : null;

    private static bool GetBool(JsonNode? args, string name, bool fallback) =>
        args?[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static int GetInt(JsonNode? args, string name, int fallback) =>
        args?[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;

    private static int? GetNullableInt(JsonNode? args, string name) =>
        args?[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;

    private static double? GetDouble(JsonNode? args, string name) =>
        args?[name] is JsonValue value && value.TryGetValue<double>(out var result) ? result : null;

    private static Guid RequireGuid(JsonNode? args, string name)
    {
        var value = RequireString(args, name);
        return Guid.TryParse(value, out var guid) ? guid : throw new ArgumentException($"参数 '{name}' 不是有效的 Guid。");
    }

    private static Guid RequireGuidValue(JsonNode? node, string name) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var guid)
            ? guid
            : throw new ArgumentException($"参数 '{name}' 含有无效的 Guid。");

    private static IReadOnlyList<Guid> GetGuidArray(JsonNode? args, string name) =>
        args?[name] is JsonArray array
            ? array.Select(item => RequireGuidValue(item, name)).ToArray()
            : [];

    private static Dictionary<string, string>? GetStringDictionary(JsonNode? args, string name)
    {
        if (args?[name] is not JsonObject jsonObject) return null;
        var dictionary = new Dictionary<string, string>();
        foreach (var pair in jsonObject)
        {
            if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) dictionary[pair.Key] = text;
        }
        return dictionary;
    }
}
