using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OllamaHub;
using OllamaHub.Logging;
using OllamaHub.Desktop.Services;
using OllamaHub.Desktop.ViewModels;
using Serilog;

namespace OllamaHub.Desktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\OllamaHub.Desktop";
    private const string ShellBootstrapMutexName = @"Local\OllamaHub.Desktop.ShellBootstrap";
    private const string SelfLaunchArgument = "--ollamahub-child";
    private GatewayProcessService? gatewayService;
    private AppDataStore? dataStore;
    private ILoggerFactory? loggerFactory;
    private Mutex? singleInstanceMutex;
    private Mutex? shellBootstrapMutex;
    private bool ownsShellBootstrapMutex;
    private bool allowMultipleInstances;
    public ILoggerFactory? LoggerFactory => loggerFactory;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            allowMultipleInstances = InstanceLaunchPolicy.AllowsMultipleInstances(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable("OLLAMAHUB_ALLOW_MULTIPLE_INSTANCES"));
            var launchedByOllamaHub = Environment.GetCommandLineArgs()
                .Skip(1)
                .Any(argument => string.Equals(argument, SelfLaunchArgument, StringComparison.OrdinalIgnoreCase));
            if (!allowMultipleInstances && !launchedByOllamaHub)
            {
                shellBootstrapMutex = new Mutex(true, ShellBootstrapMutexName, out var isShellBootstrapOwner);
                ownsShellBootstrapMutex = isShellBootstrapOwner;
                if (isShellBootstrapOwner)
                {
                    var processPath = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(processPath))
                    {
                        var shellStartInfo = new ProcessStartInfo
                        {
                            FileName = processPath,
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(processPath)
                        };
                        shellStartInfo.ArgumentList.Add(SelfLaunchArgument);
                        try
                        {
                            Process.Start(shellStartInfo);
                            desktop.Shutdown(0);
                            base.OnFrameworkInitializationCompleted();
                            return;
                        }
                        catch (Exception exception)
                        {
                            LoggingBootstrap.Configure();
                            loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));
                            loggerFactory.CreateLogger<App>().LogWarning(exception, "桌面应用自启动子进程失败，继续当前进程 {ProcessId}", Environment.ProcessId);
                        }
                    }
                    shellBootstrapMutex.Dispose();
                    shellBootstrapMutex = null;
                    ownsShellBootstrapMutex = false;
                }
            }

            if (!allowMultipleInstances)
            {
                singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
                if (!isFirstInstance)
                {
                    singleInstanceMutex.Dispose();
                    singleInstanceMutex = null;
                    LoggingBootstrap.Configure();
                    loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));
                    loggerFactory.CreateLogger<App>().LogWarning("检测到已有 OllamaHub 桌面实例，当前进程退出以避免并发读取配置库，进程 {ProcessId}", Environment.ProcessId);
                    desktop.Shutdown(0);
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
            }
            var launcherWorkingDirectory = Environment.CurrentDirectory;
            var applicationDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            Environment.CurrentDirectory = applicationDirectory;
            LoggingBootstrap.Configure();
            loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));
            var startupLogger = loggerFactory.CreateLogger<App>();
            if (allowMultipleInstances)
                startupLogger.LogWarning("调试启动已允许多个桌面实例，进程 {ProcessId}", Environment.ProcessId);
            startupLogger.LogInformation("桌面应用启动，进程 {ProcessId}，用户 {UserName}，进程路径 {ProcessPath}，基目录 {BaseDirectory}，启动工作目录 {LauncherWorkingDirectory}，规范化工作目录 {CurrentDirectory}", Environment.ProcessId, Environment.UserName, Environment.ProcessPath, AppContext.BaseDirectory, launcherWorkingDirectory, Environment.CurrentDirectory);
            var configService = new ConfigSnapshotService(loggerFactory.CreateLogger<ConfigSnapshotService>());
            gatewayService = new GatewayProcessService();
            var toastService = new ToastService();
            dataStore = new AppDataStore(configService, gatewayService, loggerFactory.CreateLogger<AppDataStore>());
            var mainWindow = new MainWindow(toastService, loggerFactory.CreateLogger<MainWindow>());
            mainWindow.DataContext = new MainWindowViewModel(gatewayService, toastService, loggerFactory, configService, mainWindow.ApplyAppearance, dataStore);
            desktop.MainWindow = mainWindow;
            desktop.Exit += async (_, _) =>
            {
                await gatewayService.StopAsync();
                if (mainWindow.DataContext is MainWindowViewModel viewModel) viewModel.Dispose();
                dataStore?.Dispose();
                dataStore = null;
                configService.Dispose();
                loggerFactory?.Dispose();
                singleInstanceMutex?.ReleaseMutex();
                singleInstanceMutex?.Dispose();
                singleInstanceMutex = null;
                if (ownsShellBootstrapMutex) shellBootstrapMutex?.ReleaseMutex();
                shellBootstrapMutex?.Dispose();
                shellBootstrapMutex = null;
                ownsShellBootstrapMutex = false;
                Log.CloseAndFlush();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
