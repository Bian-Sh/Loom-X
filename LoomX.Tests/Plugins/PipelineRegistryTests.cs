using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>Extension 注册与 Pipeline 归属（spec: plugin-runtime-pipeline / Extension 注册与 Pipeline 归属）。</summary>
public sealed class PipelineRegistryTests
{
    private static Pipeline CreatePipeline(PipelineRegistry registry, string id, ExtensionKind kind) =>
        registry.GetOrCreatePipeline(id, kind, NullLogger.Instance);

    [Fact]
    public void SinglePlugin_RegistersMultipleExtensions_ToTheirOwnPipelines()
    {
        var registry = new PipelineRegistry();
        var toolResult = CreatePipeline(registry, "tool-result", ExtensionKind.ToolResult);
        var persistence = CreatePipeline(registry, "persistence", ExtensionKind.Persistence);

        Assert.True(registry.Register(toolResult, new PipelineEntry(new TestToolResultExtension("ext.a"), "demo")));
        Assert.True(registry.Register(persistence, new PipelineEntry(new TestPersistenceExtension("ext.b"), "demo")));

        Assert.Single(toolResult.Entries);
        Assert.Single(persistence.Entries);

        // 两个 Extension 可独立启用禁用
        toolResult.Entries[0].Enabled = false;
        Assert.False(toolResult.Entries[0].Enabled);
        Assert.True(persistence.Entries[0].Enabled);
    }

    [Fact]
    public void SameExtension_MountedToTwoPipelines_IsRejected()
    {
        var registry = new PipelineRegistry();
        var first = CreatePipeline(registry, "first", ExtensionKind.ToolResult);
        var second = CreatePipeline(registry, "second", ExtensionKind.ToolResult);
        var extension = new TestToolResultExtension("ext.shared");

        Assert.True(registry.Register(first, new PipelineEntry(extension, "demo")));
        Assert.False(registry.Register(second, new PipelineEntry(extension, "demo")));

        Assert.Single(first.Entries);
        Assert.Empty(second.Entries);
        Assert.Contains(registry.Diagnostics, item => item.Contains("重复挂载"));
    }

    [Fact]
    public void ExtensionKind_MismatchingPipeline_IsRejected()
    {
        var registry = new PipelineRegistry();
        var persistence = CreatePipeline(registry, "persistence", ExtensionKind.Persistence);

        Assert.False(registry.Register(persistence, new PipelineEntry(new TestToolResultExtension("ext.a"), "demo")));

        Assert.Empty(persistence.Entries);
        Assert.Contains(registry.Diagnostics, item => item.Contains("不匹配"));
    }

    [Fact]
    public void PipelineId_KindConflict_Throws()
    {
        var registry = new PipelineRegistry();
        CreatePipeline(registry, "shared-id", ExtensionKind.ToolResult);

        Assert.Throws<InvalidOperationException>(() =>
            CreatePipeline(registry, "shared-id", ExtensionKind.Persistence));
    }
}
