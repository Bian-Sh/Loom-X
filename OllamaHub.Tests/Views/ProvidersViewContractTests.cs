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
}
