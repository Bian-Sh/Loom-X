using Avalonia.Controls;
using Avalonia;
using OllamaHub.Desktop.ViewModels;
namespace OllamaHub.Desktop.Views;
public partial class OverviewView : UserControl
{
    private readonly IOverviewGraphHost graphHost;

    public OverviewView()
    {
        InitializeComponent();
        graphHost = new OverviewGraphHost(GraphWebView);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (DataContext is OverviewViewModel viewModel) graphHost.Attach(viewModel);
        else graphHost.Detach();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args) => graphHost.Initialize();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        graphHost.Detach();
        (DataContext as OverviewViewModel)?.Dispose();
    }
}
