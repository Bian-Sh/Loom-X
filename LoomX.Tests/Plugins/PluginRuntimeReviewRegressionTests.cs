using System.Net;
using System.Text;
using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace LoomX.Tests.Plugins;

public sealed class PluginRuntimeReviewRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task assembly发现快照与生产验证清单不同必须在执行前拒绝()
    {
        var fixture = new Fixture();
        fixture.Stage("a", typeof(TriggerPlugin), "1");
        fixture.Stage("b", typeof(ReviewPlugin), "1");
        await using var runtime = fixture.Start((manifest, _) =>
        {
            if (manifest.Id == "a") fixture.Stage("b", typeof(ReviewPlugin), "2", entry: "Review.MissingEntry");
            return new(manifest.Id, manifest.HostCapabilities);
        });
        var disk = await PluginPackageValidation.ValidateAsync(fixture.Package("b"));
        output.WriteLine("生产验证=" + disk.Success + ", 当前包入口=" + disk.Manifest!.PluginType + ", Runtime=" + JsonSerializer.Serialize(runtime.PluginInfos));
        Assert.DoesNotContain(runtime.PluginInfos, item => item.Id == "b");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 候选Extension失败策略不匹配必须拒绝重载并保留旧代际(bool source)
    {
        var fixture = new Fixture();
        fixture.Stage("b", typeof(ReviewPlugin), "1", source: source);
        await using var runtime = fixture.Start();
        Assert.Single(runtime.PluginInfos);
        var before = await runtime.GetPipeline("request")!.ExecuteAsync("原始数据");
        fixture.Stage("b", typeof(ReviewPlugin), "2", policy: "fail-closed", source: source);
        var reloaded = await runtime.ReloadAsync("b");
        output.WriteLine("重载=" + reloaded + ", 旧输出=" + before.Payload + ", 当前信息=" + JsonSerializer.Serialize(runtime.PluginInfos) + ", 请求管线=" + (runtime.GetPipeline("request") is not null));
        Assert.False(reloaded);
        Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal(before, await runtime.GetPipeline("request")!.ExecuteAsync("原始数据"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 入口构造失败应生成安全结构化加载诊断(bool source)
    {
        var fixture = new Fixture();
        fixture.Stage("b", typeof(ConstructorThrowsPlugin), "1", source: source, throws: true);
        var logger = new CaptureLogger();
        await using var runtime = fixture.Start(logger: logger);
        Assert.Empty(runtime.PluginInfos);
        Assert.DoesNotContain("sk-review-private-constructor", string.Join(";", logger.Messages));
        output.WriteLine("结构化诊断=" + JsonSerializer.Serialize(runtime.StructuredDiagnostics) + ", 字符串诊断=" + string.Join(";",runtime.Diagnostics));
        Assert.Contains(runtime.StructuredDiagnostics, diagnostic => diagnostic.Stage == PluginDiagnosticStage.Load && diagnostic.Severity == PluginDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("host_capabilities")]
    [InlineData("extensions")]
    [InlineData("runtime")]
    public async Task 验证快照语义变化在能力工厂调用前拒绝(string field)
    {
        var fixture = new Fixture();
        fixture.Stage("a", typeof(TriggerPlugin), "1");
        fixture.Stage("b", typeof(ReviewPlugin), "1");
        var opened = new List<string>();
        await using var runtime = fixture.Start((manifest, _) =>
        {
            opened.Add(manifest.Id);
            if (manifest.Id == "a")
            {
                var path = Path.Combine(fixture.Package("b"), "plugin.manifest.json");
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
                node[field] = field switch
                {
                    "capabilities" => JsonSerializer.SerializeToNode(new[] { "changed" }),
                    "host_capabilities" => JsonSerializer.SerializeToNode(new[] { "logging/v1" }),
                    "extensions" => JsonSerializer.SerializeToNode(new[] { new { id = "request", kind = "request", pipeline = "changed", failure_policy = "continue-on-error" } }),
                    _ => JsonSerializer.SerializeToNode(new { kind = "assembly", entry_type = "Review.MissingEntry", assembly = "Probe.dll" }),
                };
                if (field == "runtime") { node.AsObject().Remove("assembly"); node.AsObject().Remove("plugin_type"); }
                File.WriteAllText(path, node.ToJsonString());
            }
            return new(manifest.Id, manifest.HostCapabilities);
        });
        Assert.DoesNotContain("b", opened);
        Assert.DoesNotContain(runtime.PluginInfos, item => item.Id == "b");
        Assert.Contains(runtime.StructuredDiagnostics, d => d.PluginId == "b" && d.Stage == PluginDiagnosticStage.Load && d.Severity == PluginDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("duplicate", false)]
    [InlineData("duplicate", true)]
    [InlineData("undeclared", false)]
    [InlineData("undeclared", true)]
    [InlineData("kind", false)]
    [InlineData("kind", true)]
    public async Task 残缺或不匹配候选释放自身且不释放旧租约(string mode, bool source)
    {
        var fixture = new Fixture();
        fixture.Stage("b", typeof(ReviewPlugin), "1", source: source);
        var providers = new List<DisposalProbe>();
        await using var runtime = fixture.Start((manifest, _) => { var provider = new DisposalProbe(); providers.Add(provider); return new(manifest.Id, [], log: provider); });
        await using var lease = runtime.AcquireLease();
        var before = await lease.GetPipeline("request")!.ExecuteAsync("原始数据");
        fixture.Stage("b", typeof(ReviewPlugin), "2", source: source);
        var path = Path.Combine(fixture.Package("b"), "plugin.manifest.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var declarations = node["extensions"]!.AsArray();
        if (mode == "missing") declarations.Add(JsonSerializer.SerializeToNode(new { id = "extra", kind = "request", pipeline = "request", failure_policy = "continue-on-error" }));
        if (mode == "undeclared") declarations.RemoveAt(0);
        if (mode == "kind") { declarations[0]!["kind"] = "response"; declarations[0]!["pipeline"] = "response"; }
        if (mode == "duplicate")
        {
            if (source)
            {
                var codePath = Path.Combine(fixture.Package("b"), "src/Plugin.cs");
                File.WriteAllText(codePath, File.ReadAllText(codePath).Replace("new Request(),new Response()", "new Request(),new Request(),new Response()"));
            }
            else node["plugin_type"] = typeof(DuplicateExtensionPlugin).FullName;
        }
        File.WriteAllText(path, node.ToJsonString());
        Assert.False(await runtime.ReloadAsync("b"));
        Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
        Assert.Equal(before, await runtime.GetPipeline("request")!.ExecuteAsync("原始数据"));
        Assert.Equal(before, await lease.GetPipeline("request")!.ExecuteAsync("原始数据"));
        Assert.Equal(0, providers[0].Disposals);
        Assert.Equal(1, providers[1].Disposals);
        Assert.Contains(runtime.StructuredDiagnostics, d => d.Code == "LX_EXTENSION_INVALID");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 重载构造异常保留具体加载诊断且隐藏敏感异常(bool source)
    {
        var fixture = new Fixture();
        fixture.Stage("b", typeof(ReviewPlugin), "1", source: source);
        var logger = new CaptureLogger();
        await using var runtime = fixture.Start(logger: logger);
        fixture.Stage("b", typeof(ConstructorThrowsPlugin), "2", source: source, throws: true);
        Assert.False(await runtime.ReloadAsync("b"));
        Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
        Assert.Contains(runtime.StructuredDiagnostics, d => d.Code == "LX_LOAD_FAILED" && d.Stage == PluginDiagnosticStage.Load);
        Assert.DoesNotContain("sk-review-private-constructor", JsonSerializer.Serialize(runtime.StructuredDiagnostics));
        Assert.DoesNotContain("sk-review-private-constructor", string.Join(";", logger.Messages));
    }

    [Theory]
    [InlineData("syncEof")]
    [InlineData("asyncEof")]
    [InlineData("syncDispose")]
    [InlineData("asyncDispose")]
    public async Task 旧SSE零长度读不释放且EOF或Dispose仅释放一次(string mode)
    {
        var fixture = new Fixture();
        fixture.Stage("b", typeof(ReviewPlugin), "1");
        var providers = new List<DisposalProbe>();
        await using var runtime = fixture.Start((manifest, _) => { var provider = new DisposalProbe(); providers.Add(provider); return new(manifest.Id, [], log: provider); });
        using var handler = new SseHandler();
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://review.invalid/request") { Content = new StringContent("{}") };
        var executor = new ProviderExecutionPipeline(runtime: runtime);
        var response = await executor.ExecuteStreamingAsync(client, request, new("probe", "probe", "openai", "/probe"), CancellationToken.None);
        fixture.Stage("b", typeof(ReviewPlugin), "2");
        Assert.True(await runtime.ReloadAsync("b"));
        Assert.Equal(0, providers[0].Disposals);
        Assert.Equal(0, response.Body.Read(Array.Empty<byte>(),0,0));
        Assert.Equal(0, await response.Body.ReadAsync(Memory<byte>.Empty));
        Assert.Equal(0, providers[0].Disposals);
        if (mode == "syncEof") { var buffer=new byte[16]; while(response.Body.Read(buffer,0,buffer.Length)>0) {} }
        else if (mode == "asyncEof") { await response.Body.CopyToAsync(Stream.Null); }
        else if (mode == "syncDispose") response.Body.Dispose();
        else await response.Body.DisposeAsync();
        Assert.Equal(1, providers[0].Disposals);
        await response.DisposeAsync();
        Assert.Equal(1, providers[0].Disposals);
        Assert.Equal(0, providers[1].Disposals);
    }

    [Theory]
    [InlineData("Microsoft.Data.Sqlite.dll", false)]
    [InlineData("SQLitePCLRaw.core.dll", false)]
    [InlineData("runtimes/win-x64/native/e_sqlite3.dll", false)]
    [InlineData("Microsoft.Data.Sqlite.dll", true)]
    [InlineData("SQLitePCLRaw.core.dll", true)]
    [InlineData("runtimes/win-x64/native/e_sqlite3.dll", true)]
    public async Task 两种Runtime均在入口前拒绝提供程序夹带(string dependency, bool source)
    {
        var fixture = new Fixture(); fixture.Stage("b", typeof(ReviewPlugin), "1", source: source);
        var destination = Path.Combine(fixture.Package("b"), dependency); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllText(destination,"禁止的提供程序占位");
        await using var runtime = fixture.Start();
        Assert.Empty(runtime.PluginInfos);
        Assert.Contains(runtime.StructuredDiagnostics, d=>d.Code=="LX_DEPENDENCY_FORBIDDEN");
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"data","b")));
    }


    [Fact]
    public async Task 旧SSE实际响应处理与请求保持同代际而新请求切换代际()
    {
        var fixture=new Fixture(); fixture.Stage("b",typeof(ReviewPlugin),"1",source:true,generation:true);
        await using var runtime=fixture.Start();
        using var client=new HttpClient(new GenerationHandler());
        var executor=new ProviderExecutionPipeline(runtime:runtime);
        using var first=new HttpRequestMessage(HttpMethod.Post,"https://review.invalid/first"){Content=new StringContent("{\"request\":\"VERSION\"}")};
        await using var response=await executor.ExecuteStreamingAsync(client,first,new("probe","probe","openai","/first"),CancellationToken.None);
        fixture.Stage("b",typeof(ReviewPlugin),"2",source:true,generation:true);
        Assert.True(await runtime.ReloadAsync("b"));
        using var reader=new StreamReader(response.Body);
        var text=await reader.ReadToEndAsync();
        Assert.Contains("\"request\":\"1\"",text);
        Assert.Contains("\"response\":\"1\"",text);
        using var second=new HttpRequestMessage(HttpMethod.Post,"https://review.invalid/second"){Content=new StringContent("{\"request\":\"VERSION\"}")};
        await using var current=await executor.ExecuteStreamingAsync(client,second,new("probe","probe","openai","/second"),CancellationToken.None);
        using var currentReader=new StreamReader(current.Body);
        var newText=await currentReader.ReadToEndAsync();
        Assert.Contains("\"request\":\"2\"",newText);
        Assert.Contains("\"response\":\"2\"",newText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 展示信息与声明式UI快照操作等待结束才释放旧Bridge(bool display)
    {
        var fixture=new Fixture(); fixture.Stage("b",typeof(SlowUiPlugin),"1");
        var providers=new List<DisposalProbe>();
        await using var runtime=fixture.Start((manifest,_)=>{var p=new DisposalProbe();providers.Add(p);return new(manifest.Id,[],log:p);});
        var operation=Task.Run(()=> { if(display) Assert.NotNull(runtime.GetPluginDisplayInfo("b","zh-CN")); else Assert.Single(runtime.GetUiContributions("b",PluginUiSlot.CardBody,"zh-CN")); });
        var data=Path.Combine(fixture.Root,"data","b");
        for(var index=0;index<200&&!File.Exists(Path.Combine(data,"ui-started"));index++)await Task.Delay(10);
        Assert.True(File.Exists(Path.Combine(data,"ui-started")));
        try { fixture.Stage("b",typeof(SlowUiPlugin),"2"); Assert.True(await runtime.ReloadAsync("b")); Assert.Equal(0,providers[0].Disposals); }
        finally { File.WriteAllText(Path.Combine(data,"ui-continue"),"继续"); }
        await operation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1,providers[0].Disposals);
        Assert.Equal(0,providers[1].Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 损坏依赖清单在首次加载和重载均保留具体安全诊断(bool reload)
    {
        var fixture = new Fixture();
        fixture.Stage("b", typeof(ReviewPlugin), "1");
        var providers = new List<DisposalProbe>();
        var logger = new CaptureLogger();
        PluginRuntime? runtime = null;
        PluginRuntimeLease? lease = null;
        PluginBridgeScope Factory(PluginManifest manifest, string directory)
        {
            var provider = new DisposalProbe();
            providers.Add(provider);
            return new(manifest.Id, [], log: provider);
        }
        try
        {
            if (reload)
            {
                runtime = fixture.Start(Factory, logger);
                lease = runtime.AcquireLease();
                Assert.Single(runtime.PluginInfos);
                fixture.Stage("b", typeof(ReviewPlugin), "2");
            }
            File.WriteAllText(Path.Combine(fixture.Package("b"), "Probe.deps.json"), "{");
            Assert.True((await PluginPackageValidation.ValidateAsync(fixture.Package("b"))).Success);
            if (reload)
            {
                Assert.False(await runtime!.ReloadAsync("b"));
                Assert.Equal("1", Assert.Single(runtime.PluginInfos).Version);
                Assert.Equal(0, providers[0].Disposals);
                Assert.Equal("旧代际-旧处理", (await lease!.GetPipeline("request")!.ExecuteAsync("旧代际")).Payload);
            }
            else
            {
                runtime = fixture.Start(Factory, logger);
                Assert.Empty(runtime.PluginInfos);
            }
            Assert.Contains(runtime!.StructuredDiagnostics, d => d.PluginId == "b"
                && d.Stage == PluginDiagnosticStage.Load && d.Code == "LX_LOAD_FAILED");
            Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(runtime.StructuredDiagnostics));
            Assert.DoesNotContain(fixture.Root, string.Join(";", logger.Messages));
            Assert.Equal(reload ? 1 : 0, providers.Count);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
            if (runtime is not null) await runtime.DisposeAsync();
        }
    }

    private sealed class GenerationHandler:HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var parsed=JsonSerializer.Deserialize<Dictionary<string,string>>(await request.Content!.ReadAsStringAsync(token))!;
            var payload=JsonSerializer.Serialize(new{request=parsed["request"],response="VERSION"});
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: "+payload+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
    private sealed class Fixture
    {
        public string Root {get;} = Path.Combine(Path.GetTempPath(),"loomx-runtime-review-probe-"+Guid.NewGuid().ToString("N"));
        public string Package(string id)=>Path.Combine(Root,"plugins",id);
        public void Stage(string id, Type type, string version, string policy="continue-on-error", string? entry=null, bool source=false, bool throws=false, bool generation=false)
        {
            var directory=Package(id); Directory.CreateDirectory(directory);
            if(!source && !File.Exists(Path.Combine(directory,"Probe.dll"))) File.Copy(typeof(ReviewPlugin).Assembly.Location, Path.Combine(directory,"Probe.dll"));
            if(source)
            {
                Directory.CreateDirectory(Path.Combine(directory,"src"));
                var content=Source.Replace("CTOR",throws ? "public Plugin() { throw new System.InvalidOperationException(\"sk-review-private-constructor\"); }" : "");
                if(generation) content=content.Replace("PipelineResult.Modify(payload+\"-旧处理\")", "PipelineResult.Modify(payload.Replace(\"VERSION\", \""+version+"\"))").Replace("PipelineResult.Pass(payload)", "PipelineResult.Modify(payload.Replace(\"VERSION\", \""+version+"\"))");
                File.WriteAllText(Path.Combine(directory,"src/Plugin.cs"),content);
                Directory.CreateDirectory(Path.Combine(directory,"i18n"));
                foreach(var culture in new[]{"zh-CN","zh-TW","ja-JP","en-US"}) File.WriteAllText(Path.Combine(directory,"i18n",culture+".json"),"{}");
            }
            var document=new Dictionary<string,object?> { ["id"]=id,["version"]=version,["capabilities"]=new[]{"probe"},["extensions"]=new object[]{new{id="request",kind="request",pipeline="request",failure_policy=policy},new{id="response",kind="response",pipeline="response",failure_policy="continue-on-error"}} };
            if(type==typeof(SlowUiPlugin)) document["ui"]=new {contributions=new[]{new{id="panel",slot="card-body",interactive=false}}};
            if(source) document["runtime"]=new{kind="csharp-source",entry_type="Demo.Plugin",sources=new[]{"src/Plugin.cs"}};
            else {document["assembly"]="Probe.dll"; document["plugin_type"]=entry??type.FullName;}
            File.WriteAllText(Path.Combine(directory,"plugin.manifest.json"),JsonSerializer.Serialize(document));
        }
        public PluginRuntime Start(Func<PluginManifest,string,PluginBridgeScope>? factory=null, ILoggerFactory? logger=null)=>PluginRuntime.Start(new(){PluginDirectory=Path.Combine(Root,"plugins"),DataRootDirectory=Path.Combine(Root,"data"),SourceCompiler=new SourcePluginCompiler(Path.Combine(Root,"cache")),BridgeFactory=factory},logger??NullLoggerFactory.Instance);
    }

    private const string Source="""
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using LoomX.Plugins;
    namespace Demo;
    public class Plugin:ILoomXPlugin
    {
        CTOR
        public string Id=>"b";
        public void Initialize(PluginInitializationContext context){}
        public IEnumerable<IPipelineExtension> CreateExtensions()=>new IPipelineExtension[]{new Request(),new Response()};
        public class Request:IRequestExtension
        {
            public string ExtensionId=>"request";
            public ExtensionKind Kind=>ExtensionKind.Request;
            public ExtensionFailurePolicy FailurePolicy=>ExtensionFailurePolicy.ContinueOnError;
            public IReadOnlyList<string> Capabilities=>System.Array.Empty<string>();
            public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context,string payload,CancellationToken cancellationToken=default)=>ValueTask.FromResult(PipelineResult.Modify(payload+"-旧处理"));
        }
        public class Response:IResponseExtension
        {
            public string ExtensionId=>"response";
            public ExtensionKind Kind=>ExtensionKind.Response;
            public ExtensionFailurePolicy FailurePolicy=>ExtensionFailurePolicy.ContinueOnError;
            public IReadOnlyList<string> Capabilities=>System.Array.Empty<string>();
            public ValueTask<PipelineResult> ProcessResponseAsync(PipelineContext context,string payload,CancellationToken cancellationToken=default)=>ValueTask.FromResult(PipelineResult.Pass(payload));
        }
    }
    """;
    private sealed class SseHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: {\"value\":\"完成\"}\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")});
    }
    private sealed class DisposalProbe:IPluginLogBridge,IAsyncDisposable
    {
        public int Disposals{get;private set;}
        public ValueTask WriteAsync(PluginLogEntry entry,CancellationToken cancellationToken=default)=>ValueTask.CompletedTask;
        public ValueTask DisposeAsync(){Disposals++;return ValueTask.CompletedTask;}
    }
    private sealed class CaptureLogger:ILoggerFactory,ILogger
    {
        public List<string> Messages{get;}=[];
        public ILogger CreateLogger(string categoryName)=>this;
        public void AddProvider(ILoggerProvider provider){}
        public void Dispose(){}
        public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,exception)+exception?.ToString());
    }
}
public class ReviewPlugin:ILoomXPlugin
{
    public virtual string Id=>"b";
    public void Initialize(PluginInitializationContext context){}
    public IEnumerable<IPipelineExtension> CreateExtensions()=>[new Request(),new Response()];
    public class Request:IRequestExtension
    {
        public string ExtensionId=>"request";public ExtensionKind Kind=>ExtensionKind.Request;public ExtensionFailurePolicy FailurePolicy=>ExtensionFailurePolicy.ContinueOnError;public IReadOnlyList<string> Capabilities=>[];
        public ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context,string payload,CancellationToken cancellationToken=default)=>ValueTask.FromResult(PipelineResult.Modify(payload+"-旧处理"));
    }
    public class Response:IResponseExtension
    {
        public string ExtensionId=>"response";public ExtensionKind Kind=>ExtensionKind.Response;public ExtensionFailurePolicy FailurePolicy=>ExtensionFailurePolicy.ContinueOnError;public IReadOnlyList<string> Capabilities=>[];
        public ValueTask<PipelineResult> ProcessResponseAsync(PipelineContext context,string payload,CancellationToken cancellationToken=default)=>ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}
public sealed class DuplicateExtensionPlugin:ILoomXPlugin
{
    public string Id => "b";
    public void Initialize(PluginInitializationContext context) { }
    public IEnumerable<IPipelineExtension> CreateExtensions() => [new ReviewPlugin.Request(), new ReviewPlugin.Request(), new ReviewPlugin.Response()];
}
public sealed class TriggerPlugin:ReviewPlugin {public override string Id=>"a";}
public sealed class ConstructorThrowsPlugin:ReviewPlugin { public ConstructorThrowsPlugin()=>throw new InvalidOperationException("sk-review-private-constructor"); }

public sealed class SlowUiPlugin:ILoomXPlugin,IPluginUiContributionProvider
{
    private string directory=null!;
    public string Id=>"b";
    public event EventHandler? UiInvalidated {add{} remove{}}
    public void Initialize(PluginInitializationContext context)=>directory=context.DataDirectory;
    public IEnumerable<IPipelineExtension> CreateExtensions()=>[new ReviewPlugin.Request(),new ReviewPlugin.Response()];
    private void Wait(){File.WriteAllText(Path.Combine(directory,"ui-started"),"开始");if(!SpinWait.SpinUntil(()=>File.Exists(Path.Combine(directory,"ui-continue")),TimeSpan.FromSeconds(10)))throw new TimeoutException("测试UI等待超时");}
    public PluginDisplayInfo? GetDisplayInfo(PluginUiContext context){Wait();return new("演示","说明","测试","生命周期");}
    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context){Wait();return [new(PluginUiContribution.CurrentSchemaVersion,"panel",PluginUiSlot.CardBody,new PluginUiTextNode("演示"))];}
}
