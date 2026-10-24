using System.Text.Json.Nodes;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// ZCode 适配器：把 LoomX 写进 ~/.zcode/v2/provider_config.json。
/// 移植自 ai-switch ZCodeAdapter。要点：
/// - ZCode 3.14 起从版本化 store 读 provider 注册表（config.json 只是一次性迁移源）；
/// - 全 schema 严格，未知键会被整份拒绝，托管标记只能挂在客户端自有字段
///   api.headers["x-loomx-managed"] 上；
/// - 同一份文件承载 codex/claude 两个平台条目，收养与 inspect 都按平台标记区分；
/// - provider/model 规则整表替换自己的条目，同时清掉自己遗留的 manual 规则
///   （同一 provider/model 只能声明一种，否则 ZCode 拒绝整份文件）。
/// </summary>
public sealed class ZCodeConfigAdapter : IClientConfigAdapter
{
    private const string ManagedHeader = "x-loomx-managed";
    private const string ApiTypeOpenAI = "openai-responses";
    private const string ApiTypeAnthropic = "anthropic-messages";
    private const string ProviderGroup = "standard-personal";
    private const string ConfigFileName = "provider_config.json";

    private readonly string platform;
    private readonly string apiType;
    private readonly string baseUrlSuffix;
    private readonly string fallbackProviderId;
    private readonly string displayName;

    private ZCodeConfigAdapter(
        string targetKey,
        string platform,
        string apiType,
        string baseUrlSuffix,
        string fallbackProviderId,
        string displayName)
    {
        this.platform = platform;
        this.apiType = apiType;
        this.baseUrlSuffix = baseUrlSuffix;
        this.fallbackProviderId = fallbackProviderId;
        this.displayName = displayName;
        Descriptor = new ClientTargetDescriptor(targetKey, "ZCode", RequiresModelList: true, RestartRequired: true, platform);
    }

    public static ZCodeConfigAdapter Codex() => new(
        "zcode-codex", "codex", ApiTypeOpenAI, "/v1", "loomx-codex", "LoomX (Codex)");

    public static ZCodeConfigAdapter Claude() => new(
        "zcode-claude", "claude", ApiTypeAnthropic, "", "loomx-claude", "LoomX (Claude)");

    public ClientTargetDescriptor Descriptor { get; }

    /// <summary>
    /// ZCode 自身的数据根解析链：ZCODE_DATA_BASE_DIR → homedir ponyfill（HOME 优先）。
    /// 仅当 homeRoot 就是真实用户主目录时才考虑环境变量重定向；测试传入的 fake home
    /// 原样返回，保证测试封闭。
    /// </summary>
    public FileInfo ResolvePath(string homeRoot)
    {
        var baseDir = ClientHome.ResolveBase("ZCODE_DATA_BASE_DIR", homeRoot);
        return new FileInfo(Path.Combine(baseDir, ".zcode", "v2", ConfigFileName));
    }

    private string BaseUrl(string baseUrl) =>
        baseUrlSuffix.Length == 0 ? ClientConfigContent.BaseUrlRoot(baseUrl) : ClientConfigContent.BaseUrlWithV1(baseUrl);

    private static string? ProviderIdOf(JsonNode? rule) => rule?["providerId"]?.GetValue<string>();

    private bool OwnsRule(JsonNode rule) =>
        MarkerOf(rule) == platform || ProviderIdOf(rule) == fallbackProviderId;

    private static string? MarkerOf(JsonNode? rule) =>
        rule?["config"]?["api"]?["headers"]?[ManagedHeader]?.GetValue<string>();

    private string? AdoptionTarget(JsonArray rules, ClientWriteInput input)
    {
        var marked = rules.FirstOrDefault(rule => MarkerOf(rule) == platform);
        if (ProviderIdOf(marked) is { } markedId)
        {
            return markedId;
        }

        if (rules.Any(rule => ProviderIdOf(rule) == fallbackProviderId))
        {
            return fallbackProviderId;
        }

        var expectedBase = BaseUrl(input.BaseUrl);
        foreach (var rule in rules)
        {
            var baseMatches = rule?["config"]?["api"]?["baseUrl"]?.GetValue<string>() is { } value
                && value.Trim().TrimEnd('/') == expectedBase;
            var apiKey = rule?["config"]?["access"]?["apiKey"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (baseMatches && apiKey.Length > 0 && apiKey == input.ApiKey && ProviderIdOf(rule) is { } id)
            {
                return id;
            }
        }

        return null;
    }

    private JsonObject ModelRule(string providerId, ClientModelInfo model)
    {
        var properties = new JsonObject();
        // contextWindow 与 maxOutputTokens.max 在 ZCode schema 里是正整数；
        // 未声明（0）就不写，写零会被整份拒绝。
        if (model.ContextWindow > 0)
        {
            properties["contextWindow"] = model.ContextWindow;
        }

        properties["inputFormat"] = new JsonObject
        {
            ["supportsText"] = true,
            ["supportsImage"] = model.SupportsImageInput,
        };

        var optionSpecs = new JsonObject();
        if (model.MaxOutputTokens > 0)
        {
            optionSpecs["maxOutputTokens"] = new JsonObject { ["max"] = model.MaxOutputTokens };
        }

        // values 是自由列表，档位名原样透传；无档位的模型不写该块，避免出现空控件。
        if (model.ReasoningLevels.Count > 0)
        {
            optionSpecs["reasoningLevel"] = new JsonObject
            {
                ["values"] = new JsonArray(model.ReasoningLevels.Select(level => (JsonNode?)level).ToArray()),
            };
        }

        return new JsonObject
        {
            ["modelId"] = model.Id,
            ["providerId"] = providerId,
            ["config"] = new JsonObject
            {
                ["properties"] = properties,
                ["optionSpecs"] = optionSpecs,
            },
        };
    }

    public Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken)
    {
        var config = ClientConfigContent.ParseExistingObject(path.FullName, existing);

        // 版本化 store：客户端已携带的版本不动，只在新文件上补默认版本。
        if (config["schemaVersion"] is null)
        {
            config["schemaVersion"] = 1;
        }

        var store = ClientConfigContent.GetOrCreateObject(config, "config", path.FullName);

        var providerRulesContainer = ClientConfigContent.GetOrCreateObject(store, "providerConfigRules", path.FullName);
        var providerRules = EnsureArray(providerRulesContainer, "providerRules", path.FullName);

        var providerId = AdoptionTarget(providerRules, input) ?? fallbackProviderId;
        var existingRule = providerRules.FirstOrDefault(rule => ProviderIdOf(rule) == providerId) as JsonObject;

        // 用户改过的名字保留；只给无名条目补默认名。用户手加的 header 也保留，只强制自己的标记。
        // 深拷贝再改写：JsonNode 不能同时挂在旧条目和新条目两棵树上。
        var name = existingRule?["providerName"]?.GetValue<string>() is { Length: > 0 } preservedName
            ? preservedName
            : displayName;
        var headers = (existingRule?["config"]?["api"]?["headers"] as JsonObject)?.DeepClone() ?? new JsonObject();
        headers[ManagedHeader] = platform;

        var entryConfig = (existingRule?["config"] as JsonObject)?.DeepClone() ?? new JsonObject();
        ((IDictionary<string, JsonNode?>)entryConfig).Remove("builtinModelIds");
        entryConfig["group"] = ProviderGroup;
        entryConfig["access"] = new JsonObject { ["type"] = "api-key", ["apiKey"] = input.ApiKey };
        entryConfig["api"] = new JsonObject
        {
            ["type"] = apiType,
            ["baseUrl"] = BaseUrl(input.BaseUrl),
            ["headers"] = headers,
        };

        // 模型列表属于 LoomX，整表替换；手工加进来的模型会被移除而不是合并。
        var modelIds = new JsonArray(input.Models.Select(model => (JsonNode?)model.Id).ToArray());
        entryConfig["personalModelIds"] = modelIds.DeepClone();
        entryConfig["modelOrder"] = modelIds.DeepClone();

        var entry = new JsonObject
        {
            ["providerId"] = providerId,
            ["providerName"] = name,
            ["config"] = entryConfig,
        };
        var entryIndex = providerRules.AsEnumerable().ToList().FindIndex(rule => ProviderIdOf(rule) == providerId);
        if (entryIndex >= 0)
        {
            providerRules[entryIndex] = entry;
        }
        else
        {
            providerRules.Add(entry);
        }

        var modelRulesContainer = ClientConfigContent.GetOrCreateObject(store, "modelConfigRules", path.FullName);
        if (modelRulesContainer["manualProviderModelRules"] is not JsonArray manualRules)
        {
            if (modelRulesContainer.ContainsKey("manualProviderModelRules"))
            {
                throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"manualProviderModelRules 必须是数组，拒绝覆写：{path.FullName}");
            }

            manualRules = new JsonArray();
            modelRulesContainer["manualProviderModelRules"] = manualRules;
        }

        for (var index = manualRules.Count - 1; index >= 0; index--)
        {
            if (ProviderIdOf(manualRules[index]) == providerId)
            {
                manualRules.RemoveAt(index);
            }
        }

        if (modelRulesContainer["providerModelRules"] is not JsonArray providerModelRules)
        {
            if (modelRulesContainer.ContainsKey("providerModelRules"))
            {
                throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"providerModelRules 必须是数组，拒绝覆写：{path.FullName}");
            }

            providerModelRules = new JsonArray();
            modelRulesContainer["providerModelRules"] = providerModelRules;
        }

        for (var index = providerModelRules.Count - 1; index >= 0; index--)
        {
            if (ProviderIdOf(providerModelRules[index]) == providerId)
            {
                providerModelRules.RemoveAt(index);
            }
        }

        foreach (var model in input.Models)
        {
            providerModelRules.Add(ModelRule(providerId, model));
        }

        return Task.FromResult(new ClientAdapterWriteResult(ClientConfigContent.ToPrettyJson(config), null, true));
    }

    public ClientConfigInspection Inspect(FileInfo path, string? existing)
    {
        if (existing is null)
        {
            return new ClientConfigInspection("missing", Managed: false);
        }

        try
        {
            var config = ClientConfigContent.ParseExistingObject(path.FullName, existing);
            if (config["config"] is not JsonObject store)
            {
                return new ClientConfigInspection("invalid", Managed: false);
            }

            var managed = store["providerConfigRules"]?["providerRules"] is JsonArray rules
                && rules.Any(rule => rule is not null && OwnsRule(rule));
            return new ClientConfigInspection(managed ? "managed" : "unmanaged", managed);
        }
        catch (ClientConfigException)
        {
            return new ClientConfigInspection("invalid", Managed: false);
        }
    }

    private static JsonArray EnsureArray(JsonObject parent, string key, string path)
    {
        if (parent[key] is not JsonArray array)
        {
            if (parent.ContainsKey(key))
            {
                throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"键 '{key}' 已存在且不是数组，拒绝覆写：{path}");
            }

            array = new JsonArray();
            parent[key] = array;
        }

        return array;
    }
}

/// <summary>
/// Electron 桌面客户端的数据根重定向解析（移植自 ai-switch client_home.rs）：
/// 这类客户端按 HOME 优先的 ponyfill 解析家目录，Windows 上第三方软件导出的
/// 全局 HOME 会把配置搬去别处。只有调用方传入真实用户主目录时才应用环境变量；
/// 测试 fixture 目录原样透传，测试在任何机器上都封闭。
/// </summary>
public static class ClientHome
{
    public static string ResolveBase(string baseDirVariable, string homeRoot)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile) || !SamePath(homeRoot, profile))
        {
            return homeRoot;
        }

        if (Environment.GetEnvironmentVariable(baseDirVariable) is { Length: > 0 } baseDir)
        {
            var trimmed = baseDir.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            return profile;
        }

        return SamePath(home, profile) ? profile : home;
    }

    /// <summary>Windows 路径比较：忽略大小写与尾分隔符。</summary>
    public static bool SamePath(string left, string right)
    {
        static string Normalize(string path) => path.TrimEnd('/', '\\').ToLowerInvariant();
        return Normalize(left) == Normalize(right);
    }
}
