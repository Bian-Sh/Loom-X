using System.Reflection;
using System.Net;
using LoomX.Services;
using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginRuntimeLeaseTests
{
    [Fact]
    public async Task 重载替换当前快照但等待旧租约再释放Bridge()
    {
        var fixture = new Fixture();
        var runtime = fixture.Start();
        var lease = Acquire(runtime);
        var oldPipeline = GetPipeline(lease, "request");
        fixture.Stage("2.0");
        Assert.True(await Reload(runtime));
        Assert.Equal("2.0", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal(0, fixture.Providers[0].Disposals);
        Assert.NotSame(oldPipeline, runtime.GetPipeline("request"));
        await lease.DisposeAsync();
        Assert.Equal(1, fixture.Providers[0].Disposals);
        Assert.Equal(0, fixture.Providers[1].Disposals);
        await Dispose(runtime);
        Assert.Equal(1, fixture.Providers[1].Disposals);
    }

    [Fact]
    public async Task 重载验证失败保留现有实例及Pipeline()
    {
        var fixture = new Fixture();
        var runtime = fixture.Start();
        var pipeline = runtime.GetPipeline("request");
        fixture.Stage("2.0", conflict: true);
        Assert.False(await Reload(runtime));
        Assert.Equal("1.0", Assert.Single(runtime.PluginInfos).Version);
        Assert.Same(pipeline, runtime.GetPipeline("request"));
        Assert.Equal(0, fixture.Providers[0].Disposals);
        Assert.Equal(1, fixture.Providers[1].Disposals);
        await Dispose(runtime);
    }

    [Fact]
    public async Task 启停发布新快照且不会修改已获取租约的Entry状态()
    {
        var fixture = new Fixture();
        var runtime = fixture.Start();
        await using var lease = Acquire(runtime);
        Assert.True(runtime.SetPluginEnabled("test.registration", false));
        Assert.True(GetEntries(GetPipeline(lease, "request")).Single().Enabled);
        Assert.False(runtime.Pipelines["request"].Entries.Single().Enabled);
        await Dispose(runtime);
        Assert.Equal(0, fixture.Providers[0].Disposals);
    }

    [Fact]
    public async Task Provider流式调用保持旧代际直到正文释放且后续调用使用新代际()
    {
        var fixture = new Fixture();
        var runtime = fixture.Start();
        var constructor = typeof(ProviderExecutionPipeline).GetConstructors()
            .SingleOrDefault(item => item.GetParameters().Length == 4 && item.GetParameters()[3].ParameterType == typeof(PluginRuntime));
        Assert.NotNull(constructor);
        var executor = Assert.IsAssignableFrom<IProviderExecutionPipeline>(constructor.Invoke([null, null, null, runtime]));
        using var client = new HttpClient(new StaticHandler());
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.invalid/test") { Content = new StringContent("{}") };
        var result = await executor.ExecuteStreamingAsync(client, request, new("provider", "model", "openai", "/test"), CancellationToken.None);
        fixture.Stage("2.0");
        Assert.True(await Reload(runtime));
        Assert.Equal(0, fixture.Providers[0].Disposals);
        await result.DisposeAsync();
        Assert.Equal(1, fixture.Providers[0].Disposals);
        using var next = new HttpRequestMessage(HttpMethod.Post, "https://provider.invalid/test") { Content = new StringContent("{}") };
        await using var current = await executor.ExecuteStreamingAsync(client, next, new("provider", "model", "openai", "/test"), CancellationToken.None);
        Assert.Equal(0, fixture.Providers[1].Disposals);
        await Dispose(runtime);
        Assert.Equal(0, fixture.Providers[1].Disposals);
        await current.DisposeAsync();
        Assert.Equal(1, fixture.Providers[1].Disposals);
    }

    [Fact]
    public async Task 在途UI命令保持旧实例直到命令结束()
    {
        var fixture = new Fixture(typeof(LeaseUiProbePlugin));
        var runtime = fixture.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var command = runtime.ExecuteUiCommandAsync("test.registration", PluginUiCommand.Create("panel", "wait"), cancellation.Token);
        for (var index = 0; index < 200 && !File.Exists(Path.Combine(fixture.DataDirectory, "command-started")); index++)
            await Task.Delay(5);
        Assert.True(File.Exists(Path.Combine(fixture.DataDirectory, "command-started")));
        try
        {
            fixture.Stage("2.0");
            Assert.True(await Reload(runtime));
            Assert.Equal(0, fixture.Providers[0].Disposals);
        }
        finally { File.WriteAllText(Path.Combine(fixture.DataDirectory, "command-continue"), "继续"); }
        Assert.True((await command).Handled);
        Assert.Equal(1, fixture.Providers[0].Disposals);
        await Dispose(runtime);
    }
    [Fact]
    public async Task 重载通知观察者失败不改变已提交结果并继续通知健康观察者()
    {
        var fixture = new Fixture();
        var runtime = fixture.Start();
        var delivered = 0;
        runtime.PluginUiInvalidated += (_, _) => throw new InvalidOperationException("观察者故障");
        runtime.PluginUiInvalidated += (_, _) => delivered++;
        fixture.Stage("2.0");
        Assert.True(await Reload(runtime));
        Assert.Equal("2.0", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal(1, delivered);
        await Dispose(runtime);
    }

    [Fact]
    public async Task 候选注册失败且UI取消订阅抛错仍释放实例与Bridge()
    {
        var fixture = new Fixture(typeof(FailingUnsubscribeProbePlugin));
        var runtime = fixture.Start();
        fixture.Stage("2.0", conflict: true);
        Assert.False(await Reload(runtime));
        Assert.Equal("1.0", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal(1, fixture.Providers[1].Disposals);
        Assert.True(File.Exists(Path.Combine(fixture.DataDirectory, "candidate-disposed")));
        await Dispose(runtime);
    }

    [Fact]
    public async Task 旧ALC卸载异常只生成安全诊断且不回退新实例()
    {
        var fixture = new Fixture();
        var runtime = fixture.Start();
        var assembly = runtime.Pipelines["request"].Entries.Single().Extension.GetType().Assembly;
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly)!;
        const string sentinel = "sk-unload-private-sentinel-0123456789";
        context.Unloading += _ => throw new InvalidOperationException(sentinel);
        fixture.Stage("2.0");
        Assert.True(await Reload(runtime));
        Assert.Equal("2.0", Assert.Single(runtime.PluginInfos).Version);
        var diagnostic = Assert.Single(runtime.StructuredDiagnostics, item => item.Code == "LX_ALC_UNLOAD_FAILED");
        Assert.Equal(PluginDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain(sentinel, diagnostic.Message);
        await Dispose(runtime);
    }
    private sealed class StaticHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("data: {\"value\":\"完成\"}\n\ndata: [DONE]\n\n", System.Text.Encoding.UTF8, "text/event-stream") });
    }
    private static IReadOnlyList<PipelineEntry> GetEntries(IPipeline pipeline) => Assert.IsType<Pipeline>(pipeline).Entries;
    private static IAsyncDisposable Acquire(PluginRuntime runtime)
    {
        var method = typeof(PluginRuntime).GetMethod("AcquireLease");
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IAsyncDisposable>(method.Invoke(runtime, null));
    }
    private static IPipeline GetPipeline(IAsyncDisposable lease, string id) => Assert.IsAssignableFrom<IPipeline>(
        lease.GetType().GetMethod("GetPipeline")!.Invoke(lease, [id]));
    private static async Task<bool> Reload(PluginRuntime runtime)
    {
        var method = typeof(PluginRuntime).GetMethod("ReloadAsync");
        Assert.NotNull(method);
        return await Assert.IsAssignableFrom<Task<bool>>(method.Invoke(runtime, ["test.registration", CancellationToken.None]));
    }
    private static ValueTask Dispose(PluginRuntime runtime) => Assert.IsAssignableFrom<IAsyncDisposable>(runtime).DisposeAsync();

    private sealed class Fixture(Type? pluginType = null)
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "loomx-lease-" + Guid.NewGuid().ToString("N"));
        public List<Provider> Providers { get; } = [];
        public string DataDirectory => Path.Combine(root, "data", "test.registration");
        public void Stage(string version, bool conflict = false)
        {
            var directory = Path.Combine(root, "plugins", "test.registration");
            Directory.CreateDirectory(directory);
            if (!File.Exists(Path.Combine(directory, "Probe.dll")))
                File.Copy(typeof(RegistrationProbePlugin).Assembly.Location, Path.Combine(directory, "Probe.dll"));
            File.WriteAllText(Path.Combine(directory, "plugin.manifest.json"), JsonSerializer.Serialize(new
            {
                id = "test.registration", version, assembly = "Probe.dll", plugin_type = (pluginType ?? typeof(RegistrationProbePlugin)).FullName,
                ui = pluginType is null ? null : new { contributions = new[] { new { id = "panel", slot = "detail-body", interactive = true } } },
                capabilities = new[] { "测试" }, extensions = new object[]
                {
                    new { id = "request", kind = "request", pipeline = "request", failure_policy = "continue-on-error" },
                    new { id = "response", kind = "response", pipeline = conflict ? "request" : "response", failure_policy = "continue-on-error" },
                },
            }));
        }
        public PluginRuntime Start()
        {
            Stage("1.0");
            return PluginRuntime.Start(new()
            {
                PluginDirectory = Path.Combine(root, "plugins"), DataRootDirectory = Path.Combine(root, "data"),
                BridgeFactory = (manifest, _) => { var provider = new Provider(); Providers.Add(provider); return new(manifest.Id, [], log: provider); },
            }, NullLoggerFactory.Instance);
        }
    }
    public sealed class Provider : IPluginLogBridge, IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
public sealed class LeaseUiProbePlugin : ILoomXPlugin, IPluginUiCommandHandler, IPluginUiContributionProvider, IDisposable
{
    private readonly RegistrationProbePlugin inner = new();
    private string directory = null!;
    public string Id => inner.Id;
    public void Initialize(PluginInitializationContext context) { directory = context.DataDirectory; inner.Initialize(context); }
    public IEnumerable<IPipelineExtension> CreateExtensions() => inner.CreateExtensions();
    // 命令租约夹具也必须提供与Manifest一致的有效声明式UI候选。
    public event EventHandler? UiInvalidated { add { } remove { } }
    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) =>
        [new(PluginUiContribution.CurrentSchemaVersion, "panel", PluginUiSlot.DetailBody, new PluginUiButtonNode("等待", "wait"))];
    public async ValueTask<PluginUiCommandResult> ExecuteUiCommandAsync(PluginUiCommand command, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(Path.Combine(directory, "command-started"), "开始");
        while (!File.Exists(Path.Combine(directory, "command-continue"))) await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        return PluginUiCommandResult.Success("完成");
    }
    public void Dispose() => inner.Dispose();
}
public sealed class FailingUnsubscribeProbePlugin : ILoomXPlugin, IPluginUiContributionProvider, IDisposable
{
    private readonly RegistrationProbePlugin inner = new();
    private string directory = null!;
    public string Id => inner.Id;
    public event EventHandler? UiInvalidated { add { } remove => throw new InvalidOperationException("取消订阅故障"); }
    public void Initialize(PluginInitializationContext context) { directory = context.DataDirectory; inner.Initialize(context); }
    public IEnumerable<IPipelineExtension> CreateExtensions() => inner.CreateExtensions();
    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) => [];
    public void Dispose() { File.WriteAllText(Path.Combine(directory, "candidate-disposed"), "已释放"); inner.Dispose(); }
}
