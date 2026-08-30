using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using OllamaHub.Logging;
using OllamaHub.Desktop.Services;
using OllamaHub.Desktop.ViewModels;
using Serilog;

namespace OllamaHub.Desktop;

public partial class App : Application
{
    private GatewayProcessService? gatewayService;
    private ILoggerFactory? loggerFactory;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LoggingBootstrap.Configure();
            var initialSettings = new ConfigSnapshotService().Load();
            LoggingBootstrap.SetIncludeStackTrace(initialSettings.Settings.LogStackTrace);
            loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));
            gatewayService = new GatewayProcessService();
            var toastService = new ToastService();
            desktop.MainWindow = new MainWindow(toastService) { DataContext = new MainWindowViewModel(gatewayService, toastService, loggerFactory) };
            desktop.Exit += async (_, _) =>
            {
                await gatewayService.StopAsync();
                loggerFactory?.Dispose();
                Log.CloseAndFlush();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
