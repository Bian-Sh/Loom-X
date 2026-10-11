namespace LoomX.Plugins;

/// <summary>
/// 插件初始化上下文。DataDirectory 是插件自有数据目录（规则等 Plugin-owned 数据落在这里），
/// 插件不得写宿主核心配置。
/// </summary>
public sealed record PluginInitializationContext(string PluginId, string DataDirectory)
{
    /// <summary>未由宿主装配的旧上下文不默认授予任何能力。</summary>
    public IPluginBridge Bridge { get; init; } = PluginBridgeDefaults.Unavailable;
}

/// <summary>
/// 插件入口。宿主在独立 AssemblyLoadContext 中实例化本接口实现；
/// 插件与宿主之间只允许通过本契约程序集交换类型。
/// </summary>
public interface ILoomXPlugin
{
    /// <summary>插件 id，必须与 Manifest 的 id 一致。</summary>
    string Id { get; }

    /// <summary>宿主在注册 Extension 前调用，插件在此加载自有数据。</summary>
    void Initialize(PluginInitializationContext context);

    /// <summary>创建本插件的全部 Extension 实例；一个插件可注册多个 Extension。</summary>
    IEnumerable<IPipelineExtension> CreateExtensions();
}

/// <summary>
/// 可选契约：插件自行接管"停用"的语义。
/// <para>
/// 默认情况下宿主停用插件会把它的全部 Entry 一并关闭。实现本契约后，宿主改为
/// 只回调 <see cref="OnPluginEnabledChanged"/> 而不关闭 Entry，由插件自己决定停用期间
/// 各 Extension 的行为。
/// </para>
/// <para>
/// 适用场景：停用不应等同于能力消失。例如凭据保护停用后停止识别与 token 化新明文，
/// 但仍须为历史会话中已存在的 placeholder 注入完整性指令并完成恢复，
/// 否则旧会话的凭据引用会永久失效。
/// </para>
/// </summary>
public interface IPluginEnableStateAware
{
    /// <summary>
    /// 插件启停状态变化。实现方应保证该调用不抛异常；
    /// 宿主在调用后仍以本接口返回的插件自身状态展示启停结果。
    /// </summary>
    void OnPluginEnabledChanged(bool enabled);
}
