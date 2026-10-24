using LoomX.Assistant;
using LoomX.Plugins.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>桌面与服务端共用开发工具接线；验证、读取与编译都使用生产入口。</summary>
internal static class PluginDevelopmentComposition
{
    public static void RegisterServices(IServiceCollection services, string developmentRoot, string cacheRoot, string? seedRoot = null)
    {
        services.AddSingleton(new PluginDevelopmentOptions(developmentRoot));
        services.AddSingleton(services => new SourcePluginCompiler(cacheRoot,
            services.GetRequiredService<ILoggerFactory>().CreateLogger<SourcePluginCompiler>(), seedRoot));
        services.AddSingleton(new PluginDevelopmentValidation(async (directory, cancellationToken) =>
        {
            var result = await PluginPackageValidation.ValidateAsync(directory, cancellationToken).ConfigureAwait(false);
            return new(result.Success, result.Manifest, result.Diagnostics);
        }, PluginPackageValidation.ResolvePackageDirectory, PluginPackageValidation.ReadPackageFileAsync));
        services.AddSingleton(services => new PluginDevelopmentService(
            services.GetRequiredService<PluginDevelopmentOptions>(), services.GetRequiredService<SourcePluginCompiler>(),
            services.GetRequiredService<PluginRuntime>(), services.GetRequiredService<ILoggerFactory>(),
            services.GetRequiredService<PluginDevelopmentValidation>()));
    }

    public static void RegisterTools(ToolRegistry registry, IServiceProvider services) =>
        PluginDevelopmentTools.RegisterAll(registry, services.GetRequiredService<PluginDevelopmentService>());
}