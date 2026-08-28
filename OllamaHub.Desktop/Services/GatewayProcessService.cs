using System.Diagnostics;
using System.Net.Http;

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
    private Process? process;

    public GatewayState State { get; private set; } = GatewayState.Stopped;
    public string? Error { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public event EventHandler? StateChanged;

    public async Task StartAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (await CheckHealthCoreAsync(endpoint, cancellationToken)) return;
            if (process is { HasExited: false }) return;

            SetState(GatewayState.Starting, null);
            var gatewayDll = Path.Combine(AppContext.BaseDirectory, "OllamaHub.dll");
            if (!File.Exists(gatewayDll)) throw new FileNotFoundException("未找到 OllamaHub 网关程序集，请先构建网关项目。", gatewayDll);

            process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{gatewayDll}\"",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) throw new InvalidOperationException("网关进程启动失败。");
            process.EnableRaisingEvents = true;
            process.Exited += ProcessExited;

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
            if (process is null || process.HasExited)
            {
                process?.Dispose();
                process = null;
                SetState(GatewayState.Stopped, null);
                return;
            }

            SetState(GatewayState.Stopping, null);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
            process.Dispose();
            process = null;
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
            if (process is null or { HasExited: true }) SetState(GatewayState.Stopped, null);
        }
        catch (HttpRequestException)
        {
            if (process is null or { HasExited: true }) SetState(GatewayState.Stopped, null);
        }
        return false;
    }

    private void ProcessExited(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, process)) SetState(GatewayState.Failed, "网关进程已退出。");
    }

    private void SetState(GatewayState state, string? error)
    {
        State = state;
        Error = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        process?.Dispose();
        httpClient.Dispose();
        lifecycleLock.Dispose();
    }
}
