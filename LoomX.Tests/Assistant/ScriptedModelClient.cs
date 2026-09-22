using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LoomX.Assistant;

namespace LoomX.Tests.Assistant;

/// <summary>
/// 剧本式 Mock 模型客户端：每次 StreamAsync 依次播放下一段预先编排的事件流。
/// </summary>
internal sealed class ScriptedModelClient : IModelClient
{
    private readonly Queue<IReadOnlyList<ModelStreamEvent>> turns;

    public ScriptedModelClient(params IReadOnlyList<ModelStreamEvent>[] turns)
    {
        this.turns = new Queue<IReadOnlyList<ModelStreamEvent>>(turns);
    }

    public List<ModelRequest> Requests { get; } = new();

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 快照消息列表，避免请求对象持有会话活引用导致断言时看到后续追加的消息
        Requests.Add(request with { Messages = request.Messages.ToArray() });
        if (turns.Count == 0) throw new InvalidOperationException("没有剩余的剧本。");
        foreach (var streamEvent in turns.Dequeue())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return streamEvent;
        }
        await Task.CompletedTask;
    }
}

/// <summary>
/// 永不返回的 Mock 模型客户端，用于验证取消。
/// </summary>
internal sealed class HangingModelClient : IModelClient
{
    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}

internal static class MockTools
{
    public static ToolDefinition CreateListProvidersTool(string result = """{"providers":["openai","anthropic"]}""") => new()
    {
        Name = "mock.list_providers",
        Description = "返回模拟的 Provider 列表",
        ParametersSchema = JsonNode.Parse("""{"type":"object","properties":{}}""")!,
        Handler = (_, _) => Task.FromResult(ToolResult.Ok(result)),
    };

    public static ToolDefinition CreateThrowingTool() => new()
    {
        Name = "mock.explode",
        Description = "总是抛出异常的工具",
        ParametersSchema = JsonNode.Parse("""{"type":"object","properties":{}}""")!,
        Handler = (_, _) => throw new InvalidOperationException("模拟工具故障"),
    };
}
