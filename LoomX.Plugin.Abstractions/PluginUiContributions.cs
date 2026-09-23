namespace LoomX.Plugins;

/// <summary>插件声明式 UI 的宿主挂载位置。</summary>
public enum PluginUiSlot
{
    CardBody,
    DetailBody,
}

/// <summary>声明式布局方向。</summary>
public enum PluginUiOrientation
{
    Vertical,
    Horizontal,
}

/// <summary>由宿主主题解释的语义色，不允许插件直接传入 Brush 或资源键。</summary>
public enum PluginUiTone
{
    Default,
    Accent,
    Success,
    Warning,
    Danger,
}

/// <summary>由宿主主题解释的文字层级。</summary>
public enum PluginUiTextRole
{
    Title,
    Body,
    Metric,
    Caption,
}

/// <summary>声明式节点的对齐方式。</summary>
public enum PluginUiAlignment
{
    Stretch,
    Start,
    Center,
    End,
}

/// <summary>宿主请求插件 UI 快照时提供的安全上下文。</summary>
public sealed record PluginUiContext(string CultureName);

/// <summary>插件向宿主指定位置贡献的一棵声明式 UI 节点树。</summary>
public sealed record PluginUiContribution(
    int SchemaVersion,
    string Id,
    PluginUiSlot Slot,
    PluginUiNode Root)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>声明式 UI 节点基类；契约程序集不依赖任何具体 UI 框架。</summary>
public abstract record PluginUiNode;

public sealed record PluginUiStackNode(
    IReadOnlyList<PluginUiNode> Children,
    PluginUiOrientation Orientation = PluginUiOrientation.Vertical,
    double Spacing = 0) : PluginUiNode;

public sealed record PluginUiGridNode(
    int Columns,
    IReadOnlyList<PluginUiGridItem> Children,
    double ColumnSpacing = 0,
    double RowSpacing = 0) : PluginUiNode;

public sealed record PluginUiGridItem(
    PluginUiNode Content,
    int ColumnSpan = 1,
    PluginUiAlignment HorizontalAlignment = PluginUiAlignment.Stretch,
    PluginUiAlignment VerticalAlignment = PluginUiAlignment.Stretch);

public sealed record PluginUiSurfaceNode(
    PluginUiNode Content,
    PluginUiTone Tone = PluginUiTone.Default,
    double Padding = 0,
    double CornerRadius = 0,
    bool ShowBorder = true) : PluginUiNode;

public sealed record PluginUiTextNode(
    string Text,
    PluginUiTextRole Role = PluginUiTextRole.Body,
    PluginUiTone Tone = PluginUiTone.Default,
    bool Wrap = false,
    PluginUiAlignment HorizontalAlignment = PluginUiAlignment.Start) : PluginUiNode;

public sealed record PluginUiIconNode(
    string Data,
    PluginUiTone Tone = PluginUiTone.Default,
    double Size = 16,
    PluginUiAlignment HorizontalAlignment = PluginUiAlignment.Center) : PluginUiNode;

public sealed record PluginUiDividerNode(
    PluginUiTone Tone = PluginUiTone.Default) : PluginUiNode;

/// <summary>
/// 插件可选实现的声明式 UI Provider。事件只表示快照失效，不携带业务数据；
/// Router 重新调用 GetUiContributions 获取当前文化下的安全快照。
/// </summary>
public interface IPluginUiContributionProvider
{
    event EventHandler? UiInvalidated;

    IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context);
}
