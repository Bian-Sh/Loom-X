using LoomX.Assistant;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class AssistantSessionStoreCredentialPipelineTests : IDisposable
{
    private const string ApiKey = "sk-abcdefghij0123456789abcd";
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-store-credential-" + Guid.NewGuid().ToString("N"));
    private readonly string pluginDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-store-plugin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        if (Directory.Exists(pluginDirectory)) Directory.Delete(pluginDirectory, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoad_TokenizesDiskContent_AndRestoresHistory()
    {
        Directory.CreateDirectory(pluginDirectory);
        var engine = new CredentialEngine(new SensitiveRuleStore(pluginDirectory));
        var persistence = new Pipeline("persistence", ExtensionKind.Persistence, NullLogger.Instance);
        persistence.AddEntry(new PipelineEntry(new CredentialPersistenceExtension(engine), CredentialProtectionPlugin.PluginId));
        var response = new Pipeline("response", ExtensionKind.Response, NullLogger.Instance);
        response.AddEntry(new PipelineEntry(new CredentialResponseExtension(engine), CredentialProtectionPlugin.PluginId));
        var store = new AssistantSessionStore(rootDirectory, persistencePipeline: persistence, responsePipeline: response);
        var session = new AgentSession();
        session.AddMessage(ChatMessage.User("读一下配置"));
        var call = new ToolCall("call_1", "mock.read_config", "{}") { ArgumentsAreSafe = true };
        session.AddMessage(ChatMessage.ToolResult(
            call,
            $$"""{"api_key":"{{ApiKey}}","endpoint":"https://api.example.com"}"""));
        session.MarkCompleted();

        await store.SaveAsync(session);
        var onDisk = await File.ReadAllTextAsync(Path.Combine(rootDirectory, $"{session.Id}.jsonl"));
        var restored = await store.LoadAsync(session.Id);

        Assert.DoesNotContain(ApiKey, onDisk, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, onDisk, StringComparison.Ordinal);
        Assert.NotNull(restored);
        Assert.Contains(ApiKey, restored!.Messages.Last().Content, StringComparison.Ordinal);
    }
}
