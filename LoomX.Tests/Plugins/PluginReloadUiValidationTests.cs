using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginReloadUiValidationTests
{
    [Theory]
    [InlineData("schema")]
    [InlineData("undeclared")]
    [InlineData("slot")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("root")]
    [InlineData("missing")]
    [InlineData("provider")]
    [InlineData("throws")]
    [InlineData("ja-JP")]
    public async Task 无效候选UI拒绝重载且保留旧卡片与资源(string mode)
    {
        var fixture = new Fixture();
        fixture.Write("1", "valid");
        await using var runtime = fixture.Start();
        Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal("旧卡片1", Assert.IsType<PluginUiTextNode>(Assert.Single(runtime.GetUiContributions("reload.ui", PluginUiSlot.CardBody, "zh-CN")).Root).Text);
        fixture.Write("2", mode);

        Assert.False(await runtime.ReloadAsync("reload.ui"));

        Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            Assert.Equal("旧卡片1", Assert.IsType<PluginUiTextNode>(Assert.Single(runtime.GetUiContributions("reload.ui", PluginUiSlot.CardBody, culture)).Root).Text);
        Assert.Equal(2, fixture.Resources.Count);
        Assert.Equal(0, fixture.Resources[0].Disposals);
        Assert.Equal(1, fixture.Resources[1].Disposals);
        Assert.Contains(runtime.StructuredDiagnostics, item => item.Stage == PluginDiagnosticStage.Reload && item.Code == PluginDiagnosticCodes.ReloadFailed);
        var diagnostics = JsonSerializer.Serialize(runtime.StructuredDiagnostics) + string.Join(";", runtime.Diagnostics);
        Assert.DoesNotContain("sk-ui-private-sentinel", diagnostics);
        Assert.DoesNotContain(fixture.Root, diagnostics);
    }

    [Fact]
    public async Task 有效UI候选切换成功且旧租约排空后才释放资源()
    {
        var fixture = new Fixture();
        fixture.Write("1", "valid");
        await using var runtime = fixture.Start();
        await using var lease = runtime.AcquireLease();
        fixture.Write("2", "valid");
        Assert.True(await runtime.ReloadAsync("reload.ui"));
        Assert.Equal("2", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal("旧卡片2", Assert.IsType<PluginUiTextNode>(Assert.Single(runtime.GetUiContributions("reload.ui", PluginUiSlot.CardBody, "ja-JP")).Root).Text);
        Assert.Equal(0, fixture.Resources[0].Disposals);
        await lease.DisposeAsync();
        Assert.Equal(1, fixture.Resources[0].Disposals);
        Assert.Equal(0, fixture.Resources[1].Disposals);
    }

    [Fact]
    public async Task 首次加载无效UI保持既有降级而非拒绝整个插件()
    {
        var fixture = new Fixture();
        fixture.Write("1", "schema");
        await using var runtime = fixture.Start();
        Assert.Single(runtime.PluginInfos);
        Assert.Empty(runtime.GetUiContributions("reload.ui", PluginUiSlot.CardBody, "zh-CN"));
        Assert.Contains(runtime.Diagnostics, item => item.Contains("UNSUPPORTED_SCHEMA", StringComparison.Ordinal));
    }

    private sealed class Resource : IPluginLogBridge, IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Fixture
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "loomx-reload-ui-" + Guid.NewGuid().ToString("N"));
        public List<Resource> Resources { get; } = [];
        private string Package => Path.Combine(Root, "plugins", "reload.ui");
        public PluginRuntime Start() => PluginRuntime.Start(new()
        {
            PluginDirectory = Path.Combine(Root, "plugins"), DataRootDirectory = Path.Combine(Root, "data"),
            SourceCompiler = new SourcePluginCompiler(Path.Combine(Root, "cache")),
            BridgeFactory = (manifest, _) =>
            {
                var resource = new Resource(); Resources.Add(resource);
                return new PluginBridgeScope(manifest.Id, manifest.HostCapabilities, log: resource);
            },
        }, NullLoggerFactory.Instance);

        public void Write(string version, string mode)
        {
            Directory.CreateDirectory(Path.Combine(Package, "src"));
            Directory.CreateDirectory(Path.Combine(Package, "i18n"));
            foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
                File.WriteAllText(Path.Combine(Package, "i18n", culture + ".json"), "{}");
            File.WriteAllText(Path.Combine(Package, "plugin.manifest.json"), JsonSerializer.Serialize(new
            {
                id = "reload.ui", version, runtime = new { kind = "csharp-source", entry_type = "Reload.Plugin", sources = new[] { "src/Plugin.cs" } },
                host_capabilities = new[] { "logging/v1" }, capabilities = Array.Empty<string>(), extensions = Array.Empty<string>(),
                ui = new { contributions = new[] { new { id = "card", slot = "card-body" } } },
            }));
            var valid = $"new PluginUiContribution(2, \"card\", PluginUiSlot.CardBody, new PluginUiTextNode(\"旧卡片{version}\"))";
            var expression = mode switch
            {
                "schema" => $"new[] {{ {valid.Replace("(2,", "(999,")} }}",
                "undeclared" => $"new[] {{ {valid.Replace("card", "undeclared")} }}",
                "slot" => $"new[] {{ {valid.Replace("CardBody", "DetailBody")} }}",
                "duplicate" => $"new[] {{ {valid}, {valid} }}",
                "null" => "new PluginUiContribution[] { null! }",
                "root" => "new[] { new PluginUiContribution(2, \"card\", PluginUiSlot.CardBody, null!) }",
                "missing" => "Array.Empty<PluginUiContribution>()",
                "throws" => "throw new InvalidOperationException(\"sk-ui-private-sentinel\")",
                "ja-JP" => $"new[] {{ new PluginUiContribution(context.CultureName == \"ja-JP\" ? 999 : 2, \"card\", PluginUiSlot.CardBody, new PluginUiTextNode(\"旧卡片{version}\")) }}",
                _ => $"new[] {{ {valid} }}",
            };
            // 夹具仅改变候选UI快照，真实生产编译、加载、重载和作用域释放均参与断言。
            var source = $$"""
                using System;
                using System.Collections.Generic;
                using LoomX.Plugins;
                namespace Reload;
                public sealed class Plugin : ILoomXPlugin{{(mode == "provider" ? "" : ", IPluginUiContributionProvider")}}
                {
                    public string Id => "reload.ui";
                    public event EventHandler? UiInvalidated;
                    public void Initialize(PluginInitializationContext context) { }
                    public IEnumerable<IPipelineExtension> CreateExtensions() => Array.Empty<IPipelineExtension>();
                    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) => {{expression}};
                }
                """;
            File.WriteAllText(Path.Combine(Package, "src/Plugin.cs"), source);
        }
    }
}
