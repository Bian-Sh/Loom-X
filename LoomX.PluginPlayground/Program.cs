using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;

// LoomX Plugin Playground：正式迁入宿主前，验证 Plugin Runtime/Pipeline 与
// Credential Protection 插件的组合。任一检查失败以非零退出码结束。

var checks = new List<(string Name, bool Passed, string? Detail)>();
void Check(string name, bool passed, string? detail = null)
{
    checks.Add((name, passed, detail));
    Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : $" — {detail}")}");
}

var loggerFactory = LoggerFactory.Create(builder => { });
var pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
var dataRoot = Path.Combine(Path.GetTempPath(), "loomx-plugin-playground", Guid.NewGuid().ToString("N"));

// ── 1. Runtime 发现、Manifest 验证与 ALC 加载 ──────────────────────────
var runtime = PluginRuntime.Start(
    new PluginRuntimeOptions { PluginDirectory = pluginDirectory, DataRootDirectory = dataRoot },
    loggerFactory);

var pluginInfo = runtime.PluginInfos.FirstOrDefault(item => item.Id == "loomx.credential-protection");
Check("插件发现与加载：loomx.credential-protection", pluginInfo is not null && pluginInfo.ExtensionCount == 2,
    pluginInfo is null ? string.Join(';', runtime.Diagnostics) : $"extensions={pluginInfo.ExtensionCount}");
Check("Manifest 验证无拒绝项", runtime.Diagnostics.Count == 0,
    runtime.Diagnostics.Count == 0 ? null : string.Join(';', runtime.Diagnostics));

// ── 2. Tool Result Pipeline：含明文 Key 的工具结果被脱敏 ───────────────
var toolResultPipeline = runtime.GetPipeline("tool-result");
Check("Tool Result Pipeline 已注册", toolResultPipeline is not null);

const string apiKey = "sk-abcdefghij0123456789abcd";
var toolPayload = $$"""{"tool":"read_config","output":"key is {{apiKey}}","api_key":"{{apiKey}}","status":200}""";
var toolResult = toolResultPipeline is null
    ? null
    : await toolResultPipeline.ExecuteAsync(toolPayload);
Check("Tool Result 含明文 Key 被脱敏",
    toolResult is { Outcome: PipelineOutcome.Modified }
    && !toolResult.Payload.Contains(apiKey, StringComparison.Ordinal)
    && toolResult.Payload.Contains("***", StringComparison.Ordinal));

// ── 3. Persistence Pipeline：Bearer/JWT 形态不落盘 ─────────────────────
var persistencePipeline = runtime.GetPipeline("persistence");
Check("Persistence Pipeline 已注册", persistencePipeline is not null);

const string bearerToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJVadQssw5c";
var persistencePayload = $"{{\"role\":\"tool\",\"content\":\"Authorization: Bearer {bearerToken}\"}}";
var persistenceResult = persistencePipeline is null
    ? null
    : await persistencePipeline.ExecuteAsync(persistencePayload);
Check("Persistence 含 Bearer/JWT 被清理",
    persistenceResult is { Outcome: PipelineOutcome.Modified }
    && !persistenceResult.Payload.Contains(bearerToken, StringComparison.Ordinal)
    && !System.Text.RegularExpressions.Regex.IsMatch(
        persistenceResult.Payload, @"Bearer\s+[A-Za-z0-9._~+/=-]{16,}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase),
    persistenceResult is null ? null : $"outcome={persistenceResult.Outcome} payload={persistenceResult.Payload}");

// ── 4. 普通业务数据不误判 ──────────────────────────────────────────────
var businessPayload = """{"model":"qwen3:8b","provider":"ollama","status":200,"content":"生成完成"}""";
var businessResult = toolResultPipeline is null ? null : await toolResultPipeline.ExecuteAsync(businessPayload);
Check("普通业务数据原样通过",
    businessResult is { Outcome: PipelineOutcome.Passed } && businessResult.Payload == businessPayload);

// ── 5. Entry 排序：配置顺序决定执行顺序 ────────────────────────────────
var executionLog = new List<string>();
var registry = new PipelineRegistry();
var orderPipeline = registry.GetOrCreatePipeline("order-demo", ExtensionKind.ToolResult, loggerFactory.CreateLogger<Pipeline>());
var entryA = new PipelineEntry(new RecordingExtension("ext.a", "A", executionLog), "demo.alpha");
var entryB = new PipelineEntry(new RecordingExtension("ext.b", "B", executionLog), "demo.beta");
registry.Register(orderPipeline, entryA);
registry.Register(orderPipeline, entryB);

await orderPipeline.ExecuteAsync("payload");
Check("默认按注册顺序执行", executionLog.SequenceEqual(["A", "B"]), string.Join(',', executionLog));

executionLog.Clear();
orderPipeline.Reorder([entryB, entryA]);
await orderPipeline.ExecuteAsync("payload");
Check("配置顺序调整后按新顺序执行", executionLog.SequenceEqual(["B", "A"]), string.Join(',', executionLog));

// ── 6. 启用/禁用：禁用 Entry 被跳过，重新启用恢复 ──────────────────────
executionLog.Clear();
entryA.Enabled = false;
await orderPipeline.ExecuteAsync("payload");
Check("禁用 Entry 被跳过", executionLog.SequenceEqual(["B"]), string.Join(',', executionLog));

executionLog.Clear();
entryA.Enabled = true;
await orderPipeline.ExecuteAsync("payload");
Check("重新启用后按配置顺序恢复", executionLog.SequenceEqual(["B", "A"]), string.Join(',', executionLog));

// ── 7. 异常隔离：普通 Entry 失败继续，数据安全 Entry 失败 fail closed ──
var resilientPipeline = registry.GetOrCreatePipeline("resilience-demo", ExtensionKind.ToolResult, loggerFactory.CreateLogger<Pipeline>());
registry.Register(resilientPipeline, new PipelineEntry(new ThrowingExtension("ext.observability", ExtensionKind.ToolResult, ExtensionFailurePolicy.ContinueOnError), "demo.gamma"));
registry.Register(resilientPipeline, new PipelineEntry(new RecordingExtension("ext.tail", "tail", executionLog), "demo.delta"));

executionLog.Clear();
var continueResult = await resilientPipeline.ExecuteAsync("secret-free");
Check("普通 Entry 失败记录诊断并继续",
    continueResult.Outcome == PipelineOutcome.Passed && executionLog.SequenceEqual(["tail"]),
    $"outcome={continueResult.Outcome}");

var failClosedPipeline = registry.GetOrCreatePipeline("fail-closed-demo", ExtensionKind.Persistence, loggerFactory.CreateLogger<Pipeline>());
registry.Register(failClosedPipeline, new PipelineEntry(new ThrowingExtension("ext.safety", ExtensionKind.Persistence, ExtensionFailurePolicy.FailClosed), "demo.epsilon"));
var blockedResult = await failClosedPipeline.ExecuteAsync($"raw {apiKey}");
Check("数据安全 Entry 失败 fail closed 不放行原始数据",
    blockedResult.Outcome == PipelineOutcome.Blocked
    && blockedResult.Payload.Length == 0
    && !blockedResult.Payload.Contains(apiKey, StringComparison.Ordinal));

// ── 8. 插件禁用：整条 Pipeline 的 Entry 被跳过 ─────────────────────────
runtime.SetPluginEnabled("loomx.credential-protection", false);
var disabledResult = toolResultPipeline is null ? null : await toolResultPipeline.ExecuteAsync(toolPayload);
Check("插件禁用后数据原样通过（未脱敏）",
    disabledResult is { Outcome: PipelineOutcome.Passed } && disabledResult.Payload.Contains(apiKey, StringComparison.Ordinal));
runtime.SetPluginEnabled("loomx.credential-protection", true);
var reenabledResult = toolResultPipeline is null ? null : await toolResultPipeline.ExecuteAsync(toolPayload);
Check("插件重新启用后恢复脱敏",
    reenabledResult is { Outcome: PipelineOutcome.Modified } && !reenabledResult.Payload.Contains(apiKey, StringComparison.Ordinal));

// ── 汇总 ──────────────────────────────────────────────────────────────
var failed = checks.Count(item => !item.Passed);
Console.WriteLine();
Console.WriteLine($"Playground 验证完成：{checks.Count - failed}/{checks.Count} 通过。");
return failed == 0 ? 0 : 1;

/// <summary>记录执行顺序的演示 Extension。</summary>
internal sealed class RecordingExtension(string extensionId, string tag, List<string> log) : IToolResultExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.ToolResult;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken)
    {
        log.Add(tag);
        return ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}

/// <summary>总是抛异常的演示 Extension，用于验证失败策略。</summary>
internal sealed class ThrowingExtension(string extensionId, ExtensionKind kind, ExtensionFailurePolicy failurePolicy) : IToolResultExtension, IPersistenceExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => kind;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessToolResultAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("演示异常。");

    public ValueTask<PipelineResult> ProcessPersistenceAsync(
        PipelineContext context, string payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("演示异常。");
}
