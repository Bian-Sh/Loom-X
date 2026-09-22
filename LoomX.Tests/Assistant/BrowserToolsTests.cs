using Xunit;
using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.Browser;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

public sealed class BrowserToolsTests
{
    private readonly FakeBrowserBridge bridge = new();
    private readonly ToolRegistry registry = new();
    private readonly BrowserBridgeLeaseManager leaseManager;

    public BrowserToolsTests()
    {
        leaseManager = new BrowserBridgeLeaseManager(bridge, NullLogger<BrowserBridgeLeaseManager>.Instance);
        BrowserTools.RegisterAll(registry, bridge, leaseManager);
    }

    [Fact]
    public void Registers_ElevenBrowserTools()
    {
        var expected = new[]
        {
            "browser.bridge_start", "browser.bridge_stop",
            "browser.tabs", "browser.open", "browser.read", "browser.click",
            "browser.type", "browser.wait", "browser.screenshot", "browser.network", "browser.close",
        };
        foreach (var name in expected)
        {
            Assert.True(registry.TryGet(name, out _), $"缺少工具 {name}");
        }
    }

    [Fact]
    public async Task BridgeStart_等待Extension重连后再返回状态()
    {
        bridge.IsExtensionConnected = false;
        var reconnect = Task.Run(async () =>
        {
            await Task.Delay(100);
            bridge.IsExtensionConnected = true;
        });

        var result = await InvokeAsync("browser.bridge_start", """{"assistant_session_id":"assistant-wait"}""");
        await reconnect;

        Assert.True(result.Success, result.Content);
        Assert.Contains("\"extension_connected\":true", result.Content, StringComparison.Ordinal);
        var tool = registry.All.Single(item => item.Name == "browser.bridge_start");
        Assert.True(tool.Timeout > TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Bridge工具按AssistantSessionId申请和释放租约()
    {
        var start = await InvokeAsync("browser.bridge_start", """{"assistant_session_id":"assistant-a"}""");

        Assert.True(start.Success, start.Content);
        Assert.True(bridge.IsListening);
        Assert.Equal(1, bridge.StartCount);
        Assert.Contains("\"lease_count\":1", start.Content, StringComparison.Ordinal);

        var stop = await InvokeAsync("browser.bridge_stop", """{"assistant_session_id":"assistant-a"}""");

        Assert.True(stop.Success, stop.Content);
        Assert.False(bridge.IsListening);
        Assert.Equal(1, bridge.StopCount);
        Assert.Contains("\"lease_count\":0", stop.Content, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Tabs_ListsOnlyAutomationTargets()
    {
        bridge.TargetList.Add(new BrowserTargetInfo("target-1", "session-1", 7, "https://relay.example.com", "New-API"));

        var result = await InvokeAsync("browser.tabs");

        Assert.True(result.Success);
        var json = JsonNode.Parse(result.Content)!;
        Assert.True(json["extension_connected"]!.GetValue<bool>());
        var target = Assert.Single(json["targets"]!.AsArray());
        Assert.Equal("session-1", target!["session_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Open_RejectsNonHttpUrl()
    {
        var result = await InvokeAsync("browser.open", """{"url":"file:///etc/passwd"}""");

        Assert.False(result.Success);
        Assert.Contains("invalid_url", result.Content);
        Assert.Empty(bridge.Sent);
    }

    [Fact]
    public async Task Open_ValidUrl_CreatesTarget()
    {
        bridge.NextResult = new JsonObject { ["target_id"] = "target-1", ["session_id"] = "session-1", ["tab_id"] = 7 };

        var result = await InvokeAsync("browser.open", """{"url":"https://relay.example.com"}""");

        Assert.True(result.Success);
        var (method, parameters) = Assert.Single(bridge.Sent);
        Assert.Equal("Target.createTarget", method);
        Assert.Equal("https://relay.example.com", parameters!["url"]!.GetValue<string>());
        Assert.Contains("session-1", result.Content);
    }

    [Fact]
    public async Task Read_原样返回Bridge结构化结果_统一保护由AgentLoop负责()
    {
        const string secret = "sk-livekey0123456789abcdef";
        bridge.NextResult = new JsonObject
        {
            ["content"] = $"令牌：{secret} 已创建，备用：{secret}",
        };

        var result = await InvokeAsync("browser.read", """{"session_id":"session-1"}""");

        Assert.True(result.Success);
        Assert.Contains(secret, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_原样返回Bridge网络结果_统一保护由AgentLoop负责()
    {
        bridge.NextResult = new JsonObject
        {
            ["requests"] = new JsonArray(new JsonObject
            {
                ["url"] = "https://relay.example.com/v1/chat/completions",
                ["method"] = "POST",
                ["model"] = "gpt-4o",
                ["request_headers"] = new JsonObject { ["Authorization"] = "Bearer sk-livekey0123456789abcdef" },
            }),
        };

        var result = await InvokeAsync("browser.network", """{"session_id":"session-1","url_contains":"chat"}""");

        Assert.True(result.Success);
        Assert.Contains("sk-livekey0123456789abcdef", result.Content, StringComparison.Ordinal);
        Assert.Contains("gpt-4o", result.Content);
    }

    [Fact]
    public async Task BridgeException_SurfacedAsFailedResult()
    {
        bridge.NextError = new BrowserBridgeException("unknown_session", "未知的浏览器会话：session-x");

        var result = await InvokeAsync("browser.read", """{"session_id":"session-x"}""");

        Assert.False(result.Success);
        Assert.True(result.FailureContentIsSafe);
        Assert.Contains("unknown_session", result.Content);
    }

    [Fact]
    public async Task OfflineBridge_SurfacedAsFailedResult()
    {
        bridge.NextError = new BrowserBridgeException("browser_bridge_offline", "Chrome Extension 未连接 Browser Bridge。");

        var result = await InvokeAsync("browser.open", """{"url":"https://example.com"}""");

        Assert.False(result.Success);
        Assert.True(result.FailureContentIsSafe);
        Assert.Contains("browser_bridge_offline", result.Content);
    }

    [Fact]
    public async Task Type_SendsSelectorAndText()
    {
        var result = await InvokeAsync("browser.type", """{"session_id":"session-1","selector":"#name","text":"我的 Provider"}""");

        Assert.True(result.Success);
        var (method, parameters) = Assert.Single(bridge.Sent);
        Assert.Equal("session:session-1:Page.type", method);
        Assert.Equal("#name", parameters!["selector"]!.GetValue<string>());
        Assert.Equal("我的 Provider", parameters["text"]!.GetValue<string>());
    }

    private async Task<ToolResult> InvokeAsync(string name, string? argumentsJson = null)
    {
        var tool = registry.All.First(item => item.Name == name);
        return await tool.Handler(argumentsJson is null ? null : JsonNode.Parse(argumentsJson), CancellationToken.None);
    }

    private sealed class FakeBrowserBridge : IBrowserBridge, IBrowserBridgeLifecycle
    {
        public List<BrowserTargetInfo> TargetList { get; } = new();
        public JsonNode NextResult { get; set; } = new JsonObject();
        public Exception? NextError { get; set; }
        public List<(string Method, JsonNode? Params)> Sent { get; } = new();

        public bool IsListening { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            IsListening = false;
            return Task.CompletedTask;
        }
        public bool IsExtensionConnected { get; set; } = true;

        public IReadOnlyCollection<BrowserTargetInfo> Targets => TargetList;

        public event Action<BrowserBridgeEvent>? BridgeEvent
        {
            add { }
            remove { }
        }

        public Task<JsonNode> SendCommandAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
        {
            Sent.Add((method, parameters));
            if (NextError is not null) throw NextError;
            return Task.FromResult(NextResult);
        }

        public Task<JsonNode> SendSessionCommandAsync(string sessionId, string method, JsonNode? parameters, CancellationToken cancellationToken)
        {
            Sent.Add(($"session:{sessionId}:{method}", parameters));
            if (NextError is not null) throw NextError;
            return Task.FromResult(NextResult);
        }
    }
}
