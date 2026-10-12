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
/// <summary>
/// Manifest 中声明的 UI Contribution；内容由插件运行时 Provider 提供。
/// Interactive 表示该 Contribution 会包含交互节点并接收命令回传，
/// 宿主只为声明为交互的 Contribution 挂载命令通道。
/// </summary>
public sealed record PluginManifestUiContribution(string Id, PluginUiSlot Slot, bool Interactive = false);

/// <summary>插件可选的 UI 声明集合。</summary>
public sealed record PluginManifestUi(IReadOnlyList<PluginManifestUiContribution> Contributions);

/// <summary>显式声明的程序集或 C# 源码加载入口，不包含宿主实现类型。</summary>
public sealed record PluginRuntimeManifest(
    string Kind,
    string EntryType,
    string? Assembly = null,
    IReadOnlyList<string>? Sources = null);

/// <summary>插件清单；保留既有位置构造与解构签名以兼容旧插件。</summary>
public sealed record PluginManifest(
    string Id,
    string Version,
    string Assembly,
    string PluginType,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<PluginManifestExtension> Extensions,
    PluginManifestUi? Ui = null)
{
    /// <summary>可选加载入口；解析旧清单时规范化为 assembly Runtime。</summary>
    public PluginRuntimeManifest? Runtime { get; init; }

    /// <summary>向宿主申请的版本化能力，与插件自身的 Capabilities 分离。</summary>
    public IReadOnlyList<string> HostCapabilities { get; init; } = [];
}