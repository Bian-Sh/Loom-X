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
public sealed class BrowserBridge : IBrowserBridge, IDisposable
{
    private readonly ILogger<BrowserBridge> logger;
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<string, BrowserTargetInfo> targets = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();

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

    public bool IsExtensionConnected => extensionReady && socket is { State: WebSocketState.Open };

    public IReadOnlyCollection<BrowserTargetInfo> Targets => targets.Values.ToArray();

    public event Action<BrowserBridgeEvent>? BridgeEvent;

    public void Start()
    {
        listener.Start();
        listenTask = Task.Run(AcceptLoopAsync);
        logger.LogInformation("Browser Bridge 已启动 127.0.0.1:{Port}", Port);
    }

    public async Task<JsonNode> SendCommandAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        if (!IsExtensionConnected)
        {
            throw new BrowserBridgeException("browser_bridge_offline", "Chrome Extension 未连接 Browser Bridge。");
        }

        // WebSocket 全双工但本协议一次只允许一个未完成命令，简化响应匹配
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref nextCommandId);
            var payload = BrowserProtocol.Command(id, method, parameters).ToJsonString();
            pendingResponse = null;
            pendingError = null;

            await socket!.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
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

    private async Task AcceptLoopAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                break;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            // 只接受本地回环连接
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                continue;
            }

            try
            {
                var accepted = await context.AcceptWebSocketAsync(null);
                await AttachSocketAsync(accepted.WebSocket);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Browser Bridge 接受 WebSocket 连接失败");
            }
        }
    }

    private async Task AttachSocketAsync(WebSocket webSocket)
    {
        // 新连接顶替旧连接（Extension 重连/刷新场景）
        if (socket is not null)
        {
            try
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                // 只发出关闭帧，不等对端确认（对端可能已不再读取），随后强制释放
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "replaced", closeTimeout.Token);
            }
            catch { /* 旧连接已断开 */ }

            try { socket.Abort(); } catch { /* 忽略 */ }
        }

        socket = webSocket;
        extensionReady = false;
        targets.Clear();
        receiveTask = Task.Run(() => ReceiveLoopAsync(webSocket));
    }

    private async Task ReceiveLoopAsync(WebSocket webSocket)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (webSocket.State == WebSocketState.Open && !shutdown.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult segment;
                do
                {
                    segment = await webSocket.ReceiveAsync(buffer, shutdown.Token);
                    if (segment.MessageType == WebSocketMessageType.Close)
                    {
                        // 关闭握手是双向的：必须回 Close 帧，否则对端 CloseAsync 永远等待
                        try
                        {
                            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token);
                        }
                        catch { /* 对端可能已离开 */ }

                        throw new WebSocketException("closed");
                    }

                    stream.Write(buffer, 0, segment.Count);
                }
                while (!segment.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
            // 连接断开，走下面的清理逻辑
        }

        if (ReferenceEquals(socket, webSocket))
        {
            extensionReady = false;
            foreach (var target in targets.Values)
            {
                Raise(BrowserBridgeEvent.TargetClosed, target, "extension_disconnected");
            }

            targets.Clear();
            Raise(BrowserBridgeEvent.ExtensionDisconnected, null, null);
            pendingError ??= "Extension 连接已断开。";
            logger.LogInformation("Browser Bridge Extension 已断开");
        }
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

            extensionReady = true;
            Raise(BrowserBridgeEvent.ExtensionConnected, null, message["params"]?["extension"]?.GetValue<string>());
            logger.LogInformation("Browser Bridge Extension 已连接");
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

    private void HandleExtensionEvent(JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        switch (name)
        {
            case "targetCreated":
            {
                var target = new BrowserTargetInfo(
                    parameters!["targetId"]!.GetValue<string>(),
                    parameters["sessionId"]!.GetValue<string>(),
                    parameters["tabId"]!.GetValue<int>(),
                    parameters["url"]?.GetValue<string>() ?? string.Empty,
                    parameters["title"]?.GetValue<string>() ?? string.Empty);
                targets[target.TargetId] = target;
                Raise(BrowserBridgeEvent.TargetCreated, target, null);
                break;
            }
            case "targetClosed":
            {
                var targetId = parameters!["targetId"]!.GetValue<string>();
                if (targets.TryRemove(targetId, out var target))
                {
                    Raise(BrowserBridgeEvent.TargetClosed, target, null);
                }

                break;
            }
        }
    }

    private void Raise(string kind, BrowserTargetInfo? target, string? detail) =>
        BridgeEvent?.Invoke(new BrowserBridgeEvent(kind, target, detail));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shutdown.Cancel();
        try { socket?.Abort(); } catch { /* 忽略 */ }
        try { listener.Stop(); } catch { /* 忽略 */ }
        try { receiveTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略 */ }
        try { listenTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略 */ }
        shutdown.Dispose();
        sendLock.Dispose();
    }
}

/// <summary>Browser Bridge 错误。Code/Message 只含安全摘要。</summary>
public sealed class BrowserBridgeException : Exception
{
    public BrowserBridgeException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
