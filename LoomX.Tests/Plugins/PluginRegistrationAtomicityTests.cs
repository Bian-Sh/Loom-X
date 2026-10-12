using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginRegistrationAtomicityTests
{
    [Fact]
    public void 注册第二项失败后不保留候选Entry且资源只释放一次()
    {
        var root = Path.Combine(Path.GetTempPath(), "loomx-registration-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "plugins", "test.registration");
        Directory.CreateDirectory(directory);
        File.Copy(typeof(RegistrationProbePlugin).Assembly.Location, Path.Combine(directory, "Probe.dll"));
        File.WriteAllText(Path.Combine(directory, "plugin.manifest.json"), JsonSerializer.Serialize(new
        {
            id = "test.registration", version = "1.0", assembly = "Probe.dll", plugin_type = typeof(RegistrationProbePlugin).FullName,
            capabilities = new[] { "测试" }, extensions = new object[]
            {
                new { id = "request", kind = "request", pipeline = "conflict", failure_policy = "continue-on-error" },
                new { id = "response", kind = "response", pipeline = "conflict", failure_policy = "continue-on-error" },
            },
        }));
        var provider = new DisposalProbe();
        var runtime = PluginRuntime.Start(new()
        {
            PluginDirectory = Path.Combine(root, "plugins"), DataRootDirectory = Path.Combine(root, "data"),
            BridgeFactory = (manifest, _) => new(manifest.Id, [], log: provider),
        }, NullLoggerFactory.Instance);
        Assert.Empty(runtime.PluginInfos);
        Assert.DoesNotContain(runtime.Pipelines.Values.SelectMany(pipeline => pipeline.Entries), entry => entry.PluginId == "test.registration");
        Assert.Equal(1, provider.Disposals);
        Assert.Equal("1", File.ReadAllText(Path.Combine(root, "data", "test.registration", "disposed")));
        // ALC 的程序集可能仍由 GC 持有，本测试不递归清理文件现场。
    }

    private sealed class DisposalProbe : IPluginLogBridge, IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}

public sealed class RegistrationProbePlugin : ILoomXPlugin, IDisposable
{
    private string? directory;
    private int disposals;
    public string Id => "test.registration";
    public void Initialize(PluginInitializationContext context) => directory = context.DataDirectory;
    public IEnumerable<IPipelineExtension> CreateExtensions() => [new Request(), new Response()];
    public void Dispose() => File.WriteAllText(Path.Combine(directory!, "disposed"), (++disposals).ToString());
    private sealed class Request : IRequestExtension
    {
        public string ExtensionId => "request";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => [];
        public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken cancellationToken = default) => ValueTask.FromResult(PipelineResult.Pass(payload));
    }
    private sealed class Response : IResponseExtension
    {
        public string ExtensionId => "response";
        public ExtensionKind Kind => ExtensionKind.Response;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => [];
        public ValueTask<PipelineResult> ProcessResponseAsync(PipelineContext context, string payload, CancellationToken cancellationToken = default) => ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}