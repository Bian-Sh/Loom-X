using System.IO;
using OllamaHub.Desktop.ViewModels;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class GatewayViewContractTests
{
    [Fact]
    public void ComboMembersUseBottomFootbarAndProviderStyleDragHandle()
    {
        var source = ReadDesktopFile("Views", "GatewayView.axaml");

        Assert.Contains("Classes=\"member-footbar\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"member-footbar\">", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("<ItemsControl ItemsSource=\"{Binding Routes}\">", StringComparison.Ordinal) <
            source.IndexOf("<Border Classes=\"member-footbar\">", StringComparison.Ordinal));
        Assert.Contains("Classes=\"drag-dot\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"icon drag-handle\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.drag-handle\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button Classes=\"icon\" Tag=\"{Binding}\" PointerPressed=\"RouteHandle_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("drag-placeholder", source, StringComparison.Ordinal);
        Assert.Contains("route-drag-preview", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPlaceholder}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding $parent[UserControl].DataContext.IsRouteDragActive}\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.member-footbar\"", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"Padding\" Value=\"8,2\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.footbar-add\"", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"14\" Height=\"14\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelPickerUsesIconSortAndIndentedProviderGroups()
    {
        var source = ReadDesktopFile("Views", "GatewayView.axaml");

        Assert.Contains("Click=\"ModelSort_OnClick\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SortModeOptions", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\" Text=\"{Binding ProviderName}\" FontWeight=\"SemiBold\"", source, StringComparison.Ordinal);
        Assert.Contains("Margin=\"22,0,0,0\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"", source, StringComparison.Ordinal);
        Assert.Contains("Data=\"M 11,7 L 19,16 L 11,25\"", source, StringComparison.Ordinal);
        Assert.Contains("Angle=\"{Binding ExpandIconAngle}\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource SuccessBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("ColumnSpacing=\"12\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DragAndAlphabeticalSortAreHandledByGatewayInteractions()
    {
        var viewSource = ReadDesktopFile("Views", "GatewayView.axaml.cs");
        var viewModelSource = ReadDesktopFile("ViewModels", "GatewayViewModel.cs");

        Assert.Contains("private void RouteDrag_OnPointerMoved", viewSource, StringComparison.Ordinal);
        Assert.Contains("if (e.Handled) return;", viewSource, StringComparison.Ordinal);
        Assert.Contains("GetInsertionSlot", viewSource, StringComparison.Ordinal);
        Assert.Contains("AnimateMovedRows", viewSource, StringComparison.Ordinal);
        Assert.Contains("CompleteRouteDragAsync", viewSource, StringComparison.Ordinal);
        Assert.Contains("BeginRouteDrag", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("MoveRouteDragPlaceholder", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("CancelRouteDrag", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public void ToggleModelSortDirection()", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("FilterModels(modelSearchTerm);", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("grouped.OrderByDescending", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public double ExpandIconAngle => IsExpanded ? 90 : 0;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("item.MatchesSearch(modelSearchTerm)", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSearchMatchesModelNameButNotProviderName()
    {
        var unrelatedModel = new GatewayModelOption(Guid.NewGuid(), "ChatGPT-5.6", "Deepseek");
        var matchingModel = new GatewayModelOption(Guid.NewGuid(), "deepseek-v4-flash", "SensenoVa");

        Assert.False(unrelatedModel.MatchesSearch("deep"));
        Assert.True(matchingModel.MatchesSearch("deep"));
    }

    [Fact]
    public void CopyButtonsUseOriginalGlyph()
    {
        var gatewaySource = ReadDesktopFile("Views", "GatewayView.axaml");
        var consoleSource = ReadDesktopFile("Views", "ConsoleView.axaml");

        Assert.Contains("Classes=\"icon endpoint-copy\" Content=\"⧉\"", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("Classes=\"copy-log\" Content=\"⧉\"", consoleSource, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.copy-log\"", consoleSource, StringComparison.Ordinal);
        Assert.Contains("Property=\"FontSize\" Value=\"16\"", consoleSource, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
