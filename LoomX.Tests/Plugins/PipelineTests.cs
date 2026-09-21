using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// Pipeline 有序执行、启用禁用与异常隔离
/// （spec: plugin-runtime-pipeline / 有序执行、启用禁用、异常隔离与失败策略）。
/// </summary>
public sealed class PipelineTests
{
    private static Pipeline CreatePipeline(string id = "tool-result", ExtensionKind kind = ExtensionKind.ToolResult) =>
        new(id, kind, NullLogger.Instance);

    [Fact]
    public async Task MultiplePlugins_ExecuteInConfiguredOrder_WithoutInterPluginAwareness()
    {
        var log = new List<string>();
        var pipeline = CreatePipeline();
        // 两个插件、三个 Entry：alpha 两个、beta 一个，配置顺序 b → a2 → a1
        var entryA1 = new PipelineEntry(new TestToolResultExtension("ext.a1", executionLog: log, tag: "a1"), "plugin.alpha");
        var entryA2 = new PipelineEntry(new TestToolResultExtension("ext.a2", executionLog: log, tag: "a2"), "plugin.alpha");
        var entryB = new PipelineEntry(new TestToolResultExtension("ext.b", executionLog: log, tag: "b"), "plugin.beta");
        pipeline.AddEntry(entryA1);
        pipeline.AddEntry(entryA2);
        pipeline.AddEntry(entryB);
        pipeline.Reorder([entryB, entryA2, entryA1]);

        var result = await pipeline.ExecuteAsync("payload");

        Assert.Equal(PipelineOutcome.Passed, result.Outcome);
        Assert.Equal(["b", "a2", "a1"], log);
    }

    [Fact]
    public async Task ModifiedPayload_ChainsToNextEntry()
    {
        var pipeline = CreatePipeline();
        pipeline.AddEntry(new PipelineEntry(new TestToolResultExtension("ext.upper", transform: text => text.ToUpperInvariant()), "plugin.a"));
        pipeline.AddEntry(new PipelineEntry(new TestToolResultExtension("ext.wrap", transform: text => $"[{text}]"), "plugin.b"));

        var result = await pipeline.ExecuteAsync("abc");

        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        Assert.Equal("[ABC]", result.Payload);
    }

    [Fact]
    public async Task DisabledEntry_IsSkipped_ReenableRestoresOrder()
    {
        var log = new List<string>();
        var pipeline = CreatePipeline();
        var first = new PipelineEntry(new TestToolResultExtension("ext.a", executionLog: log, tag: "a"), "plugin.a");
        var second = new PipelineEntry(new TestToolResultExtension("ext.b", executionLog: log, tag: "b"), "plugin.b");
        pipeline.AddEntry(first);
        pipeline.AddEntry(second);

        first.Enabled = false;
        await pipeline.ExecuteAsync("payload");
        Assert.Equal(["b"], log);

        log.Clear();
        first.Enabled = true;
        await pipeline.ExecuteAsync("payload");
        Assert.Equal(["a", "b"], log);
    }

    [Fact]
    public async Task OrdinaryEntryFailure_IsLoggedAndSkipped_MainFlowContinues()
    {
        var pipeline = CreatePipeline();
        var tail = new TestToolResultExtension("ext.tail", transform: text => text + "!");
        pipeline.AddEntry(new PipelineEntry(
            new ThrowingExtension("ext.observability", ExtensionKind.ToolResult, ExtensionFailurePolicy.ContinueOnError),
            "plugin.a"));
        pipeline.AddEntry(new PipelineEntry(tail, "plugin.b"));

        var result = await pipeline.ExecuteAsync("payload");

        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        Assert.Equal("payload!", result.Payload);
        Assert.Equal(1, tail.CallCount);
    }

    [Fact]
    public async Task DataSafetyEntryFailure_FailClosed_OriginalDataNotReleased()
    {
        const string secretPayload = "raw sk-abcdefghij0123456789abcd";
        var pipeline = CreatePipeline("persistence", ExtensionKind.Persistence);
        var tail = new TestPersistenceExtension("ext.tail");
        pipeline.AddEntry(new PipelineEntry(
            new ThrowingExtension("ext.safety", ExtensionKind.Persistence, ExtensionFailurePolicy.FailClosed),
            "plugin.a"));
        pipeline.AddEntry(new PipelineEntry(tail, "plugin.b"));

        var result = await pipeline.ExecuteAsync(secretPayload);

        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
        Assert.DoesNotContain(secretPayload, result.Payload, StringComparison.Ordinal);
        Assert.Equal(0, tail.CallCount); // 后续 Entry 不再接触原始数据
    }

    [Fact]
    public async Task EntryReturningBlocked_StopsPipeline()
    {
        var pipeline = CreatePipeline();
        var tail = new TestToolResultExtension("ext.tail");
        pipeline.AddEntry(new PipelineEntry(new BlockingToolResultExtension("ext.block"), "plugin.a"));
        pipeline.AddEntry(new PipelineEntry(tail, "plugin.b"));

        var result = await pipeline.ExecuteAsync("payload");

        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Equal(0, tail.CallCount);
    }

    [Fact]
    public async Task EmptyPipeline_PassesThrough()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync("payload");

        Assert.Equal(PipelineOutcome.Passed, result.Outcome);
        Assert.Equal("payload", result.Payload);
    }
}
