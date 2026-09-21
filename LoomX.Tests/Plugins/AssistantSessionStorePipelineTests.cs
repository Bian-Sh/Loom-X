using LoomX.Assistant;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// Persistence Pipeline 挂载点：AssistantSessionStore.SaveAsync 写入前
/// （spec: credential-protection / 持久化前清理、fail closed；tasks 4.2）。
/// </summary>
public sealed class AssistantSessionStorePipelineTests : IDisposable
{
    private const string ApiKey = "sk-abcdefghij0123456789abcd";

    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-store-pipeline-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
    }

    private IPipeline CreatePersistencePipeline()
    {
        var dataDirectory = Path.Combine(rootDirectory, "plugin-data");
        Directory.CreateDirectory(dataDirectory);
        var engine = new CredentialEngine(new SensitiveRuleStore(dataDirectory));
        var pipeline = new Pipeline("persistence", ExtensionKind.Persistence, NullLogger.Instance);
        pipeline.AddEntry(new PipelineEntry(new CredentialPersistenceExtension(engine), "loomx.credential-protection"));
        return pipeline;
    }

    private static AgentSession CreateSessionWithSecretToolResult()
    {
        var session = new AgentSession();
        session.AddMessage(ChatMessage.User("读一下配置"));
        var call = new ToolCall("call_1", "mock.read_config", "{}") { ArgumentsAreSafe = true };
        session.AddMessage(ChatMessage.ToolResult(
            call, $$"""{"api_key":"{{ApiKey}}","endpoint":"https://api.example.com"}"""));
        session.MarkCompleted();
        return session;
    }

    private string ReadSessionFile(string sessionId) =>
        File.ReadAllText(Path.Combine(rootDirectory, $"{sessionId}.jsonl"));

    [Fact]
    public async Task SaveAsync_SanitizesBeforeWriting_JsonlFreeOfSecretShapes()
    {
        var store = new AssistantSessionStore(rootDirectory, persistencePipeline: CreatePersistencePipeline());
        var session = CreateSessionWithSecretToolResult();

        await store.SaveAsync(session);

        var jsonl = ReadSessionFile(session.Id);
        Assert.DoesNotContain(ApiKey, jsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("\"api_key\":\"sk-", jsonl, StringComparison.Ordinal);
        Assert.Contains("***", jsonl, StringComparison.Ordinal);
        Assert.Contains("api.example.com", jsonl, StringComparison.Ordinal); // 非敏感内容保留

        // 落盘内容仍可正常加载
        var loaded = await store.LoadAsync(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(session.Messages.Count, loaded.Messages.Count);
    }

    [Fact]
    public async Task SaveAsync_PipelineBlocked_SkipsWrite()
    {
        var blockingPipeline = new Pipeline("persistence", ExtensionKind.Persistence, NullLogger.Instance);
        blockingPipeline.AddEntry(new PipelineEntry(
            new ThrowingExtension("ext.safety", ExtensionKind.Persistence, ExtensionFailurePolicy.FailClosed),
            "demo"));
        var store = new AssistantSessionStore(rootDirectory, persistencePipeline: blockingPipeline);
        var session = CreateSessionWithSecretToolResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(session));

        Assert.False(File.Exists(Path.Combine(rootDirectory, $"{session.Id}.jsonl")));
    }

    [Fact]
    public async Task SaveAsync_NullPipeline_SecretLeakScanStillBackstops()
    {
        // Pipeline 为空时 SecretLeakScan 兜底保留：sk- 形态拒绝落盘（现状行为不变）
        var store = new AssistantSessionStore(rootDirectory);
        var session = CreateSessionWithSecretToolResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(session));
    }
}
