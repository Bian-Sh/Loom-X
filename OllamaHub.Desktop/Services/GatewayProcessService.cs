using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OllamaHub;
using OllamaHub.Activity;

namespace OllamaHub.Desktop.Services;

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
    private ActivityStore? activityStore;
    private RequestTelemetryHub? telemetryHub;

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
            if (app is not null) return;

            SetState(GatewayState.Starting, null);
            app = await OllamaHubHost.CreateAsync(cancellationToken);
            activityStore = app.Services.GetRequiredService<ActivityStore>();
            telemetryHub = app.Services.GetRequiredService<RequestTelemetryHub>();
            activityStore.ActivityEnqueued += OnActivityEnqueued;
            telemetryHub.Published += OnTelemetryPublished;
            await app.StartAsync(cancellationToken);

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

    public async Task<bool> CheckHealthAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try { return await CheckHealthCoreAsync(endpoint, cancellationToken); }
        finally { lifecycleLock.Release(); }
    }

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
            await app.StopAsync(cancellationToken);
            await app.DisposeAsync();
            if (activityStore is not null) activityStore.ActivityEnqueued -= OnActivityEnqueued;
            if (telemetryHub is not null) telemetryHub.Published -= OnTelemetryPublished;
            activityStore = null;
            telemetryHub = null;
            app = null;
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
        }
        httpClient.Dispose();
        lifecycleLock.Dispose();
    }

    private void OnActivityEnqueued(object? sender, ActivityEventInput input) => ActivityEnqueued?.Invoke(this, input);
    private void OnTelemetryPublished(object? sender, RequestTelemetryEvent input) => TelemetryPublished?.Invoke(this, input);
}
