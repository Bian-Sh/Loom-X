namespace LoomX.Plugins;

/// <summary>
/// 插件初始化上下文。DataDirectory 是插件自有数据目录（规则等 Plugin-owned 数据落在这里），
/// 插件不得写宿主核心配置。
/// </summary>
public sealed record PluginInitializationContext(string PluginId, string DataDirectory);

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
