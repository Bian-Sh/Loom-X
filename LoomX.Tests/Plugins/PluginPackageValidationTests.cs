using System.Reflection;
using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginPackageValidationTests
{
    [Fact]
    public async Task 生产验证一次收集缺失源码与不兼容能力()
    {
        using var package = new SourcePluginTestPackage();
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Directory, "plugin.manifest.json")))!;
        document["host_capabilities"] = new JsonArray("storage.sqlite/v2");
        document["runtime"]!["sources"] = new JsonArray("src/missing.cs");
        package.Write("plugin.manifest.json", document.ToJsonString());
        var result = await Validate(package.Directory);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCodes.CapabilityNotSupported);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Stage == PluginDiagnosticStage.SourceRead && diagnostic.RelativePath == "src/missing.cs");
    }

    [Fact]
    public async Task 试图夹带宿主SQLite实现的源码包在执行代码前拒绝()
    {
        using var package = new SourcePluginTestPackage();
        package.Write("runtimes/win-x64/native/e_sqlite3.dll", "禁止的依赖标记");
        var result = await Validate(package.Directory);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LX_DEPENDENCY_FORBIDDEN");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains(package.Root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task 有效源码包使用与Runtime相同的生产规则()
    {
        using var package = new SourcePluginTestPackage();
        var result = await Validate(package.Directory);
        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:/absolute")]
    [InlineData("other/plugin")]
    [InlineData("")]
    public void 开发根中的包目录只由安全插件身份决定(string id)
    {
        var type = ValidationType();
        var method = type.GetMethod("ResolvePackageDirectory");
        Assert.NotNull(method);
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [Path.GetTempPath(), id]));
        Assert.IsType<PluginBridgeException>(error.InnerException);
    }

    [Theory]
    [InlineData("src/Plugin.cs", true)]
    [InlineData("../outside.cs", false)]
    [InlineData("C:/external.cs", false)]
    public async Task 开发工具读取复用生产路径边界(string path, bool valid)
    {
        using var package = new SourcePluginTestPackage();
        var method = ValidationType().GetMethod("ReadPackageFileAsync");
        Assert.NotNull(method);
        if (valid)
        {
            var bytes = await Assert.IsAssignableFrom<Task<byte[]>>(method.Invoke(null, [package.Directory, path, CancellationToken.None]));
            Assert.NotEmpty(bytes);
        }
        else
        {
            var error = await Assert.ThrowsAsync<PluginBridgeException>(async () =>
                await Assert.IsAssignableFrom<Task<byte[]>>(method.Invoke(null, [package.Directory, path, CancellationToken.None])));
            Assert.Equal(PluginBridgeError.InvalidArgument, error.Error);
            Assert.DoesNotContain(package.Root, error.Message);
        }
    }

    private static Type ValidationType()
    {
        var type = typeof(PluginRuntime).Assembly.GetType("LoomX.Plugins.Host.PluginPackageValidation");
        Assert.NotNull(type);
        return type;
    }
    private static async Task<(bool Success, IReadOnlyList<PluginDiagnostic> Diagnostics)> Validate(string directory)
    {
        var method = ValidationType().GetMethod("ValidateAsync");
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(null, [directory, CancellationToken.None]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return ((bool)result.GetType().GetProperty("Success")!.GetValue(result)!,
            Assert.IsAssignableFrom<IReadOnlyList<PluginDiagnostic>>(result.GetType().GetProperty("Diagnostics")!.GetValue(result)));
    }
}