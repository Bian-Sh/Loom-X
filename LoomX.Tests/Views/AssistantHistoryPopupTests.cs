using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using LoomX.Views;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class AssistantHistoryPopupTests
{
    [Fact]
    public void HistoryPopupUsesNativePopupHost()
    {
        AvaloniaTestBootstrap.Ensure();

        var view = new AssistantView();
        var popup = view.FindControl<Popup>("historyPopup");

        Assert.NotNull(popup);
        Assert.False(popup.ShouldUseOverlayLayer);
        Assert.Equal(PlacementMode.AnchorAndGravity, popup.Placement);
        Assert.Equal(PopupAnchor.Bottom, popup.PlacementAnchor);
        Assert.Equal(PopupGravity.Bottom, popup.PlacementGravity);
    }
}
