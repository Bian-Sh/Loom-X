using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class ToolRegistryTests
{
    [Fact]
    public void Register_ThenTryGet_ReturnsTool()
    {
        var registry = new ToolRegistry();
        var tool = MockTools.CreateListProvidersTool();

        registry.Register(tool);

        Assert.True(registry.TryGet("mock.list_providers", out var found));
        Assert.Same(tool, found);
        Assert.Single(registry.All);
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());

        Assert.True(registry.TryGet("MOCK.LIST_PROVIDERS", out _));
    }

    [Fact]
    public void Register_DuplicateName_Throws()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());

        Assert.Throws<InvalidOperationException>(() => registry.Register(MockTools.CreateListProvidersTool()));
    }

    [Fact]
    public void Register_EmptyName_Throws()
    {
        var registry = new ToolRegistry();
        var tool = MockTools.CreateListProvidersTool();
        tool = new ToolDefinition
        {
            Name = " ",
            Description = tool.Description,
            ParametersSchema = tool.ParametersSchema,
            Handler = tool.Handler,
        };

        Assert.Throws<ArgumentException>(() => registry.Register(tool));
    }

    [Fact]
    public void TryGet_UnknownTool_ReturnsFalse()
    {
        var registry = new ToolRegistry();

        Assert.False(registry.TryGet("loomx.list_providers", out var tool));
        Assert.Null(tool);
    }
}
