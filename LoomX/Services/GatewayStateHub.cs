namespace LoomX.Services;

/// <summary>
/// 网关运行状态的共享观测点：由 LoomXHost 容器注册，桌面端 GatewayProcessService 在状态变化时同步写入，
/// 供 loomx.get_status 等助手工具读取网关启停状态（配置层之外的运行时状态）。
/// 容器可在网关未监听端口时初始化（EnsureHostedServicesAsync），因此助手不依赖网关启停即可读取该状态。
/// </summary>
public sealed class GatewayStateHub
{
    public GatewayState State { get; private set; } = GatewayState.Stopped;

    /// <summary>状态为 Failed 时的安全摘要（异常消息，不含 API Key 等敏感信息）。</summary>
    public string? Error { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Update(GatewayState state, string? error)
    {
        State = state;
        Error = error;
        UpdatedAt = DateTimeOffset.Now;
    }
}
