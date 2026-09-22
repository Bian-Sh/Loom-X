using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class PluginsView : UserControl
{
    private PluginsViewModel? observedModel;

    public PluginsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs args) =>
        observedModel = DataContext as PluginsViewModel;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        observedModel ??= DataContext as PluginsViewModel;
        if (observedModel is { PluginCount: 0, IsLoading: false })
            _ = observedModel.RefreshAsync();
    }
}
