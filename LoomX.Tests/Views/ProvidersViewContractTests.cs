using System.IO;
using Avalonia.Input;
using LoomX.Views;
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

        Assert.Equal(2, source.Split("ColumnDefinitions=\"30,46,*,Auto,42\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Classes=\"model-drag-handle\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"model-drag-placeholder\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"model-drag-preview\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"ModelHandle_OnPointerPressed\"", source, StringComparison.Ordinal);
        Assert.Contains("ToggleAllModelsCommand", source, StringComparison.Ordinal);
        Assert.Contains("EnabledModelSummary", source, StringComparison.Ordinal);
        Assert.Contains("RemoteVision", source, StringComparison.Ordinal);
        Assert.Contains("MetadataToolTip", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{l:Locale providers.models.vision.tooltip}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ModelSearchQuery, UpdateSourceTrigger=PropertyChanged}\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FilteredModels}\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", source, StringComparison.Ordinal);
        Assert.Contains("Data=\"M 10,5 A 1,1 0 1 1 8,5", source, StringComparison.Ordinal);
        Assert.Contains("model-drag-glyph", source, StringComparison.Ordinal);
        Assert.Contains("Data=\"M 10,11 V 17 M 14,11 V 17", source, StringComparison.Ordinal);
        Assert.Contains("<Grid Height=\"28\" ColumnDefinitions=\"30,46,*,Auto,42\"", source, StringComparison.Ordinal);
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
        Assert.Contains("handleBorder.FindAncestorOfType<Border>()", viewSource, StringComparison.Ordinal);
        Assert.Contains("e.GetPosition(modelBorder)", viewSource, StringComparison.Ordinal);
        Assert.Contains("previewCenterY", viewSource, StringComparison.Ordinal);
        Assert.Contains("AnimateMovedRows", viewSource, StringComparison.Ordinal);
        Assert.Contains("CompleteModelDragAsync", viewSource, StringComparison.Ordinal);
        Assert.Contains("BeginModelDrag", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("modelDragPlaceholder", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("MoveModelDragPlaceholder", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ClearModelDragState", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("IsModelDragPreviewOwner", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("GetSelectedProviderModelsForSummary", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("models.Add(DraggingModel)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("FilteredModels", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("HasModelSearchQuery", viewModelSource, StringComparison.Ordinal);
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
        Assert.Contains("{l:Locale providers.headers.incomplete.suffix}", source, StringComparison.Ordinal);
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
    public void CliIdentityPopupExposesApplyEditRefreshAndCurrentStatusControls()
    {
        var viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var viewModelPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var viewSource = File.ReadAllText(viewPath);
        var viewModelSource = File.ReadAllText(viewModelPath);

        Assert.Contains("ApplyCliIdentityCommand", viewSource, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Version, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("IsVersionReadOnly", viewSource, StringComparison.Ordinal);
        Assert.Contains("RefreshCliVersionsCommand", viewSource, StringComparison.Ordinal);
        Assert.Contains("CurrentCliIdentitySummary", viewSource, StringComparison.Ordinal);
        Assert.Contains("CurrentCliIdentitySummary", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("CliIdentityItemChanged", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSavesSuppressConfigurationRefreshThatWouldReplaceEditorControls()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("private bool suppressConfigurationRefresh;", source, StringComparison.Ordinal);
        Assert.Contains("suppressConfigurationRefresh = true;", source, StringComparison.Ordinal);
        Assert.Contains("finally { suppressConfigurationRefresh = false; }", source, StringComparison.Ordinal);
        // 本机保存事件必须携带 LocalSave 来源，Providers 页据此跳过列表重建，编辑中的实例原地保留。
        Assert.Contains("if (args.Source == ConfigurationChangeSource.LocalSave) return;", source, StringComparison.Ordinal);
        Assert.Contains("private void MergeProviders(IReadOnlyList<ProviderResponse> responses)", source, StringComparison.Ordinal);
        Assert.Contains("Providers.RemoveAt(index);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Providers.Add(ProviderEditorViewModel.FromResponse(provider));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticSavesSkipUnchangedEditors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("public bool HasUnsavedChanges => Id == Guid.Empty || isDirty || editRevision != savedEditRevision;", source, StringComparison.Ordinal);
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
        Assert.DoesNotContain("DebouncedAutoSaver", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim providerSaveLock", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim modelSaveLock", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("_ = SaveProviderAsync(provider);", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("_ = SaveModelAsync(provider, model, enabledOnly", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("dataStore.UpdateModelEnabledAsync(model.Id, model.Enabled)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ProviderEditorViewModel.IsPersistedProperty(args.PropertyName)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ModelEditorViewModel.IsPersistedProperty(args.PropertyName)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", viewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseUrlHelpControlUsesAdjacentQuestionMarkAndHelpCursor()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("<StackPanel Orientation=\"Horizontal\" Spacing=\"4\"><TextBlock Text=\"{l:Locale providers.baseurl.label}\"", source, StringComparison.Ordinal);
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
        Assert.Contains("Selector=\"ListBox.provider-list\"><Setter Property=\"Background\" Value=\"Transparent\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.provider-card\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceSubtleBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem\"><Setter Property=\"Background\" Value=\"Transparent\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem:selected\"><Setter Property=\"Background\" Value=\"Transparent\"/><Setter Property=\"BorderBrush\" Value=\"{DynamicResource AccentBrush}\"/><Setter Property=\"BorderThickness\" Value=\"3,0,0,0\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter\"><Setter Property=\"Background\" Value=\"Transparent\"/><Setter Property=\"Foreground\" Value=\"{DynamicResource TextPrimaryBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem:pointerover Border.provider-card\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceMutedBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem:selected:pointerover Border.provider-card\"><Setter Property=\"Background\" Value=\"{DynamicResource SurfaceMutedBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ListBox.provider-list ListBoxItem:selected:pointerover /template/ ContentPresenter#PART_ContentPresenter\"><Setter Property=\"Background\" Value=\"Transparent\"/><Setter Property=\"Foreground\" Value=\"{DynamicResource TextPrimaryBrush}\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"0,0,0,1\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"provider-card\" Padding=\"10,8\" Background=\"{DynamicResource SurfaceSubtleBrush}\" BorderBrush=\"{DynamicResource BorderStrongBrush}\" BorderThickness=\"0,0,0,1\">", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDirectorySearchAndCompactCellContract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);
        const string deletePath = "M 10,11 V 17 M 14,11 V 17 M 19,6 V 20 A 2,2 0 0 1 17,22 H 7 A 2,2 0 0 1 5,20 V 6 M 3,6 H 21 M 8,6 V 4 A 2,2 0 0 1 10,2 H 14 A 2,2 0 0 1 16,4 V 6";

        Assert.Contains("Text=\"{l:Locale providers.header.list}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProviderSearchQuery, UpdateSourceTrigger=PropertyChanged}\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FilteredProviders}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid ColumnDefinitions=\"3,*\">", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVisible=\"{Binding $parent[ListBoxItem].IsSelected}\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid RowDefinitions=\"Auto,Auto,Auto\"", source, StringComparison.Ordinal);
        Assert.Contains("Orientation=\"Horizontal\" Spacing=\"5\" VerticalAlignment=\"Center\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{l:Locale providers.toggle.tooltip.enabled}\"", source, StringComparison.Ordinal);
        Assert.Contains("<TranslateTransform X=\"12\"/>", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\" VerticalAlignment=\"Bottom\"", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split(deletePath, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Text=\"P\" Foreground=\"{DynamicResource AccentBrush}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"Auto,Auto,Auto,Auto\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderHealthSurfaceBindsAggregateAndPerProviderState()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");

        Assert.Contains("Text=\"{Binding ProviderHealthSummary}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EnabledProviderCount}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding VerifyAllProvidersCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding HealthDetailText}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsHealthSuccess}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsHealthError}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsHealthWarning}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsHealthChecking}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderEditorUsesFourLocalizedTabsAndRemovesLegacyConnectionBlock()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        Assert.Equal(4, source.Split("<TabItem Header=", StringSplitOptions.None).Length - 1);
        Assert.Contains("providers.tab.basic", source, StringComparison.Ordinal);
        Assert.Contains("providers.tab.advanced", source, StringComparison.Ordinal);
        Assert.Contains("providers.tab.models", source, StringComparison.Ordinal);
        Assert.Contains("providers.tab.test", source, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.tab.request", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TestConnectionCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.connection.test", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BasicTabUsesCompactCompatibilitySelectorHidesProviderIdAndOwnsApiKey()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var basic = ReadTab(source, "providers.tab.basic");
        var advanced = ReadTab(source, "providers.tab.advanced");
        Assert.DoesNotContain("SelectedProvider.BusinessId", basic, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SelectedProvider.CompatibilityOptions}\"", basic, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedProvider.SelectedCompatibility, Mode=TwoWay}\"", basic, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupName=\"ProviderCompatibility\"", basic, StringComparison.Ordinal);

        var itemTemplateStart = basic.IndexOf("<ComboBox.ItemTemplate>", StringComparison.Ordinal);
        var itemTemplateEnd = basic.IndexOf("</ComboBox.ItemTemplate>", StringComparison.Ordinal);
        Assert.True(itemTemplateStart >= 0 && itemTemplateEnd > itemTemplateStart);
        var itemTemplate = basic[itemTemplateStart..itemTemplateEnd];
        Assert.Contains("providers.compat.chat.title", itemTemplate, StringComparison.Ordinal);
        Assert.Contains("providers.compat.responses.title", itemTemplate, StringComparison.Ordinal);
        Assert.Contains("providers.compat.anthropic.title", itemTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.compat.chat.description", itemTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.compat.responses.description", itemTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.compat.anthropic.description", itemTemplate, StringComparison.Ordinal);
        Assert.Contains("providers.compat.chat.description", basic, StringComparison.Ordinal);
        Assert.Contains("providers.compat.responses.description", basic, StringComparison.Ordinal);
        Assert.Contains("providers.compat.anthropic.description", basic, StringComparison.Ordinal);
        Assert.Contains("SelectedProvider.ApiKey", basic, StringComparison.Ordinal);
        Assert.Contains("<Grid ColumnDefinitions=\"*,42\" HorizontalAlignment=\"Stretch\" Margin=\"0,0,8,0\"><TextBox Grid.ColumnSpan=\"2\" HorizontalAlignment=\"Stretch\" Text=\"{Binding SelectedProvider.ApiKey", basic, StringComparison.Ordinal);
        Assert.Contains("Padding=\"12,9,46,9\"/><Button Grid.Column=\"1\" HorizontalAlignment=\"Center\" Width=\"38\" Height=\"34\" Margin=\"0,0,4,0\"", basic, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid ColumnDefinitions=\"*,Auto\"><TextBox Text=\"{Binding SelectedProvider.ApiKey", basic, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button Grid.Column=\"1\" Width=\"38\" Height=\"34\" Margin=\"-42,0,4,0\"", basic, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedProvider.ApiKey", advanced, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedAndTestTabsExposeSafeConfigurationAndTestBindings()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var advanced = ReadTab(source, "providers.tab.advanced");
        var test = ReadTab(source, "providers.tab.test");
        Assert.Contains("SelectedProvider.UseProxy", advanced, StringComparison.Ordinal);
        Assert.Contains("SelectedProvider.Headers", advanced, StringComparison.Ordinal);
        Assert.Contains("SelectedProvider.CliIdentities", advanced, StringComparison.Ordinal);
        Assert.Contains("TestPanel.SelectedModel", test, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TestPanel.TestableModels}\"", test, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding TestPanel.HasTestableModels, Converter={StaticResource ProviderBooleanNotConverter}}\"", test, StringComparison.Ordinal);
        Assert.Contains("providers.test.model.empty", test, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedProvider.Models}\"", test, StringComparison.Ordinal);
        Assert.Contains("TestPanel.SelectedMode", test, StringComparison.Ordinal);
        Assert.Contains("TestPanel.Prompt", test, StringComparison.Ordinal);
        Assert.Contains("TestPanel.SendCommand", test, StringComparison.Ordinal);
        Assert.DoesNotContain("TestPanel.ClearCommand", test, StringComparison.Ordinal);
        Assert.Contains("TestPanel.ResponseText", test, StringComparison.Ordinal);
        Assert.Contains("TestPanel.RequestSummary", test, StringComparison.Ordinal);
        Assert.Contains("providers.test.send", test, StringComparison.Ordinal);
        Assert.Contains("providers.test.stop", test, StringComparison.Ordinal);
        Assert.DoesNotContain("IsIndeterminate=\"True\"", test, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn=\"False\"", test, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", test, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.test.prompt.label", test, StringComparison.Ordinal);
        Assert.Contains("TestPanel.StopCommand", test, StringComparison.Ordinal);
        Assert.DoesNotContain("TestPanel.RetryCommand", test, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.test.copy", test, StringComparison.Ordinal);
        Assert.DoesNotContain("TestPanel.Summary.ProviderId", test, StringComparison.Ordinal);
        Assert.DoesNotContain("TestPanel.Summary.ModelId", test, StringComparison.Ordinal);
        Assert.DoesNotContain("TestPanel.Summary.RequestId", test, StringComparison.Ordinal);
    }

    [Fact]
    public void SendAndStopButtonsShareInputAreaWithoutBufferingIndicator()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var test = ReadTab(source, "providers.tab.test");

        Assert.Contains("Command=\"{Binding TestPanel.SendCommand}\"", test, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding TestPanel.StopCommand}\"", test, StringComparison.Ordinal);
        Assert.Contains("Content=\"{l:Locale providers.test.send}\"", test, StringComparison.Ordinal);
        Assert.Contains("Content=\"{l:Locale providers.test.stop}\"", test, StringComparison.Ordinal);
        Assert.DoesNotContain("IsIndeterminate=\"True\"", test, StringComparison.Ordinal);
        Assert.DoesNotContain("M 4,4 L 24,12 L 4,20 L 8,12 Z", test, StringComparison.Ordinal);
    }
    [Fact]
    public void ResponseUsesSelectableReadonlyTextWithoutCopyHandler()
    {
        var view = ReadDesktopFile("Views", "ProvidersView.axaml");
        var codeBehind = ReadDesktopFile("Views", "ProvidersView.axaml.cs");

        Assert.Contains("Text=\"{Binding TestPanel.ResponseText}\"", view, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TestResponseTextBox\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDown=\"TestResponseTextBox_OnKeyDown\"", view, StringComparison.Ordinal);
        Assert.Contains("AddHandler(InputElement.KeyDownEvent, TestResponseTextBox_OnKeyDown, RoutingStrategies.Tunnel, true)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(e.Source, TestResponseTextBox)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.test.response.title", view, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.test.clear", view, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyTestResponseButton_OnClick", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<ContextMenu", view, StringComparison.Ordinal);
        Assert.Contains("providers.test.response.copy", view, StringComparison.Ordinal);
        Assert.Contains("providers.test.response.delete", view, StringComparison.Ordinal);
        Assert.Contains("providers.test.response.selectAll", view, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.test.response.cut", view, StringComparison.Ordinal);
        Assert.DoesNotContain("providers.test.response.paste", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseShowsLocalizedEmptyStateAndHidesScrollableTextUntilResultExists()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var test = ReadTab(source, "providers.tab.test");

        Assert.Contains("Text=\"{l:Locale providers.test.response.empty}\"", test, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding TestPanel.HasResponseText, Converter={StaticResource ProviderBooleanNotConverter}}\"", test, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding TestPanel.HasResponseText}\"", test, StringComparison.Ordinal);
        var responseStart = test.IndexOf("<TextBox x:Name=\"TestResponseTextBox\"", StringComparison.Ordinal);
        var responseEnd = test.IndexOf('>', responseStart);
        var responseTag = test[responseStart..responseEnd];
        Assert.Contains("MaxHeight=\"320\"", responseTag, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", responseTag, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", responseTag, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", responseTag, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(Key.Delete, false, 6, 0, 6, true)]
    [InlineData(Key.Back, false, 6, 6, 0, true)]
    [InlineData(Key.Delete, false, 6, 1, 4, true)]
    [InlineData(Key.Back, false, 6, 4, 1, true)]
    [InlineData(Key.Delete, false, 6, 3, 3, false)]
    [InlineData(Key.Delete, true, 6, 0, 6, false)]
    [InlineData(Key.Delete, false, 0, 0, 0, false)]
    [InlineData(Key.Enter, false, 6, 0, 6, false)]
    public void Response在请求结束后允许删除任意非空选区(
        Key key,
        bool isRunning,
        int textLength,
        int selectionStart,
        int selectionEnd,
        bool expected)
    {
        Assert.Equal(expected, ProvidersView.ShouldDeleteTestResponseSelection(
            key,
            isRunning,
            textLength,
            selectionStart,
            selectionEnd));
    }

    [Fact]
    public void MainWindowInjectsProviderTestLoggerIntoConsoleLifecycle()
    {
        var source = ReadDesktopFile("ViewModels", "MainWindowViewModel.cs");

        Assert.Contains("CreateLogger<ProviderTestService>()", source, StringComparison.Ordinal);
        Assert.Contains("providerTestLogger", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderTabsKeepScrollbarsInNativeLayerAndInsetOnlyContent()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");

        Assert.Contains("Selector=\"ScrollViewer.provider-tab-scroll\"><Setter Property=\"Margin\" Value=\"0\"/><Setter Property=\"Padding\" Value=\"0,0,8,0\"/></Style>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer.provider-tab-scroll /template/ ScrollBar#PART_VerticalScrollBar", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<TranslateTransform X=\"8\"/>", source, StringComparison.Ordinal);
        Assert.Contains("<Border Padding=\"18,16,0,0\"", source, StringComparison.Ordinal);
        Assert.Contains("<Grid ColumnDefinitions=\"*\" Margin=\"0,0,18,0\">", source, StringComparison.Ordinal);

        foreach (var tabKey in new[] { "providers.tab.basic", "providers.tab.advanced", "providers.tab.models", "providers.tab.test" })
        {
            Assert.Contains("<ScrollViewer Classes=\"provider-tab-scroll\" VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">", ReadTab(source, tabKey), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyProviderDeleteActionUsesHoverReveal()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");

        Assert.Contains("Selector=\"Border.provider-card Button.provider-delete\"><Setter Property=\"Opacity\" Value=\"0\"/><Setter Property=\"IsHitTestVisible\" Value=\"False\"/>", source, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.provider-card:pointerover Button.provider-delete\"><Setter Property=\"Opacity\" Value=\"1\"/><Setter Property=\"IsHitTestVisible\" Value=\"True\"/>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("header-delete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("model-delete", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"icon-button provider-delete\"", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"RemoveHeaderButton_OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("DeleteModelCommand", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyInputStretchesToTheContainerRightEdge()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var basicTab = ReadTab(source, "providers.tab.basic");

        Assert.Contains("<Grid ColumnDefinitions=\"*,42\" HorizontalAlignment=\"Stretch\" Margin=\"0,0,8,0\">", basicTab, StringComparison.Ordinal);
        Assert.Contains("<TextBox Grid.ColumnSpan=\"2\" HorizontalAlignment=\"Stretch\" Text=\"{Binding SelectedProvider.ApiKey", basicTab, StringComparison.Ordinal);
        Assert.Contains("<Button Grid.Column=\"1\" HorizontalAlignment=\"Center\"", basicTab, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyVisibilityButtonCentersBothEyeIcons()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var buttonStart = source.IndexOf("Click=\"ToggleApiKeyVisibilityButton_OnClick\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "缺少 API Key 可见性按钮");
        var buttonTagStart = source.LastIndexOf("<Button", buttonStart, StringComparison.Ordinal);
        var buttonEnd = source.IndexOf("</Button>", buttonStart, StringComparison.Ordinal);
        var button = source[buttonTagStart..buttonEnd];

        Assert.Contains("HorizontalContentAlignment=\"Center\"", button, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Center\"", button, StringComparison.Ordinal);
        Assert.Contains("<Viewbox Width=\"18\" Height=\"18\" Stretch=\"Uniform\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"><Panel Width=\"24\" Height=\"24\">", button, StringComparison.Ordinal);
        Assert.Equal(2, button.Split("Width=\"24\" Height=\"24\" Stretch=\"None\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TestSendAndStopActionsCenterTheirContent()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");

        Assert.Contains("Selector=\"Button.provider-test-action\"><Setter Property=\"HorizontalContentAlignment\" Value=\"Center\"/><Setter Property=\"VerticalContentAlignment\" Value=\"Center\"/></Style>", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("Classes=\"provider-test-action\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TestModelSelectorUsesFixedEllipsizedTooltipLayout()
    {
        var source = ReadDesktopFile("Views", "ProvidersView.axaml");
        var testTab = ReadTab(source, "providers.tab.test");

        Assert.Contains("<ComboBox Width=\"220\"", testTab, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ModelId}\" TextTrimming=\"CharacterEllipsis\" ToolTip.Tip=\"{Binding ModelId}\"", testTab, StringComparison.Ordinal);
    }

    private static string ReadTab(string source, string headerKey)
    {
        var marker = $"<TabItem Header=\"{{l:Locale {headerKey}}}\">";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"缺少 Tab：{headerKey}");
        var end = source.IndexOf("</TabItem>", start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Tab 未闭合：{headerKey}");
        return source[start..end];
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
