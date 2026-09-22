namespace LoomX.Plugins;

/// <summary>扩展点类别：Provider 请求、Provider 响应、工具结果、持久化。</summary>
public enum ExtensionKind
{
    Request,
    Response,
    ToolResult,
    Persistence,
}

/// <summary>
/// Extension 失败策略：普通 Entry 失败记录诊断并继续；数据安全类 Entry 失败 fail closed，不放行原始数据。
/// </summary>
public enum ExtensionFailurePolicy
{
    ContinueOnError,
    FailClosed,
}

/// <summary>Manifest 中声明的单个 Extension：归属唯一 Pipeline，声明失败策略与能力。</summary>
public sealed record PluginManifestExtension(
    string Id,
    ExtensionKind Kind,
    string Pipeline,
    ExtensionFailurePolicy FailurePolicy,
    IReadOnlyList<string> Capabilities);

/// <summary>
/// 插件清单。必备字段：id、version、extensions、capabilities；
/// assembly 与 pluginType 指示宿主加载入口。
/// </summary>
public sealed record PluginManifest(
    string Id,
    string Version,
    string Assembly,
    string PluginType,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<PluginManifestExtension> Extensions);
