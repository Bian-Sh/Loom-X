using System.Text.Json.Nodes;
using LoomX.ToolResultCompression;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class ToolResultRequestRewriterTests
{
    private static readonly ToolResultCompressionOptions Options = ToolResultCompressionOptions.Default;

    /// <summary>单条结果也是“最近结果”，默认会被保真窗口跳过；协议测试显式关闭该窗口。</summary>
    private static readonly ToolResultCompressionOptions NoFidelityWindow =
        ToolResultCompressionOptions.Default with { KeepRecentToolResults = 0 };

    private static string LongText(string tag) =>
        string.Join('\n', Enumerable.Repeat($"INFO  {tag} heartbeat ok", 200));

    [Fact]
    public void Rewrite_CompressesOpenAiToolMessages()
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = "you are helpful" },
            new JsonObject { ["role"] = "user", ["content"] = "run the command" },
            new JsonObject { ["role"] = "tool", ["tool_call_id"] = "call_1", ["content"] = LongText("alpha") },
        };
        var payload = new JsonObject { ["model"] = "m", ["messages"] = messages }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(payload, "openai", NoFidelityWindow, out var output, out var report);

        Assert.True(rewritten);
        Assert.Equal(1, report.CompressedToolResults);
        var parsed = JsonNode.Parse(output)!.AsObject();
        Assert.Equal("you are helpful", parsed["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Contains("×200", parsed["messages"]![2]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_CompressesAnthropicToolResultBlocks()
    {
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = "toolu_1",
                ["content"] = LongText("anthropic"),
            },
        };
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "assistant", ["content"] = "working" },
            new JsonObject { ["role"] = "user", ["content"] = content },
        };
        var payload = new JsonObject { ["model"] = "m", ["messages"] = messages }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(payload, "anthropic", NoFidelityWindow, out var output, out var report);

        Assert.True(rewritten);
        Assert.Equal(1, report.CompressedToolResults);
        var parsed = JsonNode.Parse(output)!.AsObject();
        var block = parsed["messages"]![1]!["content"]![0]!.AsObject();
        Assert.Contains("×200", block["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_CompressesResponsesFunctionCallOutput()
    {
        var input = new JsonArray
        {
            new JsonObject { ["type"] = "function_call_output", ["call_id"] = "call_1", ["output"] = LongText("responses") },
        };
        var payload = new JsonObject { ["model"] = "m", ["input"] = input }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(payload, "responses", NoFidelityWindow, out var output, out var report);

        Assert.True(rewritten);
        Assert.Equal(1, report.CompressedToolResults);
        var parsed = JsonNode.Parse(output)!.AsObject();
        Assert.Contains("×200", parsed["input"]![0]!["output"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_CompressesGeminiFunctionResponse()
    {
        var parts = new JsonArray
        {
            new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = "run",
                    ["response"] = new JsonObject { ["content"] = LongText("gemini") },
                },
            },
        };
        var contents = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = parts } };
        var payload = new JsonObject { ["contents"] = contents }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(payload, "gemini", NoFidelityWindow, out var output, out var report);

        Assert.True(rewritten);
        Assert.Equal(1, report.CompressedToolResults);
        var parsed = JsonNode.Parse(output)!.AsObject();
        var response = parsed["contents"]![0]!["parts"]![0]!["functionResponse"]!["response"]!.AsObject();
        Assert.Contains("×200", response["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_KeepsMostRecentToolResultsVerbatim()
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "tool", ["content"] = LongText("oldest") },
            new JsonObject { ["role"] = "tool", ["content"] = LongText("older") },
            new JsonObject { ["role"] = "tool", ["content"] = LongText("recent") },
        };
        var payload = new JsonObject { ["messages"] = messages }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(
            payload, null, Options with { KeepRecentToolResults = 2 }, out var output, out var report);

        Assert.True(rewritten);
        Assert.Equal(3, report.TotalToolResults);
        Assert.Equal(1, report.CompressedToolResults);
        var parsed = JsonNode.Parse(output)!.AsObject();
        Assert.Contains("×200", parsed["messages"]![0]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain("×200", parsed["messages"]![1]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain("×200", parsed["messages"]![2]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_LeavesAssistantAndUserContentUntouched()
    {
        var assistantText = LongText("assistant");
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "assistant", ["content"] = assistantText },
            new JsonObject { ["role"] = "user", ["content"] = LongText("user") },
        };
        var payload = new JsonObject { ["messages"] = messages }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(payload, "openai", Options, out _, out _);

        Assert.False(rewritten);
    }

    [Fact]
    public void Rewrite_ReturnsFalseForNonJsonPayload()
    {
        var rewritten = ToolResultRequestRewriter.TryRewrite(
            "<html>not json</html>", "openai", Options, out _, out _);

        Assert.False(rewritten);
    }

    [Fact]
    public void Rewrite_ReturnsFalseWhenPluginDisabled()
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "tool", ["content"] = LongText("disabled") } };
        var payload = new JsonObject { ["messages"] = messages }.ToJsonString();

        var rewritten = ToolResultRequestRewriter.TryRewrite(
            payload, "openai", Options with { Enabled = false }, out _, out _);

        Assert.False(rewritten);
    }
}
