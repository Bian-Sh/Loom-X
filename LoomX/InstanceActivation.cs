using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace LoomX;

internal static class InstanceActivationClient
{
    internal const string ActivateCommand = "activate";
    internal static string PipeName { get; } = OperatingSystem.IsWindows()
        ? $"LoomX.Activate.{Process.GetCurrentProcess().SessionId}"
        : "LoomX.Activate";

    internal static bool TryActivateExistingInstance(
        Func<TimeSpan, bool>? send = null,
        int maxAttempts = 3,
        TimeSpan? retryDelay = null,
        TimeSpan? connectTimeout = null)
    {
        if (maxAttempts <= 0) return false;

        var transport = send ?? TrySendActivation;
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(100);
        var timeout = connectTimeout ?? TimeSpan.FromMilliseconds(250);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (transport(timeout)) return true;
            }
            catch
            {
                // 第二实例只负责尽力通知首实例，通信失败不能阻断其快速退出。
            }

            if (attempt < maxAttempts && delay > TimeSpan.Zero)
                Thread.Sleep(delay);
        }

        return false;
    }

    internal static bool IsActivationCommand(string? command) =>
        string.Equals(command?.Trim(), ActivateCommand, StringComparison.Ordinal);

    private static bool TrySendActivation(TimeSpan timeout)
    {
        using var client = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        client.Connect(Math.Clamp((int)timeout.TotalMilliseconds, 1, int.MaxValue));
        if (OperatingSystem.IsWindows())
            TryAllowServerToSetForegroundWindow(client.SafePipeHandle);
        using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 256, leaveOpen: false)
        {
            NewLine = "\n",
            AutoFlush = true
        };
        writer.WriteLine(ActivateCommand);
        return true;
    }

    private static void TryAllowServerToSetForegroundWindow(SafePipeHandle pipeHandle)
    {
        if (GetNamedPipeServerProcessId(pipeHandle, out var serverProcessId))
            AllowSetForegroundWindow(serverProcessId);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipeHandle, out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}

internal sealed class InstanceActivationServer : IAsyncDisposable
{
    private readonly ILogger<InstanceActivationServer> logger;
    private CancellationTokenSource? cancellation;
    private Task? listener;

    internal InstanceActivationServer(ILogger<InstanceActivationServer> logger)
    {
        this.logger = logger;
    }

    internal void Start(Action onActivate)
    {
        if (listener is not null) return;

        cancellation = new CancellationTokenSource();
        listener = ListenAsync(onActivate, cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (cancellation is null) return;

        cancellation.Cancel();
        if (listener is not null)
        {
            try
            {
                await listener.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        cancellation.Dispose();
        cancellation = null;
        listener = null;
    }

    private async Task ListenAsync(Action onActivate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    InstanceActivationClient.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false, 256, leaveOpen: false);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (InstanceActivationClient.IsActivationCommand(command))
                {
                    onActivate();
                    logger.LogInformation("收到重复启动激活请求 {ProcessId}", Environment.ProcessId);
                }
                else
                {
                    logger.LogWarning("忽略未知的单实例激活命令 {ProcessId}", Environment.ProcessId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "单实例激活监听失败 {ProcessId}", Environment.ProcessId);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
