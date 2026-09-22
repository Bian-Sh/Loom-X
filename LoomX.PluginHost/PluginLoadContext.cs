using System.Reflection;
using System.Runtime.Loader;

namespace LoomX.Plugins.Host;

/// <summary>
/// 每个插件一个 collectible AssemblyLoadContext。
/// 契约程序集与 System.*/宿主已加载程序集回退 Default 上下文解析，保证类型身份唯一；
/// 插件自身程序集经 AssemblyDependencyResolver 从插件目录解析。
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LoomX.Plugin.Abstractions",
    };

    private readonly AssemblyDependencyResolver resolver;

    public PluginLoadContext(string pluginMainAssemblyPath, string name)
        : base(name, isCollectible: true)
    {
        resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null || IsShared(name))
        {
            // 回退 Default 上下文：契约类型身份唯一，插件与宿主交换的必然是同一份契约程序集。
            return null;
        }

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static bool IsShared(string assemblyName) =>
        SharedAssemblyNames.Contains(assemblyName)
        || assemblyName is "netstandard" or "mscorlib"
        || Default.Assemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
}
