using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// 统一编排桌面端启动阶段。UI 只观察 <see cref="Progress"/>，不再自行猜测启动状态。
/// </summary>
public sealed class StartupOrchestrator
{
    private readonly AppDataStore dataStore;
    private readonly GatewayProcessService gatewayService;
    private readonly IWindowsStartupService? windowsStartupService;
    private readonly ILogger<StartupOrchestrator> logger;

    public StartupOrchestrator(
        AppDataStore dataStore,
        GatewayProcessService gatewayService,
        StartupProgress? progress = null,
        IWindowsStartupService? windowsStartupService = null,
        ILogger<StartupOrchestrator>? logger = null)
    {
        this.dataStore = dataStore;
        this.gatewayService = gatewayService;
        this.windowsStartupService = windowsStartupService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupOrchestrator>.Instance;
        Progress = progress ?? new StartupProgress();
    }

    public StartupProgress Progress { get; }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Progress.AdvanceTo(StartupPhase.Database);
            await dataStore.InitializeAsync(cancellationToken);
            logger.LogInformation("桌面启动阶段完成 {Phase}", StartupPhase.Database);

            Progress.AdvanceTo(StartupPhase.PluginCompile);
            await gatewayService.EnsureHostedServicesAsync(
                cancellationToken,
                phase =>
                {
                    if (phase == PluginRuntimeStartupPhase.Loading)
                        Progress.AdvanceTo(StartupPhase.PluginLoad);
                });
            logger.LogInformation("桌面启动阶段完成 {Phase}", StartupPhase.PluginCompile);

            if (Progress.CurrentPhase == StartupPhase.PluginCompile)
                Progress.AdvanceTo(StartupPhase.PluginLoad);
            logger.LogInformation("桌面启动阶段完成 {Phase}", StartupPhase.PluginLoad);

            Progress.AdvanceTo(StartupPhase.Gateway);
            await RestoreStartupIntentAsync(cancellationToken);
            logger.LogInformation("桌面启动阶段完成 {Phase}", StartupPhase.Gateway);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Progress.Fail("启动已取消。");
            logger.LogWarning("桌面启动已取消");
            throw;
        }
        catch (Exception exception)
        {
            Progress.Fail(exception.Message);
            logger.LogError(exception, "桌面启动编排失败 {Phase}", Progress.CurrentPhase);
            throw;
        }
    }

    private async Task RestoreStartupIntentAsync(CancellationToken cancellationToken)
    {
        if (windowsStartupService is null || dataStore.Settings is not { } settings)
            return;

        var gatewayEndpoint = dataStore.CurrentConfig.Server.Urls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(gatewayEndpoint))
            gatewayEndpoint = "http://127.0.0.1:11434";

        var coordinator = new ApplicationStartupCoordinator(
            windowsStartupService,
            gatewayService.StartAsync,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplicationStartupCoordinator>.Instance);
        await coordinator.RestoreAsync(settings, gatewayEndpoint, cancellationToken);
    }
}
