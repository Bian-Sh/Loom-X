using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OllamaHub.Desktop.Services;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop;

public partial class App : Application
{
    private GatewayProcessService? gatewayService;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            gatewayService = new GatewayProcessService();
            var toastService = new ToastService();
            desktop.MainWindow = new MainWindow(toastService) { DataContext = new MainWindowViewModel(gatewayService, toastService) };
            desktop.Exit += async (_, _) => await gatewayService.StopAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
