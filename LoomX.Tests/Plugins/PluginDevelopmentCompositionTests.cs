using System.Reflection;
using LoomX.Assistant;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginDevelopmentCompositionTests
{
    [Fact]
    public async Task 生产注册绑定同一编译器与生产验证入口并暴露六个工具()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var type = typeof(ToastService).Assembly.GetType("LoomX.Services.PluginDevelopmentComposition");
        Assert.NotNull(type);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(fixture.Runtime);
        type.GetMethod("RegisterServices", BindingFlags.Static | BindingFlags.Public)!.Invoke(null,
            [services, fixture.DevelopmentRoot, Path.Combine(fixture.Root, "composition-cache")]);
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<PluginDevelopmentService>();
        Assert.Same(provider.GetRequiredService<SourcePluginCompiler>(), provider.GetRequiredService<SourcePluginCompiler>());
        var registry = new ToolRegistry();
        type.GetMethod("RegisterTools", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [registry, provider]);
        Assert.Equal(PluginDevelopmentTools.Names, registry.All.Select(tool => tool.Name));
        Assert.True((await service.CreateAsync("demo")).Success);
        Assert.True((await service.ValidateAsync("demo")).Success);
        Assert.True((await service.CompileAsync("demo")).Success);
        Assert.True((await service.TestAsync("demo", trustCode: true)).Success);
        var validation = provider.GetRequiredService<PluginDevelopmentValidation>();
        await Assert.ThrowsAsync<LoomX.Plugins.PluginBridgeException>(() => validation.ReadPackageFileAsync(
            Path.Combine(fixture.DevelopmentRoot, "demo"), "../outside.cs", CancellationToken.None));
    }
}