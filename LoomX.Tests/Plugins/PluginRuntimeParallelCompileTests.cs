using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 并行准备/编译与串行结果一致（spec: source-plugin-runtime「插件阶段固定开销必须受控」）：
/// 发现与加载顺序保持确定、诊断按插件归组且顺序与串行一致、Pipeline 顺序不受并行调度影响。
/// </summary>
public sealed class PluginRuntimeParallelCompileTests : IDisposable
{
    private static readonly string[] WarningSource =
    [
        "using System;",
        "using System.Collections.Generic;",
        "using System.Threading;",
        "using System.Threading.Tasks;",
        "using LoomX.Plugins;",
        "namespace Demo;",
        "public sealed class Plugin : ILoomXPlugin",
        "{",
        "    public string Id => \"__ID__\";",
        "    public void Initialize(PluginInitializationContext context) { }",
        "    public IEnumerable<IPipelineExtension> CreateExtensions() => new IPipelineExtension[] { new Pass() };",
        "    private sealed class Pass : IRequestExtension",
        "    {",
        "        public string ExtensionId => \"pass.request\";",
        "        public ExtensionKind Kind => ExtensionKind.Request;",
        "        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;",
        "        public IReadOnlyList<string> Capabilities => Array.Empty<string>();",
        "        public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken cancellationToken)",
        "            => ValueTask.FromResult(PipelineResult.Pass(payload));",
        "    }",
        "}",
        "__EXTRA__",
    ];

    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-plugin-parallel-" + Guid.NewGuid().ToString("N"));

    private string PluginDirectory => Path.Combine(rootDirectory, "plugins");

    public PluginRuntimeParallelCompileTests() => Directory.CreateDirectory(PluginDirectory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 插件程序集仍由测试进程的 ALC 持有（collectible ALC 待 GC），临时目录留给系统清理。
        }
    }

    private void StagePlugin(string pluginId, string? warningMarker = null, string? brokenMarker = null)
    {
        var directory = Path.Combine(PluginDirectory, pluginId);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(directory, "i18n"));
        var extra = brokenMarker ?? (warningMarker is null ? "" : $"public class Probe_{pluginId.Replace('.', '_')} {{ public string Value = null; }}");
        var source = string.Join('\n', WarningSource)
            .Replace("__ID__", pluginId, StringComparison.Ordinal)
            .Replace("__EXTRA__", extra, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(directory, "src", "Plugin.cs"), source, new System.Text.UTF8Encoding(false));
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            File.WriteAllText(Path.Combine(directory, "i18n", culture + ".json"), "{\"title\":\"演示\"}", new System.Text.UTF8Encoding(false));
        var manifest = $$"""
            {
              "id": "{{pluginId}}",
              "version": "1",
              "runtime": { "kind": "csharp-source", "entry_type": "Demo.Plugin", "sources": ["src/Plugin.cs"] },
              "capabilities": [],
              "extensions": [
                { "id": "pass.request", "kind": "request", "pipeline": "request", "failure_policy": "continue-on-error", "capabilities": [] }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(directory, "plugin.manifest.json"), manifest, new System.Text.UTF8Encoding(false));
    }

    private PluginRuntime Start(int parallelism, string cacheRoot) => PluginRuntime.Start(
        new PluginRuntimeOptions
        {
            PluginDirectory = PluginDirectory,
            DataRootDirectory = Path.Combine(rootDirectory, "data"),
            SourceCompiler = new SourcePluginCompiler(cacheRoot),
            CompileParallelism = parallelism,
        },
        NullLoggerFactory.Instance);

    private static object[] Snapshot(PluginRuntime runtime) =>
    [
        runtime.PluginInfos.Select(item => item.Id).ToArray(),
        runtime.Pipelines.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Entries.Select(entry => $"{entry.PluginId}/{entry.Extension.ExtensionId}").ToArray()),
        runtime.StructuredDiagnostics.Select(item => $"{item.PluginId}|{item.Stage}|{item.Severity}|{item.Code}|{item.RelativePath}").ToArray(),
        runtime.Diagnostics.ToArray(),
    ];

    [Fact]
    public void ParallelPreparation_MatchesSerial_OrderDiagnosticsAndPipelines()
    {
        StagePlugin("alpha", warningMarker: "有");
        StagePlugin("beta");
        StagePlugin("gamma", warningMarker: "有");

        var serial = Start(parallelism: 1, Path.Combine(rootDirectory, "cache-serial"));
        var parallel = Start(parallelism: 4, Path.Combine(rootDirectory, "cache-parallel"));

        var serialSnapshot = Snapshot(serial);
        var parallelSnapshot = Snapshot(parallel);
        // 加载顺序与 Pipeline 顺序保持发现顺序，与并行调度无关。
        Assert.True(((string[]?)serialSnapshot[0])!.Length == 3,
            "诊断：" + string.Join("；", (string[]?)serialSnapshot[2]) + "｜" + string.Join("；", (string[]?)serialSnapshot[3]));
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, (string[]?)serialSnapshot[0]);
        Assert.Equal(serialSnapshot[0], parallelSnapshot[0]);
        Assert.Equal(serialSnapshot[1], parallelSnapshot[1]);
        // 诊断按插件归组且顺序一致（alpha 的编译警告在 gamma 之前）。
        Assert.Equal(serialSnapshot[2], parallelSnapshot[2]);
        var diagnostics = (string[]?)serialSnapshot[2];
        Assert.Equal(2, diagnostics!.Length);
        Assert.StartsWith("alpha|Compile|", diagnostics[0], StringComparison.Ordinal);
        Assert.StartsWith("gamma|Compile|", diagnostics[1], StringComparison.Ordinal);
        Assert.Equal(serialSnapshot[3], parallelSnapshot[3]);
    }

    [Fact]
    public void ParallelPreparation_RejectsFailedCompileLikeSerial()
    {
        StagePlugin("alpha", warningMarker: "有");
        StagePlugin("beta", brokenMarker: "class 语法错误 {{");
        StagePlugin("gamma");

        var serial = Start(parallelism: 1, Path.Combine(rootDirectory, "cache-serial"));
        var parallel = Start(parallelism: 4, Path.Combine(rootDirectory, "cache-parallel"));

        var serialSnapshot = Snapshot(serial);
        var parallelSnapshot = Snapshot(parallel);
        // 编译失败的插件被拒绝，其余插件不受影响；结果与串行完全一致。
        Assert.Equal(new[] { "alpha", "gamma" }, (string[]?)serialSnapshot[0]);
        Assert.Equal(serialSnapshot, parallelSnapshot);
        var diagnostics = (string[]?)serialSnapshot[2];
        Assert.Contains(diagnostics!, item => item.StartsWith("beta|Compile|Error|", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics!, item => item.StartsWith("alpha|Compile|Error|", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics!, item => item.StartsWith("gamma|Compile|Error|", StringComparison.Ordinal));
    }

    [Fact]
    public void PipelineOrder_Option_StillAppliesAfterParallelPreparation()
    {
        StagePlugin("alpha");
        StagePlugin("beta");

        var order = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["request"] = ["beta/pass.request", "alpha/pass.request"],
        };
        var runtime = PluginRuntime.Start(
            new PluginRuntimeOptions
            {
                PluginDirectory = PluginDirectory,
                DataRootDirectory = Path.Combine(rootDirectory, "data"),
                SourceCompiler = new SourcePluginCompiler(Path.Combine(rootDirectory, "cache")),
                CompileParallelism = 4,
                PipelineOrder = order,
            },
            NullLoggerFactory.Instance);

        var entries = runtime.Pipelines["request"].Entries.Select(entry => entry.PluginId).ToArray();
        Assert.Equal(new[] { "beta", "alpha" }, entries);
    }
}
