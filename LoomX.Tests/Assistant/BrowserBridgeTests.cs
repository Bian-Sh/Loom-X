using Xunit;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Assistant.Browser;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

/// <summary>
/// Browser Bridge 回环测试：真实 WebSocket 客户端模拟 Chrome Extension。
/// </summary>
public sealed class BrowserBridgeTests : IAsyncLifetime
{
    private readonly int port = 24000 + Random.Shared.Next(2000);
    private BrowserBridge bridge = null!;
    private ClientWebSocket extension = null!;

    public async Task InitializeAsync()
    {
        bridge = new BrowserBridge(port, NullLogger<BrowserBridge>.Instance);
        bridge.Start();
        extension = new ClientWebSocket();
        using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await extension.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/loomx-browser/"), connectTimeout.Token);
        }
        await SendAsync(new JsonObject
        {
            ["method"] = "Bridge.hello",
            ["params"] = new JsonObject { ["protocol"] = 1, ["extension"] = "test" },
        });
        // 给 Bridge 一点时间处理 hello
        await WaitForAsync(() => bridge.IsExtensionConnected);
    }

    public async Task DisposeAsync()
    {
        try
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await extension.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeTimeout.Token);
        }
        catch { /* 已关闭 */ }

        extension.Dispose();
        bridge.Dispose();
    }

    [Fact]
    public async Task Hello_RegistersExtensionConnection()
    {
        Assert.True(bridge.IsExtensionConnected);
    }

    [Fact]
    public async Task Hello_WrongProtocolVersion_NotConnected()
    {
        using var other = new ClientWebSocket();
        await other.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/loomx-browser/"), CancellationToken.None);
        await SendAsync(new JsonObject
        {
            ["method"] = "Bridge.hello",
            ["params"] = new JsonObject { ["protocol"] = 99 },
        }, other);

        await Task.Delay(300);
        // 旧连接已被顶掉，新连接版本不符 → 离线
        Assert.False(bridge.IsExtensionConnected);
    }

    [Fact]
    public async Task Command_ResponseRoundTrip()
    {
        var responder = Task.Run(async () =>
        {
            var command = await ReceiveAsync();
            Assert.Equal("Target.createTarget", command["method"]!.GetValue<string>());
            await SendAsync(new JsonObject
            {
                ["id"] = command["id"]!.GetValue<long>(),
                ["result"] = new JsonObject { ["target_id"] = "target-1", ["session_id"] = "session-1" },
            });
        });

        var result = await bridge.SendCommandAsync("Target.createTarget", new JsonObject { ["url"] = "https://example.com" }, CancellationToken.None);
        await responder;

        Assert.Equal("target-1", result["target_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Command_ErrorResponse_ThrowsBridgeException()
    {
        var responder = Task.Run(async () =>
        {
            var command = await ReceiveAsync();
            await SendAsync(new JsonObject
            {
                ["id"] = command["id"]!.GetValue<long>(),
                ["error"] = new JsonObject { ["message"] = "unknown target: target-x" },
            });
        });

        var exception = await Assert.ThrowsAsync<BrowserBridgeException>(() =>
            bridge.SendCommandAsync("Target.closeTarget", new JsonObject { ["targetId"] = "target-x" }, CancellationToken.None));
        await responder;

        Assert.Equal("extension_error", exception.Code);
        Assert.Contains("unknown target", exception.Message);
    }

    [Fact]
    public async Task Command_Timeout_ThrowsBridgeException()
    {
        bridge.CommandTimeout = TimeSpan.FromMilliseconds(300);

        var exception = await Assert.ThrowsAsync<BrowserBridgeException>(() =>
            bridge.SendCommandAsync("Target.getTargets", null, CancellationToken.None));

        Assert.Equal("command_timeout", exception.Code);
    }

    [Fact]
    public async Task TargetEvents_TrackedInRegistry()
    {
        var events = new List<BrowserBridgeEvent>();
        bridge.BridgeEvent += events.Add;

        await SendAsync(new JsonObject
        {
            ["method"] = "Browser.event",
            ["params"] = new JsonObject
            {
                ["name"] = "targetCreated",
                ["targetId"] = "target-9",
                ["sessionId"] = "session-9",
                ["tabId"] = 42,
                ["url"] = "https://relay.example.com",
                ["title"] = "New-API",
            },
        });
        await WaitForAsync(() => bridge.Targets.Count == 1);

        var target = Assert.Single(bridge.Targets);
        Assert.Equal("session-9", target.SessionId);
        Assert.Equal(42, target.TabId);
        Assert.Contains(events, item => item.Kind == BrowserBridgeEvent.TargetCreated);

        await SendAsync(new JsonObject
        {
            ["method"] = "Browser.event",
            ["params"] = new JsonObject { ["name"] = "targetClosed", ["targetId"] = "target-9" },
        });
        await WaitForAsync(() => bridge.Targets.Count == 0);

        Assert.Empty(bridge.Targets);
        Assert.Contains(events, item => item.Kind == BrowserBridgeEvent.TargetClosed);
    }

    [Fact]
    public async Task SessionCommand_UnknownSession_Throws()
    {
        var exception = await Assert.ThrowsAsync<BrowserBridgeException>(() =>
            bridge.SendSessionCommandAsync("session-nope", "Page.read", null, CancellationToken.None));

        Assert.Equal("unknown_session", exception.Code);
    }

    [Fact]
    public async Task SessionCommand_RoutesThroughSendCommand()
    {
        await SendAsync(new JsonObject
        {
            ["method"] = "Browser.event",
            ["params"] = new JsonObject
            {
                ["name"] = "targetCreated",
                ["targetId"] = "target-1",
                ["sessionId"] = "session-1",
                ["tabId"] = 7,
                ["url"] = "https://example.com",
            },
        });
        await WaitForAsync(() => bridge.Targets.Count == 1);

        var responder = Task.Run(async () =>
        {
            var command = await ReceiveAsync();
            Assert.Equal("Session.sendCommand", command["method"]!.GetValue<string>());
            Assert.Equal("session-1", command["params"]!["sessionId"]!.GetValue<string>());
            Assert.Equal("Page.read", command["params"]!["method"]!.GetValue<string>());
            await SendAsync(new JsonObject
            {
                ["id"] = command["id"]!.GetValue<long>(),
                ["result"] = new JsonObject { ["content"] = "页面文本" },
            });
        });

        var result = await bridge.SendSessionCommandAsync("session-1", "Page.read", new JsonObject { ["mode"] = "text" }, CancellationToken.None);
        await responder;

        Assert.Equal("页面文本", result["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Disconnect_ClearsTargetsAndMarksOffline()
    {
        await SendAsync(new JsonObject
        {
            ["method"] = "Browser.event",
            ["params"] = new JsonObject
            {
                ["name"] = "targetCreated",
                ["targetId"] = "target-1",
                ["sessionId"] = "session-1",
                ["tabId"] = 7,
            },
        });
        await WaitForAsync(() => bridge.Targets.Count == 1);

        await extension.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        await WaitForAsync(() => !bridge.IsExtensionConnected && bridge.Targets.Count == 0);

        Assert.False(bridge.IsExtensionConnected);
        Assert.Empty(bridge.Targets);
    }

    [Fact]
    public async Task Command_WhenOffline_Throws()
    {
        await extension.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        await WaitForAsync(() => !bridge.IsExtensionConnected);

        var exception = await Assert.ThrowsAsync<BrowserBridgeException>(() =>
            bridge.SendCommandAsync("Target.getTargets", null, CancellationToken.None));

        Assert.Equal("browser_bridge_offline", exception.Code);
    }

    // ---------- WebSocket 客户端辅助 ----------

    private async Task SendAsync(JsonObject message, ClientWebSocket? client = null)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        await (client ?? extension).SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<JsonObject> ReceiveAsync()
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult segment;
        do
        {
            segment = await extension.ReceiveAsync(buffer, CancellationToken.None);
            stream.Write(buffer, 0, segment.Count);
        }
        while (!segment.EndOfMessage);

        return JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()))!.AsObject();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }

        Assert.Fail("等待条件超时。");
    }
}
