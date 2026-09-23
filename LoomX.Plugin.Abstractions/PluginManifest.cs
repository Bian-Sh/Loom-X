namespace LoomX.Plugins;

/// <summary>Router Provider 传输边界的扩展点类别。</summary>
public enum ExtensionKind
{
    Request,
    Response,
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
/// <summary>Manifest 中声明的 UI Contribution；内容由插件运行时 Provider 提供。</summary>
public sealed record PluginManifestUiContribution(string Id, PluginUiSlot Slot);

/// <summary>插件可选的 UI 声明集合。</summary>
public sealed record PluginManifestUi(IReadOnlyList<PluginManifestUiContribution> Contributions);

public sealed record PluginManifest(
    string Id,
    string Version,
    string Assembly,
    string PluginType,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<PluginManifestExtension> Extensions,
    PluginManifestUi? Ui = null);