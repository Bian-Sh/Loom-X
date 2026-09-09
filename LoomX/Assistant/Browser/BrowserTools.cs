using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.Assistant.Browser;

/// <summary>
/// browser.* 工具组：通过 Browser Bridge 操作用户自己 Chrome 中的自动化标签页。
/// 读类结果（read/network）统一经过 BrowserSecretHarvester，Secret 以 secret_ref 呈现。
/// </summary>
public static class BrowserTools
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void RegisterAll(ToolRegistry registry, IBrowserBridge bridge, BrowserSecretVault vault)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(vault);

        registry.Register(new ToolDefinition
        {
            Name = "browser.tabs",
            Description = "列出 LoomX 自动化标签页（仅限 Extension 登记的目标，不含用户其他标签页）。",
            ParametersSchema = Schema("""{"type":"object","properties":{}}"""),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (_, _) => Task.FromResult(Ok(new JsonObject
            {
                ["extension_connected"] = bridge.IsExtensionConnected,
                ["targets"] = new JsonArray(bridge.Targets.Select(target => (JsonNode?)new JsonObject
                {
                    ["target_id"] = target.TargetId,
                    ["session_id"] = target.SessionId,
                    ["tab_id"] = target.TabId,
                    ["url"] = target.Url,
                    ["title"] = target.Title,
                }).ToArray()),
            })),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.open",
            Description = "在用户 Chrome 中打开新的自动化标签页，返回 target_id 与 session_id。",
            ParametersSchema = Schema("""{"type":"object","properties":{"url":{"type":"string","description":"HTTP/HTTPS 绝对地址"}},"required":["url"]}"""),
            RiskLevel = ToolRiskLevel.External,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var url = RequireString(args, "url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                {
                    return Fail("invalid_url", "url 必须是 HTTP/HTTPS 绝对地址。");
                }

                var result = await bridge.SendCommandAsync(BrowserProtocol.TargetCreateTarget, new JsonObject { ["url"] = url }, cancellationToken);
                return Ok(result.AsObject());
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.read",
            Description = "读取页面内容。mode: text(可见文本，默认) / html / markdown。结果中的 API Key 会被收割为 secret_ref。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "session_id":{"type":"string"},"mode":{"type":"string","enum":["text","html","markdown"]},
                  "selector":{"type":"string","description":"可选 CSS 选择器，只读取匹配元素"},
                  "max_length":{"type":"integer","description":"最长字符数，默认 20000"}
                },"required":["session_id"]}
                """),
            RiskLevel = ToolRiskLevel.Read,
            Timeout = TimeSpan.FromSeconds(45),
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var result = await bridge.SendSessionCommandAsync(
                    RequireString(args, "session_id"),
                    "Page.read",
                    new JsonObject
                    {
                        ["mode"] = GetString(args, "mode") ?? "text",
                        ["selector"] = GetString(args, "selector"),
                        ["maxLength"] = GetInt(args, "max_length", 20000),
                    },
                    cancellationToken);
                return Ok(BrowserSecretHarvester.Harvest(result, vault).AsObject());
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.click",
            Description = "点击页面元素（CSS 选择器）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"session_id":{"type":"string"},"selector":{"type":"string"}},"required":["session_id","selector"]}"""),
            RiskLevel = ToolRiskLevel.External,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok((await bridge.SendSessionCommandAsync(
                    RequireString(args, "session_id"),
                    "Page.click",
                    new JsonObject { ["selector"] = RequireString(args, "selector") },
                    cancellationToken)).AsObject())),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.type",
            Description = "在输入框中填入文本（CSS 选择器）。不要用于输入 API Key——Key 应由网页复制后经收割入库。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "session_id":{"type":"string"},"selector":{"type":"string"},
                  "text":{"type":"string"},"clear":{"type":"boolean","description":"先清空，默认 true"}
                },"required":["session_id","selector","text"]}
                """),
            RiskLevel = ToolRiskLevel.External,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok((await bridge.SendSessionCommandAsync(
                    RequireString(args, "session_id"),
                    "Page.type",
                    new JsonObject
                    {
                        ["selector"] = RequireString(args, "selector"),
                        ["text"] = RequireString(args, "text"),
                        ["clear"] = GetBool(args, "clear", true),
                    },
                    cancellationToken)).AsObject())),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.wait",
            Description = "等待页面条件：selector 出现 / 固定毫秒 / 页面加载完成。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "session_id":{"type":"string"},
                  "selector":{"type":"string","description":"等待出现的 CSS 选择器"},
                  "milliseconds":{"type":"integer","description":"固定等待毫秒数"},
                  "timeout_ms":{"type":"integer","description":"最长等待，默认 15000"}
                },"required":["session_id"]}
                """),
            RiskLevel = ToolRiskLevel.Read,
            Timeout = TimeSpan.FromSeconds(60),
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok((await bridge.SendSessionCommandAsync(
                    RequireString(args, "session_id"),
                    "Page.wait",
                    new JsonObject
                    {
                        ["selector"] = GetString(args, "selector"),
                        ["milliseconds"] = GetInt(args, "milliseconds", 0),
                        ["timeoutMs"] = GetInt(args, "timeout_ms", 15000),
                    },
                    cancellationToken)).AsObject())),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.screenshot",
            Description = "截取页面截图，返回 PNG 的 base64（不含任何 Secret 区域以外的内容）。",
            ParametersSchema = Schema("""{"type":"object","properties":{"session_id":{"type":"string"},"full_page":{"type":"boolean"}},"required":["session_id"]}"""),
            RiskLevel = ToolRiskLevel.Read,
            Timeout = TimeSpan.FromSeconds(45),
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok((await bridge.SendSessionCommandAsync(
                    RequireString(args, "session_id"),
                    "Page.captureScreenshot",
                    new JsonObject { ["fullPage"] = GetBool(args, "full_page", false) },
                    cancellationToken)).AsObject())),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.network",
            Description = "读取该自动化标签页最近捕获的网络请求（XHR/fetch）：URL、方法、状态码、Model 线索。Authorization 等 Secret 一律替换为 secret_ref。",
            ParametersSchema = Schema("""
                {"type":"object","properties":{
                  "session_id":{"type":"string"},
                  "url_contains":{"type":"string","description":"URL 子串过滤"},
                  "limit":{"type":"integer","description":"最多返回条数，默认 50"}
                },"required":["session_id"]}
                """),
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
            {
                var result = await bridge.SendSessionCommandAsync(
                    RequireString(args, "session_id"),
                    "Network.getRecent",
                    new JsonObject
                    {
                        ["urlContains"] = GetString(args, "url_contains"),
                        ["limit"] = GetInt(args, "limit", 50),
                    },
                    cancellationToken);
                return Ok(BrowserSecretHarvester.Harvest(result, vault).AsObject());
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "browser.close",
            Description = "关闭自动化标签页。",
            ParametersSchema = Schema("""{"type":"object","properties":{"target_id":{"type":"string"}},"required":["target_id"]}"""),
            RiskLevel = ToolRiskLevel.External,
            Handler = async (args, cancellationToken) => await GuardAsync(async () =>
                Ok((await bridge.SendCommandAsync(
                    BrowserProtocol.TargetCloseTarget,
                    new JsonObject { ["targetId"] = RequireString(args, "target_id") },
                    cancellationToken)).AsObject())),
        });
    }

    // ---------- 参数与结果辅助 ----------

    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;

    private static ToolResult Ok(JsonObject json) => ToolResult.Ok(json.ToJsonString(OutputJsonOptions));

    private static ToolResult Fail(string code, string message) =>
        ToolResult.Fail(new JsonObject { ["error"] = code, ["message"] = message }.ToJsonString(OutputJsonOptions));

    private static async Task<ToolResult> GuardAsync(Func<Task<ToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BrowserBridgeException exception)
        {
            return Fail(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Fail("invalid_arguments", exception.Message);
        }
    }

    private static string RequireString(JsonNode? args, string name)
    {
        var value = GetString(args, name);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"缺少必需参数：{name}");
        return value;
    }

    private static string? GetString(JsonNode? args, string name) =>
        args is JsonObject jsonObject && jsonObject.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;

    private static int GetInt(JsonNode? args, string name, int defaultValue)
    {
        if (args is JsonObject jsonObject && jsonObject.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var number)) return number;
            if (jsonValue.TryGetValue<long>(out var bigNumber)) return (int)bigNumber;
        }

        return defaultValue;
    }

    private static bool GetBool(JsonNode? args, string name, bool defaultValue) =>
        args is JsonObject jsonObject && jsonObject.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var flag)
            ? flag
            : defaultValue;
}
