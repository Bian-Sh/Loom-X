using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
}));

var failures = new List<string>();
void Check(string name, bool passed)
{
    Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}");
    if (!passed) failures.Add(name);
}

var pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
var dataDirectory = Path.Combine(AppContext.BaseDirectory, "plugin-data");
var runtime = PluginRuntime.Start(
    new PluginRuntimeOptions
    {
        PluginDirectory = pluginDirectory,
        DataRootDirectory = dataDirectory,
    },
    loggerFactory);

Check("Credential Protection 插件已发现", runtime.PluginInfos.Any(info => info.Id == "loomx.credential-protection"));
Check("Manifest 与 ALC 加载无诊断", runtime.Diagnostics.Count == 0);

var requestPipeline = runtime.GetPipeline("request");
Check("Router Request Pipeline 已注册", requestPipeline is not null);
Check("Credential Request Entry 已注册",
    requestPipeline is Pipeline concreteRequestPipeline
    && concreteRequestPipeline.Entries.Any(entry =>
        entry.PluginId == "loomx.credential-protection"
        && entry.Extension.ExtensionId == "credential.request"
        && entry.Extension.Kind == ExtensionKind.Request));

const string secret = "sk-playground-example-1234567890";
var payload = $$"""{"messages":[{"role":"tool","content":"credential={{secret}}"}]}""";
var protectedResult = requestPipeline is null
    ? PipelineResult.Block("Request Pipeline 未注册。")
    : await requestPipeline.ExecuteAsync(payload);
Check("请求正文中的凭据已脱敏",
    protectedResult.Outcome == PipelineOutcome.Modified
    && !protectedResult.Payload.Contains(secret, StringComparison.Ordinal)
    && protectedResult.Payload.Contains("***", StringComparison.Ordinal));

const string businessPayload = """{"model":"example-model","status":"ready"}""";
var businessResult = requestPipeline is null
    ? PipelineResult.Block("Request Pipeline 未注册。")
    : await requestPipeline.ExecuteAsync(businessPayload);
Check("普通业务请求原样通过",
    businessResult.Outcome == PipelineOutcome.Passed
    && businessResult.Payload == businessPayload);

if (requestPipeline is not null)
{
    runtime.SetEntryEnabled("loomx.credential-protection", "credential.request", false);
    var disabledResult = await requestPipeline.ExecuteAsync(payload);
    Check("禁用 Entry 后跳过脱敏",
        disabledResult.Outcome == PipelineOutcome.Passed
        && disabledResult.Payload.Contains(secret, StringComparison.Ordinal));

    runtime.SetEntryEnabled("loomx.credential-protection", "credential.request", true);
    var reenabledResult = await requestPipeline.ExecuteAsync(payload);
    Check("重新启用 Entry 后恢复脱敏",
        reenabledResult.Outcome == PipelineOutcome.Modified
        && !reenabledResult.Payload.Contains(secret, StringComparison.Ordinal));
}

var executionLog = new List<string>();
var orderedPipeline = new Pipeline("request-order", ExtensionKind.Request, loggerFactory.CreateLogger<Pipeline>());
var first = new PipelineEntry(new DemoRequestExtension("first", executionLog), "demo.first");
var second = new PipelineEntry(new DemoRequestExtension("second", executionLog), "demo.second");
orderedPipeline.AddEntry(first);
orderedPipeline.AddEntry(second);
orderedPipeline.Reorder([second, first]);
await orderedPipeline.ExecuteAsync("payload");
Check("Pipeline Entry 按 Router 配置顺序执行", executionLog.SequenceEqual(["second", "first"]));

var resilientPipeline = new Pipeline("request-resilient", ExtensionKind.Request, loggerFactory.CreateLogger<Pipeline>());
resilientPipeline.AddEntry(new PipelineEntry(
    new ThrowingRequestExtension("observability", ExtensionFailurePolicy.ContinueOnError),
    "demo.observability"));
resilientPipeline.AddEntry(new PipelineEntry(new AppendRequestExtension("tail"), "demo.tail"));
var resilientResult = await resilientPipeline.ExecuteAsync("payload");
Check("普通插件异常被隔离且主流程继续",
    resilientResult.Outcome == PipelineOutcome.Modified
    && resilientResult.Payload == "payload-tail");

var failClosedPipeline = new Pipeline("request-secure", ExtensionKind.Request, loggerFactory.CreateLogger<Pipeline>());
failClosedPipeline.AddEntry(new PipelineEntry(
    new ThrowingRequestExtension("safety", ExtensionFailurePolicy.FailClosed),
    "demo.safety"));
var blockedResult = await failClosedPipeline.ExecuteAsync(payload);
Check("数据安全插件异常 fail closed",
    blockedResult.Outcome == PipelineOutcome.Blocked
    && string.IsNullOrEmpty(blockedResult.Payload));

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "PluginPlayground: 全部验证通过。"
    : $"PluginPlayground: {failures.Count} 项验证失败：{string.Join("、", failures)}");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

internal sealed class DemoRequestExtension(string extensionId, List<string> log) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context,
        string payload,
        CancellationToken cancellationToken)
    {
        log.Add(extensionId);
        return ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}

internal sealed class AppendRequestExtension(string extensionId) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context,
        string payload,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(PipelineResult.Modify(payload + "-tail"));
}

internal sealed class ThrowingRequestExtension(
    string extensionId,
    ExtensionFailurePolicy failurePolicy) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context,
        string payload,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Playground 模拟异常。");
}
