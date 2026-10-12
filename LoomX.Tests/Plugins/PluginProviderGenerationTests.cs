using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginProviderGenerationTests
{
    [Fact]
    public async Task reload与启停并发不拆散在途Provider的实际请求响应代际()
    {
        var package = new SourcePluginTestPackage();
        Stage(package, "1");
        await using var runtime = PluginRuntime.Start(new()
        {
            PluginDirectory = package.Root, DataRootDirectory = Path.Combine(package.Root, "data"), SourceCompiler = package.Compiler,
        }, NullLoggerFactory.Instance);
        Assert.Single(runtime.PluginInfos);
        var executor = new ProviderExecutionPipeline(runtime: runtime);
        using var handler = new EchoHandler();
        using var client = new HttpClient(handler);
        using var first = Request();
        var operation = executor.ExecuteAsync(client, first, new("probe", "probe", "openai", "/probe"), CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Stage(package, "2");
            var reload = runtime.ReloadAsync("demo");
            var toggles = Task.Run(() =>
            {
                for (var index = 0; index < 20; index++) runtime.SetPluginEnabled("demo", index % 2 == 0);
            });
            Assert.True(await reload);
            await toggles;
            Assert.True(runtime.SetPluginEnabled("demo", true));
        }
        finally { handler.Continue.TrySetResult(); }
        var result = JsonNode.Parse((await operation).Body)!;
        Assert.Equal("1", result["request"]!.GetValue<string>());
        Assert.Equal("1", result["response"]!.GetValue<string>());
        using var second = Request();
        var current = JsonNode.Parse((await executor.ExecuteAsync(client, second, new("probe", "probe", "openai", "/probe"), CancellationToken.None)).Body)!;
        Assert.Equal("2", current["request"]!.GetValue<string>());
        Assert.Equal("2", current["response"]!.GetValue<string>());
    }

    [Fact]
    public async Task 同功能源码与预编译入口使用相同契约及Pipeline行为()
    {
        var package = new SourcePluginTestPackage();
        Stage(package, "1");
        var options = new PluginRuntimeOptions { PluginDirectory = package.Root, DataRootDirectory = Path.Combine(package.Root, "data"), SourceCompiler = package.Compiler };
        await using var source = PluginRuntime.Start(options, NullLoggerFactory.Instance);
        var input = "{}";
        var sourceResult = await source.GetPipeline("request")!.ExecuteAsync(input);
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Directory, "plugin.manifest.json")))!;
        var errors = new List<string>();
        var manifest = ManifestParser.Parse(document.ToJsonString(), errors)!;
        var compiled = await package.Compiler.CompileAsync(package.Directory, manifest);
        Assert.True(compiled.Success);
        File.Copy(compiled.AssemblyPath!, Path.Combine(package.Directory, "Plugin.dll"));
        document["runtime"] = new JsonObject { ["kind"] = "assembly", ["entry_type"] = "Demo.Plugin", ["assembly"] = "Plugin.dll" };
        package.Write("plugin.manifest.json", document.ToJsonString());
        await using var assembly = PluginRuntime.Start(options, NullLoggerFactory.Instance);
        Assert.True(assembly.PluginInfos.Count == 1, string.Join(";", assembly.Diagnostics.Concat(assembly.StructuredDiagnostics.Select(item => item.Code + ":" + item.Message))));
        Assert.Equal("assembly", assembly.PluginInfos[0].RuntimeKind);
        Assert.Equal(sourceResult, await assembly.GetPipeline("request")!.ExecuteAsync(input));
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "https://probe.invalid/request") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    private static void Stage(SourcePluginTestPackage package, string version)
    {
        package.Write("src/Plugin.cs", Source.Replace("VERSION", version, StringComparison.Ordinal));
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Directory, "plugin.manifest.json")))!;
        document["version"] = version;
        document["capabilities"] = new JsonArray("probe");
        document["extensions"] = new JsonArray(
            new JsonObject { ["id"] = "request", ["kind"] = "request", ["pipeline"] = "request", ["failure_policy"] = "fail-closed" },
            new JsonObject { ["id"] = "response", ["kind"] = "response", ["pipeline"] = "response", ["failure_policy"] = "fail-closed" });
        package.Write("plugin.manifest.json", document.ToJsonString());
    }
    private sealed class EchoHandler : HttpMessageHandler
    {
        private int calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Interlocked.Increment(ref calls) == 1)
            {
                Started.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
    private const string Source = """
        using System.Collections.Generic;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using LoomX.Plugins;
        namespace Demo;
        public sealed class Plugin : ILoomXPlugin
        {
            public string Id => "demo";
            public void Initialize(PluginInitializationContext context) { }
            public IEnumerable<IPipelineExtension> CreateExtensions() => new IPipelineExtension[] { new Extension("request", ExtensionKind.Request), new Extension("response", ExtensionKind.Response) };
            private sealed class Extension(string id, ExtensionKind kind) : IRequestExtension, IResponseExtension
            {
                public string ExtensionId => id;
                public ExtensionKind Kind => kind;
                public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
                public IReadOnlyList<string> Capabilities => System.Array.Empty<string>();
                private ValueTask<PipelineResult> Process(string payload)
                {
                    var document = JsonNode.Parse(payload)!;
                    document[id] = "VERSION";
                    return ValueTask.FromResult(PipelineResult.Modify(document.ToJsonString()));
                }
                public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken cancellationToken) => Process(payload);
                public ValueTask<PipelineResult> ProcessResponseAsync(PipelineContext context, string payload, CancellationToken cancellationToken) => Process(payload);
            }
        }
        """;
}