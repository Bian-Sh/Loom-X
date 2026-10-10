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
    /// <summary>宿主当前生成与校验所依据的协议版本。</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>宿主仍可渲染的最低协议版本；v1 只允许展示型节点。</summary>
    public const int MinimumSchemaVersion = 1;

    /// <summary>交互节点（按钮与字段）可用的最低协议版本。</summary>
    public const int InteractiveSchemaVersion = 2;
}

/// <summary>声明式 UI 节点基类；契约程序集不依赖任何具体 UI 框架。</summary>
public abstract record PluginUiNode;

public sealed record PluginUiStackNode(
    IReadOnlyList<PluginUiNode> Children,
    PluginUiOrientation Orientation = PluginUiOrientation.Vertical,
    double Spacing = 0) : PluginUiNode;

/// <summary>
/// 声明式网格。<see cref="ColumnWeights"/> 给出各列的宽度策略，缺省或 null 表示所有列等宽：
/// 正值表示该列占用的相对权重（Star），0 或负值表示该列按内容自适应（Auto）。
/// 这样插件既能表达「等宽卡片网格」，也能表达「开关贴左 + 文字占满剩余空间」这类非等分布局。
/// </summary>
public sealed record PluginUiGridNode(
    int Columns,
    IReadOnlyList<PluginUiGridItem> Children,
    double ColumnSpacing = 0,
    double RowSpacing = 0,
    IReadOnlyList<double>? ColumnWeights = null) : PluginUiNode;

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
/// 声明式按钮。点击时宿主把 <see cref="Arguments"/>、<see cref="ReadsFields"/>
/// 指定的表单字段值与节点自身字段值打包成命令回传插件；
/// 按钮不携带业务数据，只携带插件自证的标识与参数。
/// </summary>
public sealed record PluginUiButtonNode(
    string Label,
    string CommandId,
    IReadOnlyDictionary<string, string>? Arguments = null,
    PluginUiTone Tone = PluginUiTone.Default,
    bool IsPrimary = false,
    IReadOnlyList<string>? ReadsFields = null) : PluginUiNode;

/// <summary>
/// 声明式单行文本字段；用户输入的当前值由宿主在命令回传时以 FieldId 为键提交。
/// TextFieldNode 自身不携带命令：它的值只随引用它的按钮 <see cref="PluginUiButtonNode.ReadsFields"/> 提交。
/// </summary>
public sealed record PluginUiTextFieldNode(
    string FieldId,
    string Label,
    string Value = "",
    string? Watermark = null,
    bool IsReadOnly = false) : PluginUiNode;

/// <summary>
/// 声明式开关字段；当前勾选状态由宿主在命令回传时以 FieldId 为键提交。
/// CommandId 非空时，宿主在勾选状态变化后立即回传一次命令（无需额外提交按钮）。
/// Label 为空串时宿主只渲染开关本身，不渲染任何标签文字。
/// </summary>
public sealed record PluginUiToggleFieldNode(
    string FieldId,
    string Label,
    bool IsChecked = false,
    string? Caption = null,
    string? CommandId = null,
    IReadOnlyDictionary<string, string>? Arguments = null) : PluginUiNode;

/// <summary>
/// 声明式下拉字段；选项为插件自有的安全文案，当前选中值以 FieldId 为键回传。
/// CommandId 非空时，宿主在选中项变化后立即回传一次命令。
/// </summary>
public sealed record PluginUiChoiceFieldNode(
    string FieldId,
    string Label,
    IReadOnlyList<string> Options,
    string SelectedValue = "",
    string? CommandId = null,
    IReadOnlyDictionary<string, string>? Arguments = null) : PluginUiNode;

/// <summary>
/// 插件可选实现的声明式 UI Provider。事件只表示快照失效，不携带业务数据；
/// Router 重新调用 GetUiContributions 获取当前文化下的安全快照。
/// </summary>
public interface IPluginUiContributionProvider
{
    event EventHandler? UiInvalidated;

    IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context);
}
