using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginDevelopmentBoundaryTests
{
    [Fact]
    public async Task 合法入口与测试约定同名时生成模板仍可编译运行()
    {
        await using var fixture = new PluginDevelopmentFixture();
        Assert.True((await fixture.Service.CreateAsync("demo", "Demo.PluginTests")).Success);
        var compiled = await fixture.Service.CompileAsync("demo");
        Assert.True(compiled.Success, JsonSerializer.Serialize(compiled));
        Assert.True((await fixture.Service.TestAsync("demo", true)).Success);
    }

    [Fact]
    public async Task 入口全名不能把CSharp转义符误当作元数据名称()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var created = await fixture.Service.CreateAsync("demo", "Demo.@Plugin");
        Assert.False(created.Success);
        Assert.False(Directory.Exists(Path.Combine(fixture.DevelopmentRoot, "demo")));
    }

    [Fact]
    public async Task 真实生产验证一次返回多个缺失source能力和禁止依赖()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package(capabilities: ["logging/v99"]);
        var file = Path.Combine(fixture.DevelopmentRoot, "demo", "plugin.manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
        manifest["runtime"]!["sources"] = new JsonArray("src/missing-one.cs", "src/missing-two.cs");
        await File.WriteAllTextAsync(file, manifest.ToJsonString());
        fixture.Write("demo", "deps/Microsoft.Data.Sqlite.dll", "不应携带的提供程序");
        var validated = await fixture.Service.ValidateAsync("demo");
        Assert.False(validated.Success);
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.RelativePath == "src/missing-one.cs");
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.RelativePath == "src/missing-two.cs");
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCodes.CapabilityNotSupported);
        Assert.Contains(validated.Diagnostics, diagnostic => diagnostic.Code == "LX_DEPENDENCY_FORBIDDEN");
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "cache")));
    }

    [Fact]
    public async Task 服务成功失败日志有事件边界且不泄露插件异常原值()
    {
        await using var fixture = new PluginDevelopmentFixture();
        using var capture = new CaptureFactory();
        var service = new PluginDevelopmentService(new(fixture.DevelopmentRoot), fixture.Compiler, fixture.Runtime, capture,
            new(PluginDevelopmentFixture.ValidateProductionInputsAsync, PluginDevelopmentFixture.ResolveProductionPath, PluginDevelopmentFixture.ReadProductionFileAsync));
        Assert.True((await service.CreateAsync("created")).Success);
        const string sentinel = "sk-private-plugin-test-log-sentinel-123456";
        fixture.Package(source: PluginDevelopmentServiceTests.WorkingSource.Replace("// 自测试开始", "throw new System.InvalidOperationException(" + JsonSerializer.Serialize(sentinel) + ");"), capabilities: ["storage.sqlite/v1", "logging/v1", "notifications/v1"], withExtension: true, withUi: true);
        Assert.False((await service.TestAsync("demo", true)).Success);
        Assert.Contains(capture.Messages, message => message.Contains("操作开始"));
        Assert.Contains(capture.Messages, message => message.Contains("操作完成"));
        Assert.Contains(capture.Messages, message => message.Contains("操作失败"));
        Assert.DoesNotContain(sentinel, string.Join(';', capture.Messages));
        Assert.DoesNotContain(fixture.Root, string.Join(';', capture.Messages));
    }

    [Fact]
    public async Task Runtime忽略的额外未声明Extension不能伪装为测试成功()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var source = PluginDevelopmentServiceTests.WorkingSource.Replace("new Echo()", "new Echo(), new Extra()");
        source += """
            public sealed class Extra : IRequestExtension
            {
                public string ExtensionId => "undeclared";
                public ExtensionKind Kind => ExtensionKind.Request;
                public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
                public IReadOnlyList<string> Capabilities => Array.Empty<string>();
                public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken ct) => ValueTask.FromResult(PipelineResult.Pass(payload));
            }
            """;
        fixture.Package(source: source, capabilities: ["storage.sqlite/v1", "logging/v1", "notifications/v1"], withExtension: true, withUi: true);
        var result = await fixture.Service.TestAsync("demo", true);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "LX_TEST_RUNTIME_DIAGNOSTIC");
    }
    [Fact]
    public async Task 未协商能力原值不能借能力版本摘要回显()
    {
        await using var fixture = new PluginDevelopmentFixture();
        const string sentinel = "sk-private-capability-sentinel-123456/v99";
        fixture.Package(capabilities: [sentinel]);
        var result = await fixture.Service.ValidateAsync("demo");
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == PluginDiagnosticCodes.CapabilityNotSupported);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(result));
    }
    private sealed class CaptureFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception) + exception?.ToString());
    }
}
