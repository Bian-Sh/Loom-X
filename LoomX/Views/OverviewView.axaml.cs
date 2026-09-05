using Avalonia.Controls;
using Avalonia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LoomX.ViewModels;
namespace LoomX.Views;
public partial class OverviewView : UserControl
{
    private readonly IOverviewGraphHost graphHost;

    public OverviewView()
    {
        InitializeComponent();
        var logger = (Application.Current as App)?.LoggerFactory?.CreateLogger<OverviewGraphHost>()
            ?? NullLogger<OverviewGraphHost>.Instance;
        graphHost = new OverviewGraphHost(GraphWebView, logger);
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
