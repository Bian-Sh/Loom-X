using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginUiValidatorTests
{
    [Fact]
    public void IconOnlyButton_AllowsBoundedTooltip()
    {
        var contribution = Contribution(new PluginUiButtonNode(
            "删除",
            "delete",
            ToolTip: "删除",
            IsIconOnly: true,
            IconData: "M1,1 L9,9 L8,10 L0,2 Z"));

        Assert.True(PluginUiValidator.Validate(contribution).IsValid);
    }

    [Fact]
    public void Button_RejectsOversizedTooltip()
    {
        var contribution = Contribution(new PluginUiButtonNode(
            "×",
            "delete",
            ToolTip: new string('x', 257),
            IsIconOnly: true));

        var result = PluginUiValidator.Validate(contribution);

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_LABEL", result.ReasonCode);
    }

    [Fact]
    public void Button_RejectsOversizedIconGeometry()
    {
        var contribution = Contribution(new PluginUiButtonNode(
            "删除",
            "delete",
            ToolTip: "删除",
            IsIconOnly: true,
            IconData: new string('M', 2049)));

        var result = PluginUiValidator.Validate(contribution);

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_ICON_DATA", result.ReasonCode);
    }

    private static PluginUiContribution Contribution(PluginUiNode root) => new(
        PluginUiContribution.CurrentSchemaVersion,
        "test",
        PluginUiSlot.DetailBody,
        root);
}
