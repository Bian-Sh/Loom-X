using System.Net;
using System.Text;
using LoomX.Assistant;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using LoomX.Tests.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// Router Request Pipeline 生产挂载点：完整请求构造后、HttpClient.SendAsync 前。
/// 验证网关和内置 AI 助手共用的 ProviderExecutionPipeline 执行脱敏与 fail closed。
/// </summary>
public sealed class RouterRequestPipelineTests : IDisposable
{
    private const string ApiKey = "sk-abcdefghij0123456789abcd";
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-router-request-pipeline-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }

    private IPipeline CreateCredentialPipeline()
    {
        Directory.CreateDirectory(dataDirectory);
        var engine = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var pipeline = new Pipeline("request", ExtensionKind.Request, NullLogger.Instance);
        pipeline.AddEntry(new PipelineEntry(
            new CredentialRequestExtension(engine),
            CredentialProtectionPlugin.PluginId));
        return pipeline;
    }

    private IPipeline CreateToolResultPipeline()
    {
        Directory.CreateDirectory(dataDirectory);
        var engine = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var pipeline = new Pipeline("tool-result", ExtensionKind.ToolResult, NullLogger.Instance);
        pipeline.AddEntry(new PipelineEntry(
            new CredentialToolResultExtension(engine),
            CredentialProtectionPlugin.PluginId));
        return pipeline;
    }

    [Fact]
    public async Task ExecuteAsync_SanitizesBodyBeforeSend_PreservesHeaders()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions")
        {
            Content = new StringContent(
                $$"""{"messages":[{"role":"tool","content":"{{ApiKey}}"}]}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "provider-auth-key");
        var execution = new ProviderExecutionPipeline(
            NullLogger<ProviderExecutionPipeline>.Instance,
            CreateCredentialPipeline());

        await execution.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);

        Assert.NotNull(handler.Body);
        Assert.DoesNotContain(ApiKey, handler.Body, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, handler.Body, StringComparison.Ordinal);
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("provider-auth-key", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_UsesSameRouterRequestPipeline()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions")
        {
            Content = new StringContent($$"""{"input":"tool result {{ApiKey}}"}""", Encoding.UTF8, "application/json"),
        };
        var execution = new ProviderExecutionPipeline(
            NullLogger<ProviderExecutionPipeline>.Instance,
            CreateCredentialPipeline());

        await using var result = await execution.ExecuteStreamingAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None);

        Assert.DoesNotContain(ApiKey, handler.Body!, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssistantClient_UsesSharedRouterPipeline_ForToolResultHistory()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var execution = new ProviderExecutionPipeline(
            NullLogger<ProviderExecutionPipeline>.Instance,
            CreateCredentialPipeline());
        var client = new OpenAiCompatibleModelClient(
            httpClient,
            "https://provider.example/v1",
            "model",
            executionPipeline: execution,
            providerId: "provider");
        var toolCall = new ToolCall("call_1", "mock.read_config", "{}") { ArgumentsAreSafe = true };
        var request = new ModelRequest(
            [
                ChatMessage.User("读一下配置"),
                ChatMessage.ToolResult(toolCall, $$"""{"api_key":"{{ApiKey}}"}"""),
            ],
            []);

        await foreach (var _ in client.StreamAsync(request, CancellationToken.None))
        {
        }

        Assert.NotNull(handler.Body);
        Assert.DoesNotContain(ApiKey, handler.Body, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentLoop_ToolResultPipeline_TokenizesBeforeSessionAndNextRequest()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.browser_read",
            Description = "模拟浏览器读取结果",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object"}""")!,
            SafeArgumentsProjector = _ => new System.Text.Json.Nodes.JsonObject(),
            Handler = (_, _) => Task.FromResult(ToolResult.Ok($$"""{"content":"{{ApiKey}}"}""")),
        });
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.browser_read", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("完成"), new ModelCompletedEvent("stop")]);
        var pipeline = CreateToolResultPipeline();
        var loop = new AgentLoop(
            model,
            registry,
            NullLogger<AgentLoop>.Instance,
            toolResultProcessor: async (payload, cancellationToken) =>
            {
                var result = await pipeline.ExecuteAsync(payload, cancellationToken);
                if (result.Outcome == PipelineOutcome.Blocked) throw new InvalidOperationException();
                return result.Payload;
            });
        var session = new AgentSession();

        await foreach (var _ in loop.RunAsync(session, "读取"))
        {
        }

        var toolResult = Assert.Single(session.Messages, message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(ApiKey, toolResult.Content, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, toolResult.Content, StringComparison.Ordinal);
        var nextRequestToolResult = Assert.Single(model.Requests[1].Messages, message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(ApiKey, nextRequestToolResult.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlockedRequest_FailClosed_OriginalBodyNeverSent()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/chat/completions")
        {
            Content = new StringContent($$"""{"input":"{{ApiKey}}"}""", Encoding.UTF8, "application/json"),
        };
        var pipeline = new Pipeline("request", ExtensionKind.Request, NullLogger.Instance);
        pipeline.AddEntry(new PipelineEntry(new BlockingRequestExtension(), "demo.safety"));
        var execution = new ProviderExecutionPipeline(NullLogger<ProviderExecutionPipeline>.Instance, pipeline);

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            httpClient,
            request,
            new ProviderExecutionContext("provider", "model", "openai", "/chat/completions"),
            CancellationToken.None));

        Assert.Equal(0, handler.SendCount);
        Assert.Null(handler.Body);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public string? Body { get; private set; }
        public string? ContentType { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private sealed class BlockingRequestExtension : IRequestExtension
    {
        public string ExtensionId => "credential.block";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
        public IReadOnlyList<string> Capabilities => ["credential.mask"];

        public ValueTask<PipelineResult> ProcessRequestAsync(
            PipelineContext context,
            string payload,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PipelineResult.Block("测试阻止。"));
    }
}
