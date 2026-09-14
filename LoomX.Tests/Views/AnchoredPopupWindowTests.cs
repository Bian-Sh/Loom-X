using LoomX.Views;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class AnchoredPopupWindowTests
{
    [Fact]
    public void CompleteInteractionClosesPopupBeforeActivatingOwner()
    {
        var steps = new List<string>();

        AnchoredPopupWindow.CompleteInteraction(
            () => steps.Add("activate"),
            () => steps.Add("close"));

        Assert.Equal(["close", "activate"], steps);
    }
}
