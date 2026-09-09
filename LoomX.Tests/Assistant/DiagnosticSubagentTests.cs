using Xunit;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

/// <summary>
/// Diagnostic Subagent 测试：受限工具集、结构化结论解析、事件与步数上限。
/// </summary>
public sealed class DiagnosticSubagentTests : IAsyncLifetime
{
    private string databasePath = string.Empty;
    private ConfigurationDbContext startupContext = null!;
    private ConfigurationManagementService configuration = null!;
    private AssistantTester tester = null!;
    private DiagnosticSubagent subagent = null!;

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"loomx-diag-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        await using (var context = new ConfigurationDbContext(options))
        {
            await ConfigurationDatabase.InitializeAsync(context);
        }

        startupContext = new ConfigurationDbContext(options);
        var configurationProvider = new DatabaseConfigurationProvider(startupContext);
        await configurationProvider.ReloadAsync();
        var factory = new TestDbContextFactory(options);
        configuration = new ConfigurationManagementService(factory, configurationProvider);
        tester = new AssistantTester(new HttpClient(new DelegateHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"gpt-4o"}]}""", Encoding.UTF8, "application/json"),
        })), configuration, factory);
        subagent = new DiagnosticSubagent(
            tester,
            new NetworkProbe(NullLogger<NetworkProbe>.Instance),
            configuration,
            NullLoggerFactory.Instance);

        // 准备一个被诊断对象
        await configuration.CreateProviderAsync(new ProviderInput(
            "diag-target", "诊断目标", "https://relay.example.com/v1", "openai", true,
            "sk-test-secret-12345", false, null, false, null, "responses"), CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await startupContext.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Diagnose_ParsesStructuredReport()
    {
        var model = new ScriptedModelClient(
            // 第 1 步：查状态
            [new ModelToolCallEvent(new ToolCall("c1", "loomx.get_status", "{}")), new ModelCompletedEvent("tool_calls")],
            // 第 2 步：测试 provider
            [new ModelToolCallEvent(new ToolCall("c2", "loomx.test_provider", """{"id":"diag-target"}""")), new ModelCompletedEvent("tool_calls")],
            // 第 3 步：产出结论
            [new TextDeltaEvent("""{"reachable":true,"authenticated":true,"models_found":1,"chat_test":false,"proxy_required":false,"diagnosis":"provider_ok","summary":"中转站连通且鉴权正常。"}"""),
             new ModelCompletedEvent("stop")]);

        var events = new List<AgentEvent>();
        subagent.SubagentEvent += events.Add;

        var report = await subagent.DiagnoseProviderAsync(model, "diag-target", CancellationToken.None);

        Assert.True(report.Reachable);
        Assert.True(report.Authenticated);
        Assert.Equal(1, report.ModelsFound);
        Assert.False(report.ChatTest);
        Assert.Equal("provider_ok", report.Diagnosis);
        Assert.Equal("中转站连通且鉴权正常。", report.Summary);
        Assert.Contains(events, item => item.Kind == AgentEventKind.SubagentStarted);
        Assert.Contains(events, item => item.Kind == AgentEventKind.SubagentCompleted && item.Success == true);
    }

    [Fact]
    public async Task Diagnose_RestrictedRegistry_OnlyReadAndTestTools()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("""{"reachable":false,"authenticated":false,"models_found":0,"chat_test":false,"proxy_required":false,"diagnosis":"unknown","summary":"x"}"""),
             new ModelCompletedEvent("stop")]);

        await subagent.DiagnoseProviderAsync(model, "diag-target", CancellationToken.None);

        var toolNames = model.Requests[0].Tools.Select(tool => tool.Name).ToArray();
        Assert.Contains("loomx.get_provider", toolNames);
        Assert.Contains("loomx.test_provider", toolNames);
        Assert.Contains("diag.dns", toolNames);
        Assert.Contains("diag.tcp", toolNames);
        Assert.Contains("diag.tls", toolNames);
        // 写/删除/浏览器/嵌套诊断工具一律不得出现
        Assert.DoesNotContain(toolNames, name => name.StartsWith("browser.", StringComparison.Ordinal));
        Assert.DoesNotContain("loomx.create_provider", toolNames);
        Assert.DoesNotContain("loomx.delete_provider", toolNames);
        Assert.DoesNotContain("loomx.update_provider", toolNames);
        Assert.DoesNotContain("loomx.diagnose", toolNames);
    }

    [Fact]
    public async Task Diagnose_NonJsonAnswer_ReturnsParseFailed()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("我看这个 Provider 没啥问题。"), new ModelCompletedEvent("stop")]);

        var report = await subagent.DiagnoseProviderAsync(model, "diag-target", CancellationToken.None);

        Assert.Equal("diagnostic_parse_failed", report.Diagnosis);
        Assert.False(report.Reachable);
    }

    [Fact]
    public async Task Diagnose_AnswerWrappedInProse_StillParses()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("""结论如下：{"reachable":true,"authenticated":false,"models_found":0,"chat_test":false,"proxy_required":false,"diagnosis":"auth_failed","summary":"Key 无效。"} 以上。"""),
             new ModelCompletedEvent("stop")]);

        var report = await subagent.DiagnoseProviderAsync(model, "diag-target", CancellationToken.None);

        Assert.Equal("auth_failed", report.Diagnosis);
        Assert.True(report.Reachable);
        Assert.False(report.Authenticated);
    }

    [Fact]
    public async Task Diagnose_ExceedsMaxSteps_ReturnsLoopFailed()
    {
        // 10 步剧本全部发起工具调用，触发最大步数上限
        var turns = Enumerable.Range(0, 10)
            .Select(index => (IReadOnlyList<ModelStreamEvent>)[
                new ModelToolCallEvent(new ToolCall($"c{index}", "loomx.get_status", "{}")),
                new ModelCompletedEvent("tool_calls")])
            .ToArray();
        var model = new ScriptedModelClient(turns);

        var events = new List<AgentEvent>();
        subagent.SubagentEvent += events.Add;

        var report = await subagent.DiagnoseProviderAsync(model, "diag-target", CancellationToken.None);

        Assert.Equal("diagnostic_loop_failed", report.Diagnosis);
        Assert.Contains(events, item => item.Kind == AgentEventKind.SubagentCompleted && item.Success == false);
    }

    [Fact]
    public async Task DiagnoseTool_NoModelClient_ReturnsClearError()
    {
        var registry = new ToolRegistry();
        LoomXTools.RegisterDiagnosticTool(registry, subagent, () => null);

        var tool = registry.All.Single(item => item.Name == "loomx.diagnose");
        var result = await tool.Handler(JsonNode.Parse("""{"id":"diag-target"}"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("assistant_model_not_configured", result.Content);
    }

    [Fact]
    public async Task DiagnoseTool_WithModelClient_ReturnsReport()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("""{"reachable":true,"authenticated":true,"models_found":1,"chat_test":true,"proxy_required":false,"diagnosis":"provider_ok","summary":"正常。"}"""),
             new ModelCompletedEvent("stop")]);
        var registry = new ToolRegistry();
        LoomXTools.RegisterDiagnosticTool(registry, subagent, () => model);

        var tool = registry.All.Single(item => item.Name == "loomx.diagnose");
        var result = await tool.Handler(JsonNode.Parse("""{"id":"diag-target"}"""), CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonNode.Parse(result.Content)!;
        Assert.Equal("provider_ok", json["diagnosis"]!.GetValue<string>());
        Assert.True(json["chat_test"]!.GetValue<bool>());
    }
}
