using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// AssistantSessionStore 保持 Assistant 自身安全边界，不依赖 Router Plugin Runtime。
/// </summary>
public sealed class AssistantSessionStoreSecurityTests : IDisposable
{
    private const string ApiKey = "sk-abcdefghij0123456789abcd";
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-store-security-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_SecretLeakScanStillRejectsPlaintextSecret()
    {
        var store = new AssistantSessionStore(rootDirectory);
        var session = new AgentSession();
        session.AddMessage(ChatMessage.User("读一下配置"));
        var call = new ToolCall("call_1", "mock.read_config", "{}") { ArgumentsAreSafe = true };
        session.AddMessage(ChatMessage.ToolResult(
            call,
            $$"""{"api_key":"{{ApiKey}}","endpoint":"https://api.example.com"}"""));
        session.MarkCompleted();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(session));

        Assert.False(File.Exists(Path.Combine(rootDirectory, $"{session.Id}.jsonl")));
    }
}
