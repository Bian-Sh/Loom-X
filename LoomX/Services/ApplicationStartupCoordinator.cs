using LoomX.Configuration;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

internal sealed class ApplicationStartupCoordinator(
    IWindowsStartupService windowsStartupService,
    Func<string, CancellationToken, Task> startGateway,
    ILogger<ApplicationStartupCoordinator> logger)
{
    public async Task<string?> RestoreAsync(
        AppSettingsResponse settings,
        string gatewayEndpoint,
        CancellationToken cancellationToken = default)
    {
        string? startupRegistrationError = null;
        try
        {
            windowsStartupService.Apply(settings.StartWithWindows);
        }
        catch (Exception exception)
        {
            startupRegistrationError = exception.Message;
            logger.LogError(exception, "APP 启动时同步 Windows 开机自启动失败 {Enabled}", settings.StartWithWindows);
        }

        if (!settings.GatewayRunning) return startupRegistrationError;

        try
        {
            await startGateway(gatewayEndpoint, cancellationToken);
            logger.LogInformation("APP 启动时恢复网关运行意图完成 {GatewayEndpoint}", gatewayEndpoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "APP 启动时恢复网关运行意图失败 {GatewayEndpoint}", gatewayEndpoint);
        }

        return startupRegistrationError;
    }
}
