using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant.Browser;

/// <summary>
/// Chrome Extension 侧的 Browser Bridge 抽象，便于测试替换。
/// </summary>
public interface IBrowserBridge
{
    bool IsExtensionConnected { get; }

    IReadOnlyCollection<BrowserTargetInfo> Targets { get; }

    event Action<BrowserBridgeEvent>? BridgeEvent;

    Task<JsonNode> SendCommandAsync(string method, JsonNode? parameters, CancellationToken cancellationToken);

    Task<JsonNode> SendSessionCommandAsync(string sessionId, string method, JsonNode? parameters, CancellationToken cancellationToken);
}

/// <summary>
/// 本地 Browser Bridge：监听 127.0.0.1 的 WebSocket，与 Chrome MV3 Extension 维持
/// CDP-like 会话（targetId/sessionId/tabId 映射）。只允许 Extension 创建/登记的
/// automation tab，绝不读取用户其他 Chrome 标签页。
/// </summary>
public sealed class BrowserBridge : IBrowserBridge, IBrowserBridgeLifecycle, IDisposable, IAsyncDisposable
{
    private readonly ILogger<BrowserBridge> logger;
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<string, BrowserTargetInfo> targets = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly object connectionSync = new();

    private CancellationTokenSource? lifecycleCancellation;
    private WebSocket? socket;
    private long nextCommandId;
    private JsonObject? pendingResponse;
    private string? pendingError;
    private Task? listenTask;
    private Task? receiveTask;
    private bool extensionReady;
    private bool disposed;

    public BrowserBridge(int port, ILogger<BrowserBridge> logger)
    {
        this.logger = logger;
        Port = port;
        listener.Prefixes.Add($"http://127.0.0.1:{port}/loomx-browser/");
    }

    public int Port { get; }

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsListening => listener.IsListening;

    public bool IsExtensionConnected => extensionReady && socket is { State: WebSocketState.Open };

    public IReadOnlyCollection<BrowserTargetInfo> Targets => targets.Values.ToArray();

    public event Action<BrowserBridgeEvent>? BridgeEvent;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listener.IsListening) return;

            try
            {
                listener.Start();
            }
            catch (HttpListenerException exception)
            {
                throw new BrowserBridgeException("bridge_port_unavailable", $"Browser Bridge 端口 {Port} 不可用。", exception);
            }

            lifecycleCancellation?.Dispose();
            lifecycleCancellation = new CancellationTokenSource();
            listenTask = Task.Run(() => AcceptLoopAsync(lifecycleCancellation.Token), CancellationToken.None);
            logger.LogInformation("Browser Bridge 已启动 127.0.0.1:{Port}", Port);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!listener.IsListening && lifecycleCancellation is null) return;

            var cancellation = lifecycleCancellation;
            var currentListenTask = listenTask;
            var currentReceiveTask = receiveTask;
            WebSocket? currentSocket;
            lock (connectionSync)
            {
                currentSocket = socket;
                socket = null;
                extensionReady = false;
            }

            cancellation?.Cancel();
            try { listener.Stop(); } catch (ObjectDisposedException) { }

            if (currentSocket is not null)
            {
                try
                {
                    using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    closeTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    if (currentSocket.State == WebSocketState.Open)
                    {
                        await currentSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bridge_stopped", closeTimeout.Token);
                    }
                }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // 对端可能已离开，继续强制释放。
                }

                try { currentSocket.Abort(); } catch { }
                currentSocket.Dispose();
            }

            await AwaitQuietlyAsync(currentReceiveTask);
            await AwaitQuietlyAsync(currentListenTask);

            foreach (var target in targets.Values)
            {
                Raise(BrowserBridgeEvent.TargetClosed, target, "bridge_stopped");
            }

            targets.Clear();
            pendingResponse = null;
            pendingError = "Browser Bridge 已停止。";
            receiveTask = null;
            listenTask = null;
            lifecycleCancellation = null;
            cancellation?.Dispose();
            logger.LogInformation("Browser Bridge 已停止 127.0.0.1:{Port}", Port);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task<JsonNode> SendCommandAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        if (!IsExtensionConnected)
        {
            throw new BrowserBridgeException("browser_bridge_offline", "Chrome Extension 未连接 Browser Bridge。");
        }

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref nextCommandId);
            var payload = BrowserProtocol.Command(id, method, parameters).ToJsonString();
            pendingResponse = null;
            pendingError = null;

            WebSocket activeSocket;
            CancellationToken lifecycleToken;
            lock (connectionSync)
            {
                activeSocket = socket ?? throw new BrowserBridgeException("browser_bridge_offline", "Chrome Extension 未连接 Browser Bridge。");
                lifecycleToken = lifecycleCancellation?.Token ?? CancellationToken.None;
            }

            await activeSocket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifecycleToken);
            timeout.CancelAfter(CommandTimeout);
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (pendingError is not null)
                {
                    throw new BrowserBridgeException("extension_error", pendingError);
                }

                if (pendingResponse is not null)
                {
                    return (JsonNode?)pendingResponse["result"] ?? new JsonObject();
                }

                await Task.Delay(20, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrowserBridgeException("command_timeout", $"浏览器命令超时：{method}");
        }
        finally
        {
            sendLock.Release();
        }
    }

    public Task<JsonNode> SendSessionCommandAsync(string sessionId, string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        if (!targets.Values.Any(target => target.SessionId == sessionId))
        {
            throw new BrowserBridgeException("unknown_session", $"未知的浏览器会话：{sessionId}");
        }

        return SendCommandAsync(BrowserProtocol.SessionSendCommand, new JsonObject
        {
            ["sessionId"] = sessionId,
            ["method"] = method,
            ["params"] = parameters ?? new JsonObject(),
        }, cancellationToken);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                break;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                continue;
            }

            try
            {
                var accepted = await context.AcceptWebSocketAsync(null);
                await AttachSocketAsync(accepted.WebSocket, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Browser Bridge 接受 WebSocket 连接失败");
            }
        }
    }

    private async Task AttachSocketAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        WebSocket? previous;
        lock (connectionSync)
        {
            previous = socket;
            socket = webSocket;
            extensionReady = false;
        }

        if (previous is not null)
        {
            try
            {
                using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                closeTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                await previous.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "replaced", closeTimeout.Token);
            }
            catch { }

            try { previous.Abort(); } catch { }
            previous.Dispose();
        }

        targets.Clear();
        receiveTask = Task.Run(() => ReceiveLoopAsync(webSocket, cancellationToken), CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult segment;
                do
                {
                    segment = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (segment.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token);
                        }
                        catch { }

                        throw new WebSocketException("closed");
                    }

                    stream.Write(buffer, 0, segment.Count);
                }
                while (!segment.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            // 连接断开，走下面的清理逻辑。
        }

        lock (connectionSync)
        {
            if (!ReferenceEquals(socket, webSocket)) return;
            socket = null;
            extensionReady = false;
        }

        foreach (var target in targets.Values)
        {
            Raise(BrowserBridgeEvent.TargetClosed, target, "extension_disconnected");
        }

        targets.Clear();
        Raise(BrowserBridgeEvent.ExtensionDisconnected, null, null);
        pendingError ??= "Extension 连接已断开。";
        logger.LogInformation("Browser Bridge Extension 已断开");
    }

    private void HandleMessage(string json)
    {
        if (!BrowserProtocol.TryParse(json, out var message) || message is null)
        {
            return;
        }

        if (BrowserProtocol.IsHello(message))
        {
            var version = message["params"]?["protocol"]?.GetValue<int>() ?? 0;
            if (version != BrowserProtocol.ProtocolVersion)
            {
                logger.LogWarning("Browser Bridge 协议版本不匹配 {Version}", version);
                return;
            }

            RestoreTargetSnapshot(message["params"]?["targets"] as JsonArray);
            extensionReady = true;
            Raise(BrowserBridgeEvent.ExtensionConnected, null, message["params"]?["extension"]?.GetValue<string>());
            logger.LogInformation("Browser Bridge Extension 已连接 {TargetCount}", targets.Count);
            return;
        }

        if (BrowserProtocol.IsResponse(message))
        {
            if (message["error"] is JsonObject error)
            {
                pendingError = error["message"]?.GetValue<string>() ?? "Extension 返回未知错误。";
            }
            else
            {
                pendingResponse = message;
            }

            return;
        }

        if (BrowserProtocol.IsEvent(message))
        {
            HandleExtensionEvent(message["params"] as JsonObject);
        }
    }

    private void RestoreTargetSnapshot(JsonArray? snapshot)
    {
        targets.Clear();
        if (snapshot is null) return;

        foreach (var node in snapshot.OfType<JsonObject>())
        {
            if (!TryCreateTarget(node, out var target)) continue;
            targets[target.TargetId] = target;
            Raise(BrowserBridgeEvent.TargetCreated, target, "hello_snapshot");
        }
    }

    private void HandleExtensionEvent(JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        switch (name)
        {
            case "targetCreated" when TryCreateTarget(parameters!, out var target):
                targets[target.TargetId] = target;
                Raise(BrowserBridgeEvent.TargetCreated, target, null);
                break;
            case "targetClosed":
            {
                var targetId = parameters!["targetId"]!.GetValue<string>();
                if (targets.TryRemove(targetId, out var closedTarget))
                {
                    Raise(BrowserBridgeEvent.TargetClosed, closedTarget, null);
                }

                break;
            }
        }
    }

    private static bool TryCreateTarget(JsonObject parameters, out BrowserTargetInfo target)
    {
        target = default!;
        if (parameters["targetId"] is not JsonValue targetIdValue
            || parameters["sessionId"] is not JsonValue sessionIdValue
            || parameters["tabId"] is not JsonValue tabIdValue
            || !targetIdValue.TryGetValue<string>(out var targetId)
            || !sessionIdValue.TryGetValue<string>(out var sessionId)
            || !tabIdValue.TryGetValue<int>(out var tabId)
            || string.IsNullOrWhiteSpace(targetId)
            || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        target = new BrowserTargetInfo(
            targetId,
            sessionId,
            tabId,
            parameters["url"]?.GetValue<string>() ?? string.Empty,
            parameters["title"]?.GetValue<string>() ?? string.Empty);
        return true;
    }

    private void Raise(string kind, BrowserTargetInfo? target, string? detail) =>
        BridgeEvent?.Invoke(new BrowserBridgeEvent(kind, target, detail));

    private static async Task AwaitQuietlyAsync(Task? task)
    {
        if (task is null) return;
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or WebSocketException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync();
        disposed = true;
        listener.Close();
        lifecycleLock.Dispose();
        sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Browser Bridge 错误。Code/Message 只含安全摘要。</summary>
public sealed class BrowserBridgeException : Exception
{
    public BrowserBridgeException(string code, string message, Exception? innerException = null) : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
