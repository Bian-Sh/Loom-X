using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using LoomX.Services;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// 结构化诊断结果。只含安全摘要，不含 Secret。
/// </summary>
public sealed record DiagnosticReport(
    bool Reachable,
    bool Authenticated,
    int ModelsFound,
    bool ChatTest,
    bool ProxyRequired,
    string Diagnosis,
    string Summary)
{
    public JsonObject ToJson() => new()
    {
        ["reachable"] = Reachable,
        ["authenticated"] = Authenticated,
        ["models_found"] = ModelsFound,
        ["chat_test"] = ChatTest,
        ["proxy_required"] = ProxyRequired,
        ["diagnosis"] = Diagnosis,
        ["summary"] = Summary,
    };

    public static DiagnosticReport Fallback(string diagnosis, string summary) =>
        new(false, false, 0, false, false, diagnosis, summary);
}

/// <summary>
/// Diagnostic Subagent：Provider / Model / Network 诊断工人。
/// 独立 AgentLoop + 受限只读工具集（diag.* 网络探针 + 指定 loomx 只读/测试工具），
/// 最大步数硬上限，禁止写工具、禁止再嵌套 Subagent。
/// </summary>
public sealed class DiagnosticSubagent
{
    private const int MaxSteps = 10;

    private static readonly string[] AllowedLoomXTools =
    [
        "loomx.get_status",
        "loomx.get_provider",
        "loomx.list_models",
        "loomx.test_provider",
        "loomx.test_model",
    ];

    private readonly AssistantTester tester;
    private readonly NetworkProbe networkProbe;
    private readonly ConfigurationManagementService configuration;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DiagnosticSubagent> logger;

    public DiagnosticSubagent(
        AssistantTester tester,
        NetworkProbe networkProbe,
        ConfigurationManagementService configuration,
        ILoggerFactory loggerFactory)
    {
        this.tester = tester;
        this.networkProbe = networkProbe;
        this.configuration = configuration;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<DiagnosticSubagent>();
    }

    /// <summary>Subagent 事件（SubagentStarted/SubagentCompleted 及其内部工具活动）。</summary>
    public event Action<AgentEvent>? SubagentEvent;

    /// <summary>
    /// 对指定 Provider 做分层诊断（DNS → TCP → TLS → HTTP → Auth → Models → Chat），
    /// 由模型驱动受限工具集逐层排查，输出结构化诊断结论。
    /// </summary>
    public async Task<DiagnosticReport> DiagnoseProviderAsync(
        IModelClient modelClient,
        string idOrBusinessId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modelClient);
        Raise(AgentEvent.Create(Guid.NewGuid().ToString("N"), AgentEventKind.SubagentStarted) with
        {
            Detail = $"diagnostic:{idOrBusinessId}",
        });

        var registry = BuildRestrictedRegistry();
        var session = new AgentSession(new AgentSessionOptions
        {
            MaxSteps = MaxSteps,
            ModelTimeout = TimeSpan.FromSeconds(90),
            SystemPrompt = """
                你是 LoomX 的 Provider 诊断工人。目标：定位用户指定 Provider 的连通性/鉴权问题。
                规则：
                1. 严格按层排查：DNS → TCP → TLS → HTTP/Auth → Models → Chat。上一层失败就不必测下一层。
                2. 你只能使用提供的只读与测试工具，不要尝试修改任何配置。
                3. 每个工具结果都是 JSON，仔细阅读其中的 error / diagnosis 字段。
                4. 最终回答必须是且只是一个 JSON 对象（不要 Markdown 代码块），字段：
                   {"reachable":bool,"authenticated":bool,"models_found":int,"chat_test":bool,
                    "proxy_required":bool,"diagnosis":"短蛇形命名结论","summary":"一句中文结论"}
                """,
        });

        var loop = new AgentLoop(modelClient, registry, loggerFactory.CreateLogger<AgentLoop>());
        var failed = false;
        try
        {
            await foreach (var agentEvent in loop.RunAsync(
                session,
                $"请诊断 Provider「{idOrBusinessId}」。先用 loomx.get_provider 获取其 base_url，再逐层排查。",
                cancellationToken))
            {
                if (agentEvent.Kind is AgentEventKind.TaskFailed or AgentEventKind.TaskCompleted)
                {
                    failed = agentEvent.Kind == AgentEventKind.TaskFailed;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "诊断 Subagent 运行失败 {Target}", idOrBusinessId);
            failed = true;
        }

        var report = failed
            ? DiagnosticReport.Fallback("diagnostic_loop_failed", "诊断流程未能完成，请检查助手模型配置。")
            : ParseReport(session);

        Raise(AgentEvent.Create(session.Id, AgentEventKind.SubagentCompleted) with
        {
            Success = !failed,
            Detail = report.Diagnosis,
        });
        return report;
    }

    private ToolRegistry BuildRestrictedRegistry()
    {
        // 从完整工具集中挑白名单，确保 Subagent 拿不到写/删除/浏览器工具
        var full = new ToolRegistry();
        LoomXTools.RegisterAll(full, configuration, new DisabledConfigurationProvider(), tester, SkillStore.Empty());
        var registry = new ToolRegistry();
        foreach (var tool in full.All.Where(tool => AllowedLoomXTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase)))
        {
            registry.Register(tool);
        }

        RegisterProbeTools(registry);
        return registry;
    }

    private void RegisterProbeTools(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition
        {
            Name = "diag.dns",
            Description = "对主机名做 DNS 解析探测。",
            ParametersSchema = JsonNode.Parse("""{"type":"object","properties":{"host":{"type":"string"}},"required":["host"]}""")!,
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) =>
                ToolResult.Ok((await networkProbe.DnsAsync(RequireHost(args), cancellationToken)).ToJsonString()),
        });

        registry.Register(new ToolDefinition
        {
            Name = "diag.tcp",
            Description = "对 host:port 做 TCP 连接探测。",
            ParametersSchema = JsonNode.Parse("""{"type":"object","properties":{"host":{"type":"string"},"port":{"type":"integer"}},"required":["host","port"]}""")!,
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) =>
            {
                var jsonObject = args as JsonObject ?? throw new ArgumentException("参数必须是 JSON 对象。");
                var host = jsonObject["host"]?.GetValue<string>() ?? throw new ArgumentException("缺少参数 host。");
                var port = jsonObject["port"]?.GetValue<int>() ?? throw new ArgumentException("缺少参数 port。");
                return ToolResult.Ok((await networkProbe.TcpAsync(host, port, cancellationToken)).ToJsonString());
            },
        });

        registry.Register(new ToolDefinition
        {
            Name = "diag.tls",
            Description = "对 host:port 做 TLS 握手探测，返回协议版本与证书有效期。",
            ParametersSchema = JsonNode.Parse("""{"type":"object","properties":{"host":{"type":"string"},"port":{"type":"integer"}},"required":["host","port"]}""")!,
            RiskLevel = ToolRiskLevel.Read,
            Handler = async (args, cancellationToken) =>
            {
                var jsonObject = args as JsonObject ?? throw new ArgumentException("参数必须是 JSON 对象。");
                var host = jsonObject["host"]?.GetValue<string>() ?? throw new ArgumentException("缺少参数 host。");
                var port = jsonObject["port"]?.GetValue<int>() ?? 443;
                return ToolResult.Ok((await networkProbe.TlsAsync(host, port, cancellationToken)).ToJsonString());
            },
        });
    }

    private static string RequireHost(JsonNode? args) =>
        (args as JsonObject)?["host"]?.GetValue<string>() ?? throw new ArgumentException("缺少参数 host。");

    private static DiagnosticReport ParseReport(AgentSession session)
    {
        var answer = session.Messages
            .LastOrDefault(message => message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Content))
            ?.Content;
        if (answer is null)
        {
            return DiagnosticReport.Fallback("diagnostic_no_answer", "诊断未产出结论。");
        }

        try
        {
            var start = answer.IndexOf('{');
            var end = answer.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return DiagnosticReport.Fallback("diagnostic_parse_failed", "诊断结论不是有效的 JSON。");
            }

            var json = JsonNode.Parse(answer[start..(end + 1)]) as JsonObject ?? throw new JsonException();
            return new DiagnosticReport(
                json["reachable"]?.GetValue<bool>() ?? false,
                json["authenticated"]?.GetValue<bool>() ?? false,
                json["models_found"]?.GetValue<int>() ?? 0,
                json["chat_test"]?.GetValue<bool>() ?? false,
                json["proxy_required"]?.GetValue<bool>() ?? false,
                json["diagnosis"]?.GetValue<string>() ?? "unknown",
                json["summary"]?.GetValue<string>() ?? string.Empty);
        }
        catch (JsonException)
        {
            return DiagnosticReport.Fallback("diagnostic_parse_failed", "诊断结论不是有效的 JSON。");
        }
    }

    private void Raise(AgentEvent agentEvent) => SubagentEvent?.Invoke(agentEvent);

    /// <summary>诊断场景下的配置提供方：只暴露安全摘要，不创建任何运行时副作用。</summary>
    private sealed class DisabledConfigurationProvider : IDatabaseConfigurationProvider
    {
        public ResolvedAppConfig Current { get; } = new();

        public IReadOnlyList<ResolvedModelConfig> GetModels() => [];

        public ResolvedModelConfig? FindModel(string? modelName) => null;

        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyLocalChangeAsync(ConfigurationChangedEventArgs change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
