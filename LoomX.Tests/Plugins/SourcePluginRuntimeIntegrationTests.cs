using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class SourcePluginRuntimeIntegrationTests
{
    [Fact]
    public async Task 源码入口通过共享契约加载且更新失败保持旧实例()
    {
        var package = new SourcePluginTestPackage();
        var options = Options(package);
        await using var runtime = PluginRuntime.Start(options, NullLoggerFactory.Instance);
        Assert.Equal("demo", Assert.Single(runtime.PluginInfos).Id);
        var kind = typeof(LoadedPluginInfo).GetProperty("RuntimeKind");
        Assert.NotNull(kind);
        Assert.Equal("csharp-source", kind.GetValue(Assert.Single(runtime.PluginInfos)));
        package.Write("src/Plugin.cs", "故意制造语法错误");
        Assert.False(await runtime.ReloadAsync("demo"));
        Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
        Assert.Contains(runtime.StructuredDiagnostics, diagnostic => diagnostic.Stage == PluginDiagnosticStage.Compile && diagnostic.Line.HasValue);
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source);
        package.SetManifest(version: "2");
        Assert.True(await runtime.ReloadAsync("demo"));
        Assert.Equal("2", Assert.Single(runtime.PluginInfos).Version);
    }

    [Fact]
    public async Task 编译成功但入口不实现契约时拒绝且返回稳定加载诊断()
    {
        var package = new SourcePluginTestPackage();
        package.Write("src/Plugin.cs", "namespace Demo; public sealed class Plugin { }");
        await using var runtime = PluginRuntime.Start(Options(package), NullLoggerFactory.Instance);
        Assert.Empty(runtime.PluginInfos);
        Assert.Contains(runtime.StructuredDiagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCodes.InvalidEntry);
    }

    [Fact]
    public async Task 插件初始化异常中的敏感值不进入宿主日志()
    {
        var package = new SourcePluginTestPackage();
        const string sentinel = "sk-private-initialize-sentinel-0123456789";
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source.Replace("public void Initialize(PluginInitializationContext context) { }",
            $"public void Initialize(PluginInitializationContext context) {{ throw new System.InvalidOperationException(\"{sentinel}\"); }}", StringComparison.Ordinal));
        using var logger = new CaptureFactory();
        await using var runtime = PluginRuntime.Start(Options(package), logger);
        Assert.Empty(runtime.PluginInfos);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(sentinel, StringComparison.Ordinal));
        Assert.Contains(runtime.StructuredDiagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCodes.InitializationFailed);
    }

    [Fact]
    public async Task Runtime在执行源码入口前拒绝夹带的SQLite实现()
    {
        var package = new SourcePluginTestPackage();
        package.Write("SQLitePCLRaw.core.dll", "不能由插件替换宿主实现");
        await using var runtime = PluginRuntime.Start(Options(package), NullLoggerFactory.Instance);
        Assert.Empty(runtime.PluginInfos);
        Assert.Contains(runtime.StructuredDiagnostics, diagnostic => diagnostic.Code == "LX_DEPENDENCY_FORBIDDEN");
    }
    private static PluginRuntimeOptions Options(SourcePluginTestPackage package)
    {
        var options = new PluginRuntimeOptions { PluginDirectory = package.Root, DataRootDirectory = Path.Combine(package.Root, "data") };
        var property = typeof(PluginRuntimeOptions).GetProperty("SourceCompiler");
        Assert.NotNull(property);
        property.SetValue(options, package.Compiler);
        return options;
    }

    private sealed class CaptureFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception?.ToString());
    }
}