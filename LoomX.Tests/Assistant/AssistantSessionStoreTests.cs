using Xunit;
using LoomX.Assistant;
using System.Text.Json.Nodes;

namespace LoomX.Tests.Assistant;

/// <summary>
/// 会话持久化：保存/载入回环、列表摘要、Secret 兜底扫描、Running 状态恢复。
/// </summary>
public sealed class AssistantSessionStoreTests : IDisposable
{
    private readonly string rootDirectory = Path.Combine(Path.GetTempPath(), $"loomx-sessions-{Guid.NewGuid():N}");
    private readonly AssistantSessionStore store;

    public AssistantSessionStoreTests()
    {
        store = new AssistantSessionStore(rootDirectory);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveLoad_RoundTrip_PreservesMessagesAndState()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("帮我看看有几个 Provider"));
        session.RestoreMessage(ChatMessage.AssistantToolCalls(
            [new ToolCall("c1", "loomx.list_providers", "{}")], null));
        session.RestoreMessage(ChatMessage.ToolResult(new ToolCall("c1", "loomx.list_providers", "{}"), """{"providers":[]}"""));
        session.RestoreMessage(ChatMessage.Assistant("你当前有 3 个 Provider。"));
        session.MarkRunning();
        session.MarkCompleted();

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(AgentSessionState.Completed, loaded.State);
        Assert.Equal(4, loaded.Messages.Count);
        Assert.Equal("帮我看看有几个 Provider", loaded.Messages[0].Content);
        Assert.Equal("loomx.list_providers", loaded.Messages[1].ToolCalls[0].Name);
        Assert.Equal("c1", loaded.Messages[2].ToolCallId);
        Assert.Equal("你当前有 3 个 Provider。", loaded.Messages[3].Content);
    }

    [Fact]
    public async Task SaveLoad_RunningState_RestoredAsCancelled()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("跑到一半的会话"));
        session.MarkRunning(); // 崩溃残留状态

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync(session.Id);

        Assert.Equal(AgentSessionState.Cancelled, loaded!.State);
    }

    [Fact]
    public async Task List_ReturnsSummariesWithUserTitle()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("把这个中转站配置到 LoomX，谢谢，这是一句非常非常长的话，专门用来测试标题截断功能是否能够正常工作"));
        session.MarkRunning();
        session.MarkCompleted();
        await store.SaveAsync(session);

        var summaries = store.List();

        var summary = Assert.Single(summaries);
        Assert.Equal(session.Id, summary.SessionId);
        Assert.EndsWith("…", summary.Title);
        Assert.Equal("Completed", summary.State);
        Assert.Equal(1, summary.MessageCount);
    }

    [Fact]
    public async Task Save_SecretShapedContent_Refused()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User($"我的 Key 是 sk-abcdefghijklmnopqrstuvwxyz012345 帮我存一下"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(session));
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task Save_SecretRefContent_Allowed()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.ToolResult(
            new ToolCall("c1", "loomx.get_provider", """{"id":"x"}"""),
            """{"api_key":{"configured":true,"secret_ref":"secret://provider/demo/apikey"}}"""));
        session.MarkRunning();
        session.MarkCompleted();

        await store.SaveAsync(session); // secret_ref 不含明文，必须允许落盘
        Assert.Single(store.List());
    }

    [Fact]
    public async Task Load_MissingSession_ReturnsNull()
    {
        Assert.Null(await store.LoadAsync("does-not-exist"));
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("删除我"));
        session.MarkRunning();
        session.MarkCompleted();
        await store.SaveAsync(session);

        store.Delete(session.Id);

        Assert.Empty(store.List());
    }

    [Fact]
    public async Task Append_RetainsOrderedReasoningBlocksAndStableHeader()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("查询"));
        session.MarkRunning();
        await store.SaveAsync(session);
        var path = Path.Combine(rootDirectory, session.Id + ".jsonl");
        var firstLine = File.ReadLines(path).First();

        session.RestoreMessage(ChatMessage.Assistant("答案") with
        {
            Blocks = [new ChatContentBlock(ChatContentKind.Thinking, "原文"),
                new ChatContentBlock(ChatContentKind.Thinking, "摘要", true),
                new ChatContentBlock(ChatContentKind.Text, "答案")],
        });
        session.MarkCompleted();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.SaveAsync(session)));

        var lines = File.ReadAllLines(path);
        Assert.Equal(firstLine, lines[0]);
        Assert.Equal(2, lines.Count(line => JsonNode.Parse(line)?["type"]?.GetValue<string>() == "message"));
        var loaded = await store.LoadAsync(session.Id);
        Assert.Equal("原文", loaded!.Messages[^1].Blocks[0].Text);
        Assert.True(loaded.Messages[^1].Blocks[1].IsSummary);
        Assert.Equal(session.Messages[^1].Timestamp, loaded.Messages[^1].Timestamp);
        Assert.Equal(AgentSessionState.Completed, loaded.State);
    }

    [Fact]
    public async Task V1MigrationAndBrokenTail_PreservePriorMessages()
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, id + ".jsonl");
        var savedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await File.WriteAllTextAsync(path, $"{{\"type\":\"meta\",\"version\":1,\"session_id\":\"{id}\",\"state\":\"Completed\",\"updated_at\":\"{savedAt:O}\"}}\n" +
            "{\"type\":\"message\",\"role\":\"User\",\"content\":\"旧消息\"}\n" +
            "{\"type\":\"message\",\"role\":\"Assistant\",\"content\":\"旧回答\"}\n" +
            "{\"type\":");

        var session = await store.LoadAsync(id);
        Assert.Equal(2, session!.Messages.Count);
        Assert.Equal(savedAt, session.Messages[0].Timestamp);
        session.RestoreMessage(ChatMessage.User("新消息"));
        await store.SaveAsync(session);

        var lines = File.ReadAllLines(path);
        Assert.Equal("session", JsonNode.Parse(lines[0])?["type"]?.GetValue<string>());
        Assert.Equal(3, lines.Count(line => JsonNode.Parse(line)?["type"]?.GetValue<string>() == "message"));
        Assert.Equal(3, (await store.LoadAsync(id))!.Messages.Count);
    }

    [Fact]
    public async Task V2BrokenTail_IsRepairedBeforeAppending()
    {
        var session = new AgentSession();
        session.RestoreMessage(ChatMessage.User("先前消息"));
        await store.SaveAsync(session);
        var path = Path.Combine(rootDirectory, session.Id + ".jsonl");
        await File.AppendAllTextAsync(path, "{\"type\":");
        Assert.Single((await store.LoadAsync(session.Id))!.Messages);

        session.RestoreMessage(ChatMessage.Assistant("后来回答"));
        await store.SaveAsync(session);

        Assert.All(File.ReadAllLines(path), line => Assert.NotNull(JsonNode.Parse(line)));
        Assert.Equal(2, (await store.LoadAsync(session.Id))!.Messages.Count);
    }

    [Theory]
    [InlineData("sk-abc123", false)]                       // 太短，不算 Key
    [InlineData("sk-abcdefghijklmnopqrstuvwxyz012345", true)]
    [InlineData("Authorization: Bearer abcdef0123456789abcdef", true)]
    [InlineData("secret://provider/demo/apikey", false)]   // secret_ref 是安全引用
    [InlineData("普通中文内容，没有任何敏感信息", false)]
    public void SecretLeakScan_DetectsShapes(string content, bool expected)
    {
        Assert.Equal(expected, AssistantSessionStore.SecretLeakScan($"{{\"content\":\"{content}\"}}"));
    }
}
