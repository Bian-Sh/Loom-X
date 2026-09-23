using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>Extension 注册与 Router Pipeline 归属。</summary>
public sealed class PipelineRegistryTests
{
    private static Pipeline CreatePipeline(PipelineRegistry registry, string id, ExtensionKind kind) =>
        registry.GetOrCreatePipeline(id, kind, NullLogger.Instance);

    [Fact]
    public void SinglePlugin_RegistersRequestAndResponseExtensions_ToTheirOwnPipelines()
    {
        var registry = new PipelineRegistry();
        var request = CreatePipeline(registry, "request", ExtensionKind.Request);
        var response = CreatePipeline(registry, "response", ExtensionKind.Response);

        Assert.True(registry.Register(request, new PipelineEntry(new TestRequestExtension("ext.request"), "demo")));
        Assert.True(registry.Register(response, new PipelineEntry(new TestResponseExtension("ext.response"), "demo")));

        Assert.Single(request.Entries);
        Assert.Single(response.Entries);
        request.Entries[0].Enabled = false;
        Assert.False(request.Entries[0].Enabled);
        Assert.True(response.Entries[0].Enabled);
    }

    [Fact]
    public void SameExtension_MountedToTwoPipelines_IsRejected()
    {
        var registry = new PipelineRegistry();
        var first = CreatePipeline(registry, "first", ExtensionKind.Request);
        var second = CreatePipeline(registry, "second", ExtensionKind.Request);
        var extension = new TestRequestExtension("ext.shared");

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
        var response = CreatePipeline(registry, "response", ExtensionKind.Response);

        Assert.False(registry.Register(response, new PipelineEntry(new TestRequestExtension("ext.a"), "demo")));

        Assert.Empty(response.Entries);
        Assert.Contains(registry.Diagnostics, item => item.Contains("不匹配"));
    }

    [Fact]
    public void PipelineId_KindConflict_Throws()
    {
        var registry = new PipelineRegistry();
        CreatePipeline(registry, "shared-id", ExtensionKind.Request);

        Assert.Throws<InvalidOperationException>(() =>
            CreatePipeline(registry, "shared-id", ExtensionKind.Response));
    }
}
