using System.IO;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Views;

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
        Assert.Contains("PointerPressed=\"RouteHandle_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.drag-handle\"", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"Cursor\" Value=\"SizeAll\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("drag-handle\" Tag=\"{Binding}\" IsEnabled=\"{Binding IsDragEnabled}\" PointerPressed=\"RouteHandle_OnPointerPressed\" ToolTip.Tip=\"拖动调整故障转移顺序\" AutomationProperties.Name=\"拖动调整故障转移顺序\" Cursor=\"SizeAll\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button Classes=\"icon\" Tag=\"{Binding}\" PointerPressed=\"RouteHandle_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("drag-placeholder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("释放到此处", source, StringComparison.Ordinal);
        Assert.Contains("<Grid Height=\"36\"/>", source, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"0,1,0,0\"", source, StringComparison.Ordinal);
        Assert.Contains("route-drag-preview", source, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsDragEnabled}\"", source, StringComparison.Ordinal);
        Assert.Contains("Cursor\" Value=\"No\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPlaceholder}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsDragPreviewOwner}\"", source, StringComparison.Ordinal);
        Assert.Contains("DraggingRoute.ModelName", source, StringComparison.Ordinal);
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
    public void ModelPickerUsesDedicatedDynamicPopupSurface()
    {
        var source = ReadDesktopFile("Views", "GatewayView.axaml");

        Assert.Contains("x:Name=\"modelPickerPanel\"", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"360\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"420\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource PopupBackgroundBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsLightDismissEnabled=\"True\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointListUsesThemeResponsiveCellSpacing()
    {
        var source = ReadDesktopFile("Views", "GatewayView.axaml");

        Assert.Contains("Selector=\"ListBox.endpoint-list\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.endpoint-list ListBoxItem\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceSubtleBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"0,0,0,1\"", source, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{DynamicResource BorderStrongBrush}\" BorderThickness=\"0,0,0,1\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.endpoint-list ListBoxItem:selected\"><Setter Property=\"Background\" Value=\"{DynamicResource AccentSoftBrush}\"/>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DragAndAlphabeticalSortAreHandledByGatewayInteractions()
    {
        var viewSource = ReadDesktopFile("Views", "GatewayView.axaml.cs");
        var viewModelSource = ReadDesktopFile("ViewModels", "GatewayViewModel.cs");

        Assert.Contains("private void RouteDrag_OnPointerMoved", viewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHandler(InputElement.PointerPressedEvent", viewSource, StringComparison.Ordinal);
        Assert.Contains("GetInsertionSlot", viewSource, StringComparison.Ordinal);
        Assert.Contains("previewCenterY", viewSource, StringComparison.Ordinal);
        Assert.Contains("AnimateMovedRows", viewSource, StringComparison.Ordinal);
        Assert.Contains("CompleteRouteDragAsync", viewSource, StringComparison.Ordinal);
        Assert.True(
            viewSource.IndexOf("var routeTop", StringComparison.Ordinal) <
            viewSource.IndexOf("if (!viewModel.BeginRouteDrag", StringComparison.Ordinal));
        Assert.Contains("BeginRouteDrag", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("!SelectedCombo.CanDragRoutes", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("dragOwnerCombo", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (dragOwnerCombo is null || dragPlaceholder is null)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("MoveRouteDragPlaceholder", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("CancelRouteDrag", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("IsDragPreviewOwner", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public void ToggleModelSortDirection()", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("FilterModels(modelSearchTerm);", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("grouped.OrderByDescending", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public double ExpandIconAngle => IsExpanded ? 90 : 0;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("item.MatchesSearch(modelSearchTerm)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public bool CanDragRoutes => Routes.Count > 1;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("IsDragEnabled", viewModelSource, StringComparison.Ordinal);
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
    public void ComboDragAvailabilityFollowsRouteCount()
    {
        var combo = new GatewayComboEditorViewModel();
        var first = new GatewayRouteEditorViewModel();
        var second = new GatewayRouteEditorViewModel();

        combo.Routes.Add(first);
        Assert.False(combo.CanDragRoutes);
        Assert.False(first.IsDragEnabled);

        combo.Routes.Add(second);
        Assert.True(combo.CanDragRoutes);
        Assert.True(first.IsDragEnabled);
        Assert.True(second.IsDragEnabled);

        combo.Routes.Remove(second);
        Assert.False(combo.CanDragRoutes);
        Assert.False(first.IsDragEnabled);
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
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
