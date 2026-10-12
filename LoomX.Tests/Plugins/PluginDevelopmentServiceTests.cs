using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginDevelopmentServiceTests
{
    [Fact]
    public async Task 创建模板有显式源码入口自测试四语言且生产编译成功()
    {
        await using var fixture = new PluginDevelopmentFixture();
        Assert.True((await fixture.Service.CreateAsync("demo", "Demo.Plugin")).Success);
        var directory = Path.Combine(fixture.DevelopmentRoot, "demo");
        var errors = new List<string>();
        var manifest = ManifestParser.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "plugin.manifest.json")), errors);
        Assert.NotNull(manifest);
        Assert.Empty(errors);
        Assert.Equal("csharp-source", manifest.Runtime!.Kind);
        Assert.Equal("Demo.Plugin", manifest.Runtime.EntryType);
        Assert.Contains("src/Plugin.cs", manifest.Runtime.Sources!);
        Assert.Contains("tests/PluginTests.cs", manifest.Runtime.Sources!);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories));
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            Assert.True(File.Exists(Path.Combine(directory, "i18n", culture + ".json")));
        Assert.True((await fixture.Service.CompileAsync("demo")).Success);
        Assert.Empty(fixture.Runtime.PluginInfos);
    }

    [Fact]
    public async Task 已有包非法输入和不兼容版本不会写入或覆盖()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package();
        var path = Path.Combine(fixture.DevelopmentRoot, "demo", "src", "Plugin.cs");
        var before = await File.ReadAllBytesAsync(path);
        var existing = await fixture.Service.CreateAsync("demo");
        Assert.False(existing.Success);
        Assert.Contains(existing.Diagnostics, item => item.Code == "LX_PACKAGE_EXISTS");
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        foreach (var id in new[] { "../escape", "a/b", "NUL", "C:/escape", "demo." })
        {
            var invalid = await fixture.Service.CreateAsync(id);
            Assert.False(invalid.Success);
            Assert.Contains(invalid.Diagnostics, item => item.Code == PluginDiagnosticCodes.InvalidPath);
        }
        Assert.False((await fixture.Service.CreateAsync("other", "Demo.class")).Success);
        Assert.False(Directory.Exists(Path.Combine(fixture.DevelopmentRoot, "other")));
        var incompatible = await fixture.Service.CreateAsync("version-test", pluginApiMajor: 99);
        Assert.Contains(incompatible.Diagnostics, item => item.Code == "LX_PLUGIN_API_INCOMPATIBLE");
        Assert.False(Directory.Exists(Path.Combine(fixture.DevelopmentRoot, "version-test")));
    }

    [Fact]
    public async Task 验证收集缺失源码和不支持能力且不会编译()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package(capabilities: ["storage.sqlite/v99"]);
        File.Delete(Path.Combine(fixture.DevelopmentRoot, "demo", "src", "Plugin.cs"));
        var result = await fixture.Service.ValidateAsync("demo");
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Stage == PluginDiagnosticStage.SourceRead);
        Assert.Contains(result.Diagnostics, item => item.Code == PluginDiagnosticCodes.CapabilityNotSupported);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "cache")));
    }

    [Fact]
    public async Task 编译诊断可定位并修复重编译且diagnostics不重编译()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package(source: "#error sk-private-literal-sentinel-123456");
        var failure = await fixture.Service.CompileAsync("demo");
        Assert.False(failure.Success);
        Assert.Contains(failure.Diagnostics, item => item.Code == "CS1029" && item.RelativePath == "src/Plugin.cs" && item.Line == 1 && item.Column.HasValue);
        Assert.DoesNotContain("sk-private", JsonSerializer.Serialize(failure));
        fixture.Write("demo", "src/Plugin.cs", PluginDevelopmentFixture.BasicSource);
        Assert.Equal(failure.Diagnostics, (await fixture.Service.DiagnosticsAsync("demo")).Diagnostics);
        var success = await fixture.Service.CompileAsync("demo");
        Assert.True(success.Success);
        Assert.NotNull(success.CacheKey);
        Assert.Equal("compiled", success.ArtifactStatus);
        var hit = await fixture.Service.CompileAsync("demo");
        Assert.True(hit.Success);
        Assert.True(hit.CacheHit);
        Assert.Equal(success.CacheKey, hit.CacheKey);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(success));
    }

    [Fact]
    public async Task 默认测试与重载不执行未授权可信代码()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package();
        foreach (var result in new[] { await fixture.Service.TestAsync("demo"), await fixture.Service.ReloadAsync("demo") })
        {
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, item => item.Code == "LX_TRUST_REQUIRED");
        }
        Assert.Empty(fixture.Runtime.PluginInfos);
        Assert.False(Directory.Exists(fixture.ProductionData));
    }

    [Fact]
    public async Task 隔离测试执行Pipeline声明式UI与SQLite自测试不触碰生产数据()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package(source: WorkingSource, capabilities: ["storage.sqlite/v1", "logging/v1", "notifications/v1"], withExtension: true, withUi: true);
        var result = await fixture.Service.TestAsync("demo", true, [new("development.request", "测试正文", "测试正文-已处理", PipelineOutcome.Modified)]);
        Assert.True(result.Success, JsonSerializer.Serialize(result));
        Assert.True(result.TestsPassed >= 2);
        Assert.Equal("trusted-in-process-temporary-data-not-os-sandbox", result.Isolation);
        Assert.Empty(fixture.Runtime.PluginInfos);
        Assert.False(Directory.Exists(fixture.ProductionData));
        Assert.DoesNotContain("测试正文", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("temporary.db", JsonSerializer.Serialize(result));
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task 插件测试异常和失败比较不回显敏感数据()
    {
        await using var fixture = new PluginDevelopmentFixture();
        const string sentinel = "sk-private-self-test-sentinel-123456";
        fixture.Package(source: WorkingSource.Replace("// 自测试开始", "throw new System.InvalidOperationException(" + JsonSerializer.Serialize(sentinel) + ");"), capabilities: ["storage.sqlite/v1", "logging/v1", "notifications/v1"], withExtension: true, withUi: true);
        var result = await fixture.Service.TestAsync("demo", true);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Stage == PluginDiagnosticStage.Test);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(result));
        fixture.Write("demo", "src/Plugin.cs", WorkingSource);
        var mismatch = await fixture.Service.TestAsync("demo", true, [new("development.request", sentinel, "另一个敏感正文")]);
        Assert.False(mismatch.Success);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(mismatch));
        Assert.DoesNotContain("另一个敏感正文", JsonSerializer.Serialize(mismatch));
    }

    [Fact]
    public async Task 测试拒绝无效UI和缺失声明的Extension()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package(source: WorkingSource.Replace("new PluginUiContribution(2,", "new PluginUiContribution(999,"), capabilities: ["storage.sqlite/v1", "logging/v1", "notifications/v1"], withExtension: true, withUi: true);
        var invalidUi = await fixture.Service.TestAsync("demo", true);
        Assert.False(invalidUi.Success);
        Assert.Contains(invalidUi.Diagnostics, item => item.Code == "LX_TEST_UI_FAILED");
        fixture.Package(source: PluginDevelopmentFixture.BasicSource, withExtension: true);
        var missing = await fixture.Service.TestAsync("demo", true);
        Assert.False(missing.Success);
        Assert.Contains(missing.Diagnostics, item => item.Code == "LX_TEST_REGISTRATION_FAILED");
    }

    [Fact]
    public async Task 显式reload用注入Runtime安装路径失败保留旧实例且不默认安装()
    {
        await using var fixture = new PluginDevelopmentFixture();
        fixture.Package(installed: true);
        await fixture.StartInstalledAsync();
        Assert.Equal("1", Assert.Single(fixture.Runtime.PluginInfos).Version);
        fixture.Write("demo", "src/Plugin.cs", "#error sk-private-reload-sentinel-123456", installed: true);
        var failed = await fixture.Service.ReloadAsync("demo", true);
        Assert.False(failed.Success);
        Assert.Equal("1", Assert.Single(fixture.Runtime.PluginInfos).Version);
        Assert.DoesNotContain("sk-private", JsonSerializer.Serialize(failed));
        fixture.Package(version: "2", installed: true);
        Assert.True((await fixture.Service.ReloadAsync("demo", true)).Success);
        Assert.Equal("2", Assert.Single(fixture.Runtime.PluginInfos).Version);
        fixture.Package("not-installed");
        Assert.False((await fixture.Service.ReloadAsync("not-installed", true)).Success);
        Assert.DoesNotContain(fixture.Runtime.PluginInfos, item => item.Id == "not-installed");
    }

    [Fact]
    public async Task 取消继续传播且不写入模板()
    {
        await using var fixture = new PluginDevelopmentFixture();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateAsync("demo", cancellationToken: source.Token));
        Assert.False(Directory.Exists(Path.Combine(fixture.DevelopmentRoot, "demo")));
    }

    internal const string WorkingSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using LoomX.Plugins;
        namespace Demo;
        public sealed class Plugin : ILoomXPlugin, IPluginUiContributionProvider
        {
            public string Id => "demo";
            public event EventHandler? UiInvalidated;
            public void Initialize(PluginInitializationContext context) { }
            public IEnumerable<IPipelineExtension> CreateExtensions() => new IPipelineExtension[] { new Echo() };
            public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) => new PluginUiContribution[]
            {
                new PluginUiContribution(2, "card", PluginUiSlot.CardBody, new PluginUiTextNode("开发测试")),
            };
        }
        public sealed class Echo : IRequestExtension
        {
            public string ExtensionId => "echo";
            public ExtensionKind Kind => ExtensionKind.Request;
            public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
            public IReadOnlyList<string> Capabilities => Array.Empty<string>();
            public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken ct)
                => ValueTask.FromResult(PipelineResult.Modify(payload + "-已处理"));
        }
        public static class PluginTests
        {
            public static async ValueTask RunAsync(PluginInitializationContext context, CancellationToken ct)
            {
                // 自测试开始
                if (!context.DataDirectory.Contains("LoomX-development-test-")) throw new InvalidOperationException("不是临时数据根。");
                await using var db = await context.Bridge.Storage.OpenSqliteAsync("temporary.db", ct);
                await db.ExecuteAsync(new PluginSqlCommand("CREATE TABLE Example (Value TEXT NOT NULL)"), ct);
                await db.ExecuteAsync(new PluginSqlCommand("INSERT INTO Example(Value) VALUES ($value)", new[] { new PluginSqlParameter("$value", PluginSqlValue.FromText("测试数据")) }), ct);
                var row = await db.QuerySingleAsync(new PluginSqlCommand("SELECT Value FROM Example"), ct);
                if (row?["Value"].Text != "测试数据") throw new InvalidOperationException("SQLite 测试失败。");
                await context.Bridge.Log.WriteAsync(new PluginLogEntry(PluginLogLevel.Information, "自测试完成"), ct);
                await context.Bridge.Notifications.ShowAsync(new PluginNotification(PluginNotificationLevel.Success, "自测试完成"), ct);
            }
        }
        """;
}
