using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class ProvidersViewContractTests
{
    [Fact]
    public void ModelListDoesNotExposeModelEditorSelection()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedModel, Mode=TwoWay}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelChangesSelectTheChangedModelBeforeAutomaticSave()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("if (sender is not ModelEditorViewModel model) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (!ReferenceEquals(SelectedModel, model))", source, StringComparison.Ordinal);
        Assert.Contains("SelectedModel = model;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelRowsExposeDragHandleAndReadonlyMetadataLayout()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Equal(2, source.Split("ColumnDefinitions=\"30,38,*,Auto,40\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Classes=\"model-drag-handle\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"model-drag-placeholder\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"model-drag-preview\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"ModelHandle_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("ToggleAllModelsCommand", source, StringComparison.Ordinal);
        Assert.Contains("EnabledModelSummary", source, StringComparison.Ordinal);
        Assert.Contains("RemoteVision", source, StringComparison.Ordinal);
        Assert.Contains("DeleteModelCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Pencil", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"添加模型\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"模型配置\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedModel.DisplayName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelDragUsesGatewayPlaceholderAndPreviewFlow()
    {
        var viewSource = ReadDesktopFile("Views", "ProvidersView.axaml.cs");
        var viewModelSource = ReadDesktopFile("ViewModels", "MainWindowViewModel.cs");

        Assert.Contains("FindModelDragHost", viewSource, StringComparison.Ordinal);
        Assert.Contains("modelDragPointerOffsetY", viewSource, StringComparison.Ordinal);
        Assert.Contains("previewCenterY", viewSource, StringComparison.Ordinal);
        Assert.Contains("AnimateMovedRows", viewSource, StringComparison.Ordinal);
        Assert.Contains("CompleteModelDragAsync", viewSource, StringComparison.Ordinal);
        Assert.Contains("BeginModelDrag", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("modelDragPlaceholder", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("MoveModelDragPlaceholder", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ClearModelDragState", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("IsModelDragPreviewOwner", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("model.IsRealModel", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("private async Task ToggleAllModelsAsync()", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("await SaveModelAsync(provider, model);", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteHeadersShowPersistentWarning()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("SelectedProvider.HasIncompleteHeaders", source, StringComparison.Ordinal);
        Assert.Contains("SelectedProvider.IncompleteHeaderCount", source, StringComparison.Ordinal);
        Assert.Contains("补全名称和值后才会保存", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSyncButtonUsesCompactRotatingIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("Classes=\"icon-glyph\" Data=\"M 26,12 A 10,10 0 1,0 23,20", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"12\" Height=\"12\" RenderTransformOrigin=\"50%,50%\"", source, StringComparison.Ordinal);
        Assert.Contains("<RotateTransform Angle=\"{Binding SyncIconAngle}\"/>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSyncReportsToastAndStopsAnimationForCurrentRequest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("toastService.Show(Status, ToastLevel.Success);", source, StringComparison.Ordinal);
        Assert.Contains("toastService.Show(Status, ToastLevel.Error);", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("StopModelSyncAnimation();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSavesSuppressConfigurationRefreshThatWouldReplaceEditorControls()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("private bool suppressConfigurationRefresh;", source, StringComparison.Ordinal);
        Assert.Contains("if (suppressConfigurationRefresh) return;", source, StringComparison.Ordinal);
        Assert.Contains("suppressConfigurationRefresh = true;", source, StringComparison.Ordinal);
        Assert.Contains("finally { suppressConfigurationRefresh = false; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticSavesSkipUnchangedEditors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("public bool HasUnsavedChanges => Id == Guid.Empty || isDirty;", source, StringComparison.Ordinal);
        Assert.Contains("if (!provider.HasUnsavedChanges) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (!model.HasUnsavedChanges) return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderEditorUsesViewModelChangesInsteadOfFocusSaves()
    {
        var viewModelPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var codeBehindPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml.cs");
        var viewModelSource = File.ReadAllText(viewModelPath);
        var viewSource = File.ReadAllText(viewPath);
        var codeBehindSource = File.ReadAllText(codeBehindPath);

        Assert.DoesNotContain("ScheduleAutoSave", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleModelAutoSave", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LostFocus=", viewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderEditorField_OnLostFocus", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelEditorField_OnLostFocus", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderEditorSelectionChanged", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelEditorToggle_OnClick", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("provider.PropertyChanged += ProviderChanged;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("model.PropertyChanged += ModelChanged;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("QueueProviderAutoSave(provider)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("QueueModelAutoSave(provider, model)", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseUrlHelpControlUsesAdjacentQuestionMarkAndHelpCursor()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("<StackPanel Orientation=\"Horizontal\" Spacing=\"4\"><TextBlock Text=\"Base URL\"", source, StringComparison.Ordinal);
        Assert.Contains("Cursor=\"Help\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"16\" Height=\"16\"", source, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"?\" FontSize=\"10\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"?\" FontSize=\"12\" FontWeight=\"SemiBold\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Data=\"M 10,10 A 5,5", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDirectoryMatchesEndpointListSurfaceContract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("Classes=\"provider-list\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceSubtleBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem:selected\"><Setter Property=\"Background\" Value=\"{DynamicResource AccentSoftBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"0,0,0,1\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Padding=\"14,12\" Background=\"Transparent\" BorderBrush=\"{DynamicResource BorderStrongBrush}\" BorderThickness=\"0,0,0,1\">", source, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }

    [Fact]
    public void ModelDirectoryMatchesProviderDirectoryTransparencyContract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("Classes=\"model-list\"", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.model-list\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.model-list ListBoxItem\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceSubtleBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.model-list ListBoxItem:selected\"><Setter Property=\"Background\" Value=\"{DynamicResource AccentSoftBrush}\"/>", source, StringComparison.Ordinal);
    }
}
