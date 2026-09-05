using Avalonia.Controls;
using Avalonia;
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
        RuntimeGraph.SizeChanged += OnGraphSizeChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args) => RuntimeGraph.FitToView();

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args) => RuntimeGraph.FitToView();

    private void OnGraphSizeChanged(object? sender, SizeChangedEventArgs args) => RuntimeGraph.FitToView(args.NewSize);

    private void ZoomOut_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RuntimeGraph.ZoomOut();

    private void ZoomIn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RuntimeGraph.ZoomIn();

    private void FitGraph_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RuntimeGraph.FitToView();

    private void FocusEndpoint_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverviewEndpointViewModel endpoint })
            RuntimeGraph.FocusEndpoint(endpoint.Key);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        (DataContext as OverviewViewModel)?.Dispose();
    }
}
