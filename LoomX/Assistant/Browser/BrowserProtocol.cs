using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.Assistant.Browser;

/// <summary>
/// Browser Bridge 与 Chrome Extension 之间的 CDP-like 协议消息。
/// 命令：Bridge → Extension {id, method, params}；
/// 响应：Extension → Bridge {id, result?} 或 {id, error?}；
/// 事件：Extension → Bridge {method: "Browser.event", params: {name, sessionId?, data}}。
/// 消息只携带安全摘要，Secret 由 SecretHarvester 在 Bridge 侧拦截，绝不上行到模型。
/// </summary>
public static class BrowserProtocol
{
    public const int ProtocolVersion = 1;
    public const string HelloMethod = "Bridge.hello";
    public const string EventMethod = "Browser.event";

    // CDP-like Target 域
    public const string TargetGetTargets = "Target.getTargets";
    public const string TargetCreateTarget = "Target.createTarget";
    public const string TargetCloseTarget = "Target.closeTarget";

    // 会话级命令透传（chrome.debugger.sendCommand）
    public const string SessionSendCommand = "Session.sendCommand";

    public static JsonObject Command(long id, string method, JsonNode? parameters = null)
    {
        var command = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null) command["params"] = parameters;
        return command;
    }

    public static bool TryParse(string json, out JsonObject? message)
    {
        message = null;
        try
        {
            message = JsonNode.Parse(json) as JsonObject;
            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsResponse(JsonObject message) => message.ContainsKey("id");

    public static bool IsEvent(JsonObject message) =>
        !message.ContainsKey("id") && message["method"]?.GetValue<string>() == EventMethod;

    public static bool IsHello(JsonObject message) =>
        !message.ContainsKey("id") && message["method"]?.GetValue<string>() == HelloMethod;
}

/// <summary>一个已连接的 Extension 登记的自动化标签页。</summary>
public sealed record BrowserTargetInfo(
    string TargetId,
    string SessionId,
    int TabId,
    string Url,
    string Title);

/// <summary>Bridge 向外抛出的结构化事件（target 创建/关闭/掉线）。</summary>
public sealed record BrowserBridgeEvent(string Kind, BrowserTargetInfo? Target, string? Detail)
{
    public const string TargetCreated = "BrowserTargetCreated";
    public const string TargetClosed = "BrowserTargetClosed";
    public const string ExtensionConnected = "ExtensionConnected";
    public const string ExtensionDisconnected = "ExtensionDisconnected";
}
