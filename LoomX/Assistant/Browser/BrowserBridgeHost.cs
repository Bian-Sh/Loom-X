using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant.Browser;

/// <summary>
/// Browser Bridge 的宿主服务：随应用启动监听 127.0.0.1，随应用停止释放。
/// 端口被占用时只记警告，不影响主程序启动。
/// </summary>
public sealed class BrowserBridgeHost : IHostedService
{
    private readonly BrowserBridge bridge;
    private readonly ILogger<BrowserBridgeHost> logger;

    public BrowserBridgeHost(BrowserBridge bridge, ILogger<BrowserBridgeHost> logger)
    {
        this.bridge = bridge;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            bridge.Start();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Browser Bridge 启动失败（端口 {Port} 可能被占用），浏览器能力暂不可用", bridge.Port);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        bridge.Dispose();
        return Task.CompletedTask;
    }
}
