using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LoomX;
using LoomX.Activity;

namespace LoomX.Services;

public enum GatewayState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public sealed class GatewayProcessService : IDisposable
{
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private WebApplication? app;
    private bool appStarted;
    private ActivityStore? activityStore;
    private RequestTelemetryHub? telemetryHub;
    private GatewayStateHub? stateHub;

    public GatewayState State { get; private set; } = GatewayState.Stopped;
    public string? Error { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<ActivityEventInput>? ActivityEnqueued;
    public event EventHandler<RequestTelemetryEvent>? TelemetryPublished;

    public async Task StartAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (await CheckHealthCoreAsync(endpoint, cancellationToken)) return;

            SetState(GatewayState.Starting, null);
            app ??= await LoomXHost.CreateAsync(cancellationToken);
            activityStore ??= app.Services.GetRequiredService<ActivityStore>();
            telemetryHub ??= app.Services.GetRequiredService<RequestTelemetryHub>();
            activityStore.ActivityEnqueued -= OnActivityEnqueued;
            activityStore.ActivityEnqueued += OnActivityEnqueued;
            telemetryHub.Published -= OnTelemetryPublished;
            telemetryHub.Published += OnTelemetryPublished;
            await app.StartAsync(cancellationToken);
            appStarted = true;

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await CheckHealthCoreAsync(endpoint, cancellationToken)) return;
                await Task.Delay(200, cancellationToken);
            }

            throw new InvalidOperationException($"网关已启动，但健康检查未通过：{endpoint}");
        }
        catch (OperationCanceledException)
        {
            SetState(GatewayState.Stopped, "启动已取消。");
            throw;
        }
        catch (Exception exception)
        {
            SetState(GatewayState.Failed, exception.Message);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 仅初始化网关容器中的共享服务，不监听网关端口。
    /// 小助手直连 Provider 时调用此方法，因此不受概览页网关启停状态影响。
    /// </summary>
    public async Task EnsureHostedServicesAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (app is not null) return;
            app = await LoomXHost.CreateAsync(cancellationToken);
            try
            {
                app.Services.GetRequiredService<LoomX.Assistant.AssistantPreferencesStore>().MigrateLegacyIfNeeded();
            }
            catch (Exception exception)
            {
                // 偏好迁移失败不阻止助手使用，正常启动时仍会再次尝试。
                app.Services.GetRequiredService<ILogger<GatewayProcessService>>()
                    .LogWarning(exception, "AI 助手偏好旧版 JSON 迁移检查失败");
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task<bool> CheckHealthAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try { return await CheckHealthCoreAsync(endpoint, cancellationToken); }
        finally { lifecycleLock.Release(); }
    }

    /// <summary>
    /// 解析已初始化的共享容器服务（如小助手 AssistantService）。
    /// 容器可以在网关未监听端口时初始化，因此助手不依赖网关启停状态。
    /// </summary>
    public T? GetHostedService<T>() where T : class => app?.Services.GetService(typeof(T)) as T;

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (app is null)
            {
                SetState(GatewayState.Stopped, null);
                return;
            }

            SetState(GatewayState.Stopping, null);
            if (appStarted) await app.StopAsync(cancellationToken);
            await app.DisposeAsync();
            if (activityStore is not null) activityStore.ActivityEnqueued -= OnActivityEnqueued;
            if (telemetryHub is not null) telemetryHub.Published -= OnTelemetryPublished;
            activityStore = null;
            telemetryHub = null;
            app = null;
            appStarted = false;
            SetState(GatewayState.Stopped, null);
        }
        finally { lifecycleLock.Release(); }
    }

    private async Task<bool> CheckHealthCoreAsync(string endpoint, CancellationToken cancellationToken)
    {
        LastCheckedAt = DateTimeOffset.Now;
        try
        {
            using var response = await httpClient.GetAsync(endpoint.TrimEnd('/') + "/", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetState(GatewayState.Running, null);
                return true;
            }
            SetState(GatewayState.Failed, $"健康检查返回 HTTP {(int)response.StatusCode}。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (app is null) SetState(GatewayState.Stopped, null);
        }
        catch (HttpRequestException)
        {
            if (app is null) SetState(GatewayState.Stopped, null);
        }
        return false;
    }

    private void SetState(GatewayState state, string? error)
    {
        State = state;
        Error = error;
        stateHub ??= app?.Services.GetService<GatewayStateHub>();
        stateHub?.Update(state, error);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (app is not null)
        {
            if (activityStore is not null) activityStore.ActivityEnqueued -= OnActivityEnqueued;
            if (telemetryHub is not null) telemetryHub.Published -= OnTelemetryPublished;
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            activityStore = null;
            telemetryHub = null;
            app = null;
            appStarted = false;
        }
        httpClient.Dispose();
        lifecycleLock.Dispose();
    }

    private void OnActivityEnqueued(object? sender, ActivityEventInput input) => ActivityEnqueued?.Invoke(this, input);
    private void OnTelemetryPublished(object? sender, RequestTelemetryEvent input) => TelemetryPublished?.Invoke(this, input);
}
