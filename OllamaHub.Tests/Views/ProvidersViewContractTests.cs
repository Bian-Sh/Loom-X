using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class ProvidersViewContractTests
{
    [Fact]
    public void ModelListBindsSelectedModelForAutomaticToggleSave()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("SelectedItem=\"{Binding SelectedModel, Mode=TwoWay}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelChangesSelectTheChangedModelBeforeAutomaticSave()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("if (sender is ModelEditorViewModel model && !ReferenceEquals(SelectedModel, model))", source, StringComparison.Ordinal);
        Assert.Contains("SelectedModel = model;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelRowsDoNotReserveSpaceForDragHandle()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Equal(2, source.Split("ColumnDefinitions=\"1.5*,*,80,100,56\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("M 6,8 L 11,8 M 6,16 L 11,16", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteHeadersShowPersistentWarning()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("SelectedProvider.HasIncompleteHeaders", source, StringComparison.Ordinal);
        Assert.Contains("SelectedProvider.IncompleteHeaderCount", source, StringComparison.Ordinal);
        Assert.Contains("补全名称和值后才会保存", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSyncButtonUsesCompactRotatingIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("Classes=\"icon-glyph\" Data=\"M 26,12 A 10,10 0 1,0 23,20", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"12\" Height=\"12\" RenderTransformOrigin=\"50%,50%\"", source, StringComparison.Ordinal);
        Assert.Contains("<RotateTransform Angle=\"{Binding SyncIconAngle}\"/>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSyncReportsToastAndStopsAnimationForCurrentRequest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("toastService.Show(Status, ToastLevel.Success);", source, StringComparison.Ordinal);
        Assert.Contains("toastService.Show(Status, ToastLevel.Error);", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("StopModelSyncAnimation();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseUrlHelpControlUsesAdjacentQuestionMarkAndHelpCursor()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "ProvidersView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("<StackPanel Orientation=\"Horizontal\" Spacing=\"4\"><TextBlock Text=\"Base URL\"", source, StringComparison.Ordinal);
        Assert.Contains("Cursor=\"Help\"", source, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"?\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Data=\"M 10,10 A 5,5", source, StringComparison.Ordinal);
    }
}
