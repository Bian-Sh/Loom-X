using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LoomX.Plugins;
using LoomX.Services;

namespace LoomX.Assistant;

/// <summary>六个稳定开发工具；风险授权沿用 ToolRegistry，可信代码还要求显式确认。</summary>
public static class PluginDevelopmentTools
{
    public static readonly IReadOnlyList<string> Names = Array.AsReadOnly(new[] {
        "loomx.plugin.compile", "loomx.plugin.create", "loomx.plugin.diagnostics",
        "loomx.plugin.reload", "loomx.plugin.test", "loomx.plugin.validate",
    });
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static void RegisterAll(ToolRegistry registry, PluginDevelopmentService service)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(service);
        foreach (var name in Names)
        {
            var operation = name[(name.LastIndexOf('.') + 1)..];
            registry.Register(new ToolDefinition
            {
                Name = name,
                Description = Describe(operation),
                ParametersSchema = Schema(operation),
                RiskLevel = operation is "validate" or "diagnostics" ? ToolRiskLevel.Read : ToolRiskLevel.Write,
                SafeArgumentsProjector = _ => new JsonObject { ["operation"] = operation },
                Timeout = TimeSpan.FromMinutes(2),
                Handler = (arguments, ct) => ExecuteAsync(service, operation, arguments, ct),
            });
        }
    }

    private static string Describe(string operation) => operation switch
    {
        "create" => "在指定开发根内创建合法 id 子目录，不覆盖已有包；只生成源码模板，不安装或运行。",
        "validate" => "不执行代码或编译，复用生产规则收集清单、能力、源码路径与负载诊断。",
        "compile" => "使用宿主内置 Roslyn 编译并写入缓存，不执行插件，不要求本机 SDK。",
        "diagnostics" => "读取最近一次安全结构化诊断；首次调用只验证，不执行或编译。",
        "test" => "经用户确认 trust_code 后，在临时数据根和测试 Bridge 执行可信插件；不连接真实 Provider、生产 Vault 或配置，不安装、不 reload。可信 C# 与宿主同进程权限，这不是 OS 沙箱。需确认时使用 assistant.ask_user，不得自行推定授权。",
        _ => "经用户确认 trust_code 后，调用注入生产 Runtime 的原子 reload；失败保留旧实例。会执行当前用户权限的可信 C#，不是 OS 沙箱；未安装插件拒绝。需确认时使用 assistant.ask_user。",
    };

    private static JsonNode Schema(string operation)
    {
        var properties = new JsonObject
        {
            ["plugin_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
            ["plugin_api_major"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
        };
        if (operation == "create") properties["entry_type"] = new JsonObject { ["type"] = "string" };
        if (operation is "test" or "reload") properties["trust_code"] = new JsonObject { ["type"] = "boolean", ["default"] = false };
        if (operation == "test") properties["cases"] = JsonNode.Parse("""
            {"type":"array","maxItems":100,"items":{"type":"object","additionalProperties":false,
            "properties":{"pipeline":{"type":"string"},"payload":{"type":"string"},"expected":{"type":"string"},
            "outcome":{"type":"string","enum":["passed","modified","blocked"]}},"required":["pipeline","payload","expected"]}}
            """);
        return new JsonObject { ["type"] = "object", ["properties"] = properties,
            ["required"] = new JsonArray("plugin_id"), ["additionalProperties"] = false };
    }

    private static async Task<ToolResult> ExecuteAsync(PluginDevelopmentService service, string operation, JsonNode? arguments, CancellationToken ct)
    {
        try
        {
            var args = arguments as JsonObject ?? throw new ArgumentException();
            var allowed = ((JsonObject)Schema(operation)["properties"]!).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            if (args.Any(item => !allowed.Contains(item.Key))) throw new ArgumentException();
            var id = args["plugin_id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException();
            var major = args["plugin_api_major"]?.GetValue<int>() ?? 1;
            var trust = args["trust_code"]?.GetValue<bool>() ?? false;
            var result = operation switch
            {
                "create" => await service.CreateAsync(id, args["entry_type"]?.GetValue<string>() ?? "Development.Plugin", major, ct),
                "validate" => await service.ValidateAsync(id, major, ct),
                "compile" => await service.CompileAsync(id, major, ct),
                "diagnostics" => await service.DiagnosticsAsync(id, major, ct),
                "test" => await service.TestAsync(id, trust, ReadCases(args["cases"]), major, ct),
                _ => await service.ReloadAsync(id, trust, major, ct),
            };
            var content = JsonSerializer.Serialize(result, JsonOptions);
            return result.Success ? ToolResult.Ok(content) : ToolResult.SafeFail(content);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            var failure = new PluginDevelopmentResult(false, operation, null,
                [new("", PluginDiagnosticStage.Manifest, PluginDiagnosticSeverity.Error, "LX_DEVELOPMENT_ARGUMENT_INVALID", "工具参数无效或操作无法完成；已隐藏原始数据。")]);
            return ToolResult.SafeFail(JsonSerializer.Serialize(failure, JsonOptions));
        }
    }

    internal static IReadOnlyList<PluginDevelopmentTestCase>? ReadCases(JsonNode? node)
    {
        if (node is null) return null;
        if (node is not JsonArray array || array.Count > 100) throw new ArgumentException();
        return array.Select(item =>
        {
            if (item is not JsonObject value || value.Any(property => property.Key is not ("pipeline" or "payload" or "expected" or "outcome"))) throw new ArgumentException();
            var outcome = value["outcome"]?.GetValue<string>() ?? "passed";
            var kind = outcome switch { "passed" => PipelineOutcome.Passed, "modified" => PipelineOutcome.Modified, "blocked" => PipelineOutcome.Blocked, _ => throw new ArgumentException() };
            return new PluginDevelopmentTestCase(value["pipeline"]?.GetValue<string>() ?? throw new ArgumentException(),
                value["payload"]?.GetValue<string>() ?? throw new ArgumentException(), value["expected"]?.GetValue<string>() ?? throw new ArgumentException(), kind);
        }).ToArray();
    }
}
