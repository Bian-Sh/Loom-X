using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginCapabilityRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "loomx-bridge-runtime-" + Guid.NewGuid().ToString("N"));
    private readonly string? originalProbe = Environment.GetEnvironmentVariable("LOOMX_PLUGIN_CAPABILITY_PROBE");

    [Fact]
    public void 不兼容能力在执行构造函数之前拒绝()
    {
        Stage("storage.sqlite/v2");
        var marker = Path.Combine(root, "constructor-probe");
        Environment.SetEnvironmentVariable("LOOMX_PLUGIN_CAPABILITY_PROBE", marker);
        var runtime = PluginRuntime.Start(Options(), NullLoggerFactory.Instance);
        Assert.Empty(runtime.PluginInfos);
        Assert.False(File.Exists(marker));
        Assert.Contains(runtime.Diagnostics, item => item.Contains("LX_CAPABILITY_UNSUPPORTED", StringComparison.Ordinal));
    }

    [Fact]
    public void 合法插件初始化收到按声明授权的Bridge()
    {
        Stage("logging/v1");
        var options = Options();
        var factory = typeof(PluginRuntimeOptions).GetProperty("BridgeFactory");
        Assert.NotNull(factory);
        var log = new RecordingLog();
        factory.SetValue(options, (Func<PluginManifest, string, PluginBridgeScope>)((manifest, _) =>
            new(manifest.Id, ["logging/v1"], log: log)));
        var runtime = PluginRuntime.Start(options, NullLoggerFactory.Instance);
        Assert.Single(runtime.PluginInfos);
        Assert.Empty(runtime.Diagnostics);
        Assert.Equal(1, log.Calls);
        Assert.True(File.Exists(Path.Combine(root, "data", "test.bridge", "initialized")));
    }

    [Fact]
    public void 组合根错误授予未声明能力时不执行初始化()
    {
        Stage(null);
        var options = Options();
        var log = new RecordingLog();
        typeof(PluginRuntimeOptions).GetProperty("BridgeFactory")!.SetValue(options,
            (Func<PluginManifest, string, PluginBridgeScope>)((manifest, _) =>
                new(manifest.Id, ["logging/v1"], log: log)));
        var runtime = PluginRuntime.Start(options, NullLoggerFactory.Instance);
        Assert.Empty(runtime.PluginInfos);
        Assert.Equal(0, log.Calls);
    }
    [Fact]
    public void 必需能力缺少实际适配器时不执行插件构造函数()
    {
        Stage("logging/v1");
        var marker = Path.Combine(root, "missing-adapter-probe");
        Environment.SetEnvironmentVariable("LOOMX_PLUGIN_CAPABILITY_PROBE", marker);
        var runtime = PluginRuntime.Start(Options(), NullLoggerFactory.Instance);
        Assert.Empty(runtime.PluginInfos);
        Assert.False(File.Exists(marker));
        Assert.Contains(runtime.StructuredDiagnostics, item => item.Code == PluginDiagnosticCodes.CapabilityNotSupported);
    }

    private PluginRuntimeOptions Options() => new()
    {
        PluginDirectory = Path.Combine(root, "plugins"), DataRootDirectory = Path.Combine(root, "data"),
    };

    private void Stage(string? capability)
    {
        var directory = Path.Combine(root, "plugins", "test.bridge");
        Directory.CreateDirectory(directory);
        var assembly = typeof(CapabilityProbePlugin).Assembly;
        File.Copy(assembly.Location, Path.Combine(directory, "TestPlugin.dll"));
        File.WriteAllText(Path.Combine(directory, ManifestParser.ManifestFileName), JsonSerializer.Serialize(new
        {
            id = "test.bridge", version = "1.0.0", assembly = "TestPlugin.dll",
            plugin_type = typeof(CapabilityProbePlugin).FullName, capabilities = new[] { "测试" },
            host_capabilities = capability is null ? Array.Empty<string>() : new[] { capability },
            extensions = new[] { new { id = "probe.request", kind = "request", pipeline = "request", failure_policy = "continue-on-error" } },
        }));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOOMX_PLUGIN_CAPABILITY_PROBE", originalProbe);
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class RecordingLog : IPluginLogBridge
    {
        public int Calls { get; private set; }
        public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.CompletedTask; }
    }
}

public sealed class CapabilityProbePlugin : ILoomXPlugin
{
    public CapabilityProbePlugin()
    {
        var marker = Environment.GetEnvironmentVariable("LOOMX_PLUGIN_CAPABILITY_PROBE");
        if (marker is not null) File.WriteAllText(marker, "构造函数已执行");
    }
    public string Id => "test.bridge";
    public void Initialize(PluginInitializationContext context)
    {
        context.Bridge.Log.WriteAsync(new(PluginLogLevel.Information, "插件初始化完成")).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(context.DataDirectory, "initialized"), "完成");
    }
    public IEnumerable<IPipelineExtension> CreateExtensions() => [new ProbeExtension()];
    private sealed class ProbeExtension : IRequestExtension
    {
        public string ExtensionId => "probe.request";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => [];
        public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}
