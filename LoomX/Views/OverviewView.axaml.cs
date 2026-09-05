using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoomX.NodeGraph;
using LoomX.ViewModels;
namespace LoomX.Views;
public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs args) => FitActiveGraph();

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args) => FitActiveGraph();

    private void OnGraphSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (sender is RuntimeGraphControl graph) graph.FitToView(args.NewSize);
    }

    private void ZoomOut_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FindActiveGraph()?.ZoomOut();

    private void ZoomIn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FindActiveGraph()?.ZoomIn();

    private void FitGraph_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FindActiveGraph()?.FitToView();

    private void FocusEndpoint_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: OverviewEndpointViewModel endpoint }) return;
        if (DataContext is OverviewViewModel viewModel) viewModel.SelectEndpoint(endpoint);
        Dispatcher.UIThread.Post(() => FindActiveGraph()?.FitToView());
    }

    private void FitActiveGraph() => Dispatcher.UIThread.Post(() => FindActiveGraph()?.FitToView());

    private RuntimeGraphControl? FindActiveGraph() => FindVisualDescendant<RuntimeGraphControl>(this, graph => graph.IsVisible);

    private static T? FindVisualDescendant<T>(Visual root, Func<T, bool> predicate)
        where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T match && predicate(match)) return match;
            if (FindVisualDescendant(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        (DataContext as OverviewViewModel)?.Dispose();
    }
}
