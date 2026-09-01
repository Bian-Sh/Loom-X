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
        Assert.Contains("Text=\"⋮⋮\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"icon drag-handle\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.drag-handle\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button Classes=\"icon\" Tag=\"{Binding}\" PointerPressed=\"RouteHandle_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("DragDrop.DragOver=\"Route_OnDragOver\"", source, StringComparison.Ordinal);
        Assert.Contains("DragDrop.Drop=\"Route_OnDrop\"", source, StringComparison.Ordinal);
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
        Assert.Contains("Text=\"&gt;\"", source, StringComparison.Ordinal);
        Assert.Contains("Angle=\"{Binding ExpandIconAngle}\"", source, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#16A34A\"", source, StringComparison.Ordinal);
        Assert.Contains("ColumnSpacing=\"12\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DragAndAlphabeticalSortAreHandledByGatewayInteractions()
    {
        var viewSource = ReadDesktopFile("Views", "GatewayView.axaml.cs");
        var viewModelSource = ReadDesktopFile("ViewModels", "GatewayViewModel.cs");

        Assert.Contains("private void Route_OnDragOver", viewSource, StringComparison.Ordinal);
        Assert.Contains("e.DragEffects = Guid.TryParse", viewSource, StringComparison.Ordinal);
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

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", .. segments]);
        return File.ReadAllText(path);
    }
}
