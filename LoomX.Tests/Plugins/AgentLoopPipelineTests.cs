using LoomX.Assistant;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Tests.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// Tool Result Pipeline 挂载点：AgentLoop 中 EnsureSafeFailure 之后、session.AddMessage 之前
/// （spec: credential-protection / 敏感数据检测、持久化前清理；tasks 4.1）。
/// </summary>
public sealed class AgentLoopPipelineTests : IDisposable
{
    private const string ApiKey = "sk-abcdefghij0123456789abcd";

    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-agentloop-pipeline-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }

    private IPipeline CreateToolResultPipeline()
    {
        Directory.CreateDirectory(dataDirectory);
        var engine = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var pipeline = new Pipeline("tool-result", ExtensionKind.ToolResult, NullLogger.Instance);
        pipeline.AddEntry(new PipelineEntry(new CredentialToolResultExtension(engine), "loomx.credential-protection"));
        return pipeline;
    }

    private static ToolRegistry CreateRegistryWithSecretTool()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.read_secret_config",
            Description = "返回包含明文 Key 的配置",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object","properties":{}}""")!,
            Handler = (_, _) => Task.FromResult(ToolResult.Ok(
                $$"""{"api_key":"{{ApiKey}}","endpoint":"https://api.example.com"}""")),
        });
        return registry;
    }

    private static async Task<List<AgentEvent>> CollectAsync(IAsyncEnumerable<AgentEvent> events)
    {
        var collected = new List<AgentEvent>();
        await foreach (var agentEvent in events) collected.Add(agentEvent);
        return collected;
    }

    [Fact]
    public async Task ToolResult_SanitizedBeforeEnteringSessionHistory()
    {
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.read_secret_config", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已读取。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();
        var loop = new AgentLoop(
            modelClient, CreateRegistryWithSecretTool(), NullLogger<AgentLoop>.Instance,
            toolResultPipeline: CreateToolResultPipeline());

        await CollectAsync(loop.RunAsync(session, "读一下配置"));

        // 会话历史中的工具结果已脱敏
        var toolMessage = session.Messages.First(message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(ApiKey, toolMessage.Content, StringComparison.Ordinal);
        Assert.Contains("***", toolMessage.Content, StringComparison.Ordinal);
        Assert.Contains("https://api.example.com", toolMessage.Content, StringComparison.Ordinal);

        // 回传给模型的历史同样是脱敏后内容
        var sentToolMessage = modelClient.Requests[1].Messages.First(message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(ApiKey, sentToolMessage.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlockedToolResult_MapsToSafeFailure_OriginalNotInHistory()
    {
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.read_secret_config", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已读取。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();
        var blockingPipeline = new Pipeline("tool-result", ExtensionKind.ToolResult, NullLogger.Instance);
        blockingPipeline.AddEntry(new PipelineEntry(new BlockingToolResultExtension("ext.block"), "demo"));
        var loop = new AgentLoop(
            modelClient, CreateRegistryWithSecretTool(), NullLogger<AgentLoop>.Instance,
            toolResultPipeline: blockingPipeline);

        await CollectAsync(loop.RunAsync(session, "读一下配置"));

        var toolMessage = session.Messages.First(message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(ApiKey, toolMessage.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("api.example.com", toolMessage.Content, StringComparison.Ordinal);
        var completed = Assert.Single(
            modelClient.Requests[1].Messages,
            message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(ApiKey, completed.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullPipeline_BehaviorUnchanged()
    {
        var modelClient = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.read_secret_config", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已读取。"), new ModelCompletedEvent("stop")]);
        var session = new AgentSession();
        var loop = new AgentLoop(
            modelClient, CreateRegistryWithSecretTool(), NullLogger<AgentLoop>.Instance);

        await CollectAsync(loop.RunAsync(session, "读一下配置"));

        var toolMessage = session.Messages.First(message => message.Role == ChatRole.Tool);
        Assert.Contains(ApiKey, toolMessage.Content, StringComparison.Ordinal); // 无 Pipeline 时保持现状
    }
}
