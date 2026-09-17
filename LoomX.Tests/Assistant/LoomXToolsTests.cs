using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.Browser;
using LoomX.Configuration;
using LoomX.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class LoomXToolsTests : IAsyncLifetime
{
    internal const string PlaintextApiKey = "sk-test-secret-12345";

    private string databasePath = string.Empty;
    private string skillsDirectory = string.Empty;
    private ConfigurationDbContext startupContext = null!;
    private ConfigurationManagementService configuration = null!;
    private ToolRegistry registry = null!;
    private GatewayStateHub gatewayStateHub = null!;
    private BrowserSecretVault secretVault = null!;

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"loomx-tools-{Guid.NewGuid():N}.db");
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

        var tester = new AssistantTester(new HttpClient(new DelegateHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"gpt-4o"}]}""", Encoding.UTF8, "application/json"),
        })), configuration, factory);

        skillsDirectory = Path.Combine(Path.GetTempPath(), $"loomx-skills-{Guid.NewGuid():N}");
        var skillDirectory = Path.Combine(skillsDirectory, "providers", "test-skill");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "manifest.json"),
            """{"name":"test-skill","description":"测试 Skill","when_to_use":"测试时"}""");
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "# 测试 Skill 内容");

        registry = new ToolRegistry();
        gatewayStateHub = new GatewayStateHub();
        secretVault = new BrowserSecretVault();
        LoomXTools.RegisterAll(registry, configuration, configurationProvider, tester, new SkillStore(skillsDirectory), secretVault, gatewayStateHub);
    }

    public async Task DisposeAsync()
    {
        await startupContext.DisposeAsync();
        TryDelete(databasePath);
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        try { if (Directory.Exists(skillsDirectory)) Directory.Delete(skillsDirectory, recursive: true); } catch (IOException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    internal async Task<ToolResult> InvokeAsync(string toolName, string? argumentsJson = null)
    {
        var tool = registry.All.FirstOrDefault(item => item.Name == toolName)
            ?? throw new InvalidOperationException($"工具 '{toolName}' 未注册。");
        return await tool.Handler(argumentsJson is null ? null : JsonNode.Parse(argumentsJson), CancellationToken.None);
    }

    internal async Task<string> CreateProviderAsync()
    {
        var provider = await configuration.CreateProviderAsync(
            new ProviderInput("demo", "演示 Provider", "https://api.example.com/v1", "openai", true, PlaintextApiKey, false, null),
            CancellationToken.None);
        return provider.Id.ToString();
    }

    [Fact]
    public async Task CreateProvider_支持浏览器SecretRef并在Schema中公开参数()
    {
        var tool = Assert.Single(registry.All, item => item.Name == "loomx.create_provider");
        Assert.NotNull(tool.ParametersSchema["properties"]?["api_key_secret_ref"]);

        var secretRef = secretVault.Store(PlaintextApiKey, "api_key");
        var result = await InvokeAsync(
            "loomx.create_provider",
            $$"""{"business_id":"browser-secret","display_name":"浏览器 Secret","base_url":"https://api.example.com/v1","api_mode":"openai","api_key_secret_ref":"{{secretRef}}"}""");

        Assert.True(result.Success, result.Content);
        Assert.DoesNotContain(PlaintextApiKey, result.Content);
        var provider = await InvokeAsync("loomx.get_provider", """{"id":"browser-secret"}""");
        Assert.True(provider.Success, provider.Content);
        Assert.Contains("\"configured\":true", provider.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void GatewayProcessService_不直接控制BrowserBridge生命周期()
    {
        var source = ReadRepositoryFile("LoomX", "Services", "GatewayProcessService.cs");

        Assert.DoesNotContain("BrowserBridge>().Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BrowserBridge>().Stop", source, StringComparison.Ordinal);
        Assert.Contains("browserBridge ??= new", source, StringComparison.Ordinal);
        Assert.Contains("browserBridgeLeaseManager ??= new", source, StringComparison.Ordinal);
        Assert.Contains("LoomXHost.CreateAsync(browserBridge, browserBridgeLeaseManager", source, StringComparison.Ordinal);
        Assert.Contains("browserBridge?.Dispose();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserExtension_具备Alarm保活目标恢复与纯传输契约()
    {
        var source = ReadRepositoryFile("LoomX", "BrowserExtension", "background.js");
        var manifest = ReadRepositoryFile("LoomX", "BrowserExtension", "manifest.json");

        Assert.Contains("const KEEPALIVE_INTERVAL_MS = 20000;", source, StringComparison.Ordinal);
        Assert.Contains("const BRIDGE_HEALTH_URL = \"http://127.0.0.1:17831/loomx-browser/\";", source, StringComparison.Ordinal);
        Assert.Contains("async function probeBridge()", source, StringComparison.Ordinal);
        Assert.Contains("if (!(await probeBridge()))", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (!(await probeBridge()))", StringComparison.Ordinal)
            < source.IndexOf("new WebSocket(BRIDGE_URL)", StringComparison.Ordinal),
            "Extension 必须先探测 Bridge，再创建 WebSocket，避免离线时产生连接拒绝错误。");
        Assert.Contains("Bridge.keepAlive", source, StringComparison.Ordinal);
        Assert.Contains("chrome.alarms", source, StringComparison.Ordinal);
        Assert.Contains("targets: currentTargets()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("detachAllTargets()", source, StringComparison.Ordinal);
        Assert.Contains("active: false", source, StringComparison.Ordinal);
        Assert.Contains("Page.captureScreenshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("secretPattern", source, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key: apiKey", source, StringComparison.Ordinal);
        Assert.Contains("\"alarms\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"minimum_chrome_version\": \"120\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserSkill_要求AI主动管理当前AssistantSession租约()
    {
        var source = ReadRepositoryFile("LoomX", "Skills", "relays", "new-api", "SKILL.md");

        Assert.Contains("browser.bridge_start", source, StringComparison.Ordinal);
        Assert.Contains("browser.bridge_stop", source, StringComparison.Ordinal);
        Assert.Contains("assistant_session_id", source, StringComparison.Ordinal);
        Assert.Contains("当前 Assistant Session ID", source, StringComparison.Ordinal);
        Assert.Contains("删除 Session", source, StringComparison.Ordinal);
        Assert.Contains("意外残留", source, StringComparison.Ordinal);
    }
    [Fact]
    public void LoggingBootstrap_文件Sink不组合Shared与Buffered()
    {
        var source = ReadRepositoryFile("LoomX", "Logging", "LoggingBootstrap.cs");

        Assert.Contains("buffered: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("shared: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterAll_RegistersAllRequiredTools()
    {
        var expected = new[]
        {
            "loomx.get_status",
            "loomx.list_endpoints", "loomx.get_endpoint", "loomx.create_endpoint", "loomx.update_endpoint", "loomx.delete_endpoint",
            "loomx.list_combos", "loomx.get_combo", "loomx.create_combo", "loomx.update_combo", "loomx.delete_combo",
            "loomx.list_providers", "loomx.get_provider", "loomx.create_provider", "loomx.update_provider", "loomx.delete_provider",
            "loomx.list_models", "loomx.get_model", "loomx.create_model", "loomx.update_model", "loomx.delete_model",
            "loomx.test_provider", "loomx.test_model", "loomx.test_endpoint",
        };
        foreach (var name in expected)
        {
            Assert.True(registry.TryGet(name, out _), $"缺少工具 {name}");
        }
    }

    [Fact]
    public async Task GetStatus_ReturnsVersionAndCounts()
    {
        var result = await InvokeAsync("loomx.get_status");

        Assert.True(result.Success);
        var json = JsonNode.Parse(result.Content)!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(json["version"]?.GetValue<string>()));
        Assert.NotNull(json["providers"]);
        Assert.NotNull(json["endpoints"]);
    }

    [Fact]
    public async Task GetStatus_ReportsGatewayRuntimeState()
    {
        gatewayStateHub.Update(GatewayState.Stopped, null);
        var stopped = JsonNode.Parse((await InvokeAsync("loomx.get_status")).Content)!.AsObject();
        Assert.False(stopped["gateway"]!["running"]!.GetValue<bool>());
        Assert.Equal("stopped", stopped["gateway"]!["state"]!.GetValue<string>());

        gatewayStateHub.Update(GatewayState.Running, null);
        var running = JsonNode.Parse((await InvokeAsync("loomx.get_status")).Content)!.AsObject();
        Assert.True(running["gateway"]!["running"]!.GetValue<bool>());
        Assert.Equal("running", running["gateway"]!["state"]!.GetValue<string>());

        gatewayStateHub.Update(GatewayState.Failed, "健康检查返回 HTTP 503。");
        var failed = JsonNode.Parse((await InvokeAsync("loomx.get_status")).Content)!.AsObject();
        Assert.False(failed["gateway"]!["running"]!.GetValue<bool>());
        Assert.Equal("failed", failed["gateway"]!["state"]!.GetValue<string>());
        Assert.Equal("健康检查返回 HTTP 503。", failed["gateway"]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProviderCrud_RoundTrip()
    {
        var created = await InvokeAsync("loomx.create_provider",
            """{"business_id":"demo","display_name":"演示","base_url":"https://api.example.com/v1","api_mode":"openai","api_key":"sk-test-secret-12345"}""");
        Assert.True(created.Success, created.Content);
        var id = JsonNode.Parse(created.Content)!["id"]!.GetValue<Guid>();

        var listed = await InvokeAsync("loomx.list_providers");
        Assert.Contains("demo", listed.Content);

        var fetched = await InvokeAsync("loomx.get_provider", """{"id":"demo"}""");
        Assert.True(fetched.Success);
        Assert.Contains("api.example.com", fetched.Content);

        var updated = await InvokeAsync("loomx.update_provider",
            $$"""{"id":"{{id}}","business_id":"demo","display_name":"演示2","base_url":"https://api.example.com/v1","api_mode":"openai","enabled":false}""");
        Assert.True(updated.Success, updated.Content);
        Assert.Contains("演示2", updated.Content);

        var deleted = await InvokeAsync("loomx.delete_provider", $$"""{"id":"{{id}}"}""");
        Assert.True(deleted.Success, deleted.Content);
    }

    [Fact]
    public async Task SecretBoundary_NoToolOutputContainsPlaintextApiKey()
    {
        await CreateProviderAsync();

        foreach (var (tool, args) in new[]
        {
            ("loomx.list_providers", (string?)null),
            ("loomx.get_provider", """{"id":"demo"}"""),
            ("loomx.get_status", null),
        })
        {
            var result = await InvokeAsync(tool, args);
            Assert.True(result.Success);
            Assert.DoesNotContain(PlaintextApiKey, result.Content);
        }

        var provider = await InvokeAsync("loomx.get_provider", """{"id":"demo"}""");
        Assert.Contains("secret://provider/demo/apikey", provider.Content);
        Assert.Contains("\"configured\":true", provider.Content);
    }

    [Fact]
    public async Task GetProvider_NotFound_ReturnsFailure()
    {
        var result = await InvokeAsync("loomx.get_provider", """{"id":"no-such-provider"}""");

        Assert.False(result.Success);
        Assert.Contains("不存在", result.Content);
    }

    [Fact]
    public async Task CreateProvider_MissingRequiredArg_ReturnsFailure()
    {
        var result = await InvokeAsync("loomx.create_provider", """{"business_id":"demo"}""");

        Assert.False(result.Success);
        Assert.Contains("缺少必填参数", result.Content);
    }

    [Fact]
    public async Task ModelCrud_RoundTrip()
    {
        var providerId = await CreateProviderAsync();

        var created = await InvokeAsync("loomx.create_model",
            $$"""{"provider_id":"{{providerId}}","model_id":"gpt-4o","display_name":"GPT-4o","family":"gpt-4","context_length":128000,"max_tokens":4096,"vision":true}""");
        Assert.True(created.Success, created.Content);
        var modelId = JsonNode.Parse(created.Content)!["id"]!.GetValue<Guid>();

        var listed = await InvokeAsync("loomx.list_models", $$"""{"provider_id":"{{providerId}}"}""");
        Assert.Contains("gpt-4o", listed.Content);

        var fetched = await InvokeAsync("loomx.get_model", $$"""{"id":"{{modelId}}"}""");
        Assert.True(fetched.Success);
        Assert.Contains("128000", fetched.Content);

        var updated = await InvokeAsync("loomx.update_model",
            $$"""{"id":"{{modelId}}","model_id":"gpt-4o","display_name":"GPT-4o","family":"gpt-4","context_length":128000,"max_tokens":8192,"enabled":true}""");
        Assert.True(updated.Success, updated.Content);
        Assert.Contains("8192", updated.Content);

        var deleted = await InvokeAsync("loomx.delete_model", $$"""{"id":"{{modelId}}"}""");
        Assert.True(deleted.Success, deleted.Content);
    }

    [Fact]
    public async Task ComboCrud_WithRoutes()
    {
        var providerId = await CreateProviderAsync();
        var model = await configuration.CreateModelAsync(
            Guid.Parse(providerId),
            new ModelInput("gpt-4o", "GPT-4o", null, "gpt-4", null, null, 128000, 4096, false, null, null, true, null, false, null, null),
            CancellationToken.None);

        var created = await InvokeAsync("loomx.create_combo",
            $$"""{"name":"demo-combo","model_ids":["{{model.Id}}"]}""");
        Assert.True(created.Success, created.Content);
        var comboJson = JsonNode.Parse(created.Content)!.AsObject();
        Assert.Single(comboJson["routes"]!.AsArray());

        var fetched = await InvokeAsync("loomx.get_combo", """{"id":"demo-combo"}""");
        Assert.True(fetched.Success);
        Assert.Contains("GPT-4o", fetched.Content);

        var routeId = comboJson["routes"]!.AsArray()[0]!["id"]!.GetValue<Guid>();
        var removed = await InvokeAsync("loomx.remove_combo_route", $$"""{"route_id":"{{routeId}}"}""");
        Assert.True(removed.Success, removed.Content);

        var deleted = await InvokeAsync("loomx.delete_combo", $$"""{"id":"{{comboJson["id"]!.GetValue<Guid>()}}"}""");
        Assert.True(deleted.Success, deleted.Content);
    }

    [Fact]
    public async Task EndpointTools_ListGetUpdate()
    {
        var listed = await InvokeAsync("loomx.list_endpoints");
        Assert.True(listed.Success);
        Assert.Contains("ollama", listed.Content);

        var fetched = await InvokeAsync("loomx.get_endpoint", """{"key":"openai"}""");
        Assert.True(fetched.Success, fetched.Content);
        Assert.Contains("openai", fetched.Content);
        Assert.DoesNotContain(PlaintextApiKey, fetched.Content);

        var updated = await InvokeAsync("loomx.update_endpoint", """{"key":"ollama","enabled":false}""");
        Assert.True(updated.Success, updated.Content);
        Assert.Contains("\"enabled\":false", updated.Content);

        var restored = await InvokeAsync("loomx.update_endpoint", """{"key":"ollama","enabled":true}""");
        Assert.True(restored.Success, restored.Content);
    }

    [Fact]
    public async Task CreateOrDeleteEndpoint_ReturnsGuidanceFailure()
    {
        var created = await InvokeAsync("loomx.create_endpoint", """{"key":"custom"}""");
        Assert.False(created.Success);
        Assert.Contains("系统预置", created.Content);

        var deleted = await InvokeAsync("loomx.delete_endpoint", """{"key":"ollama"}""");
        Assert.False(deleted.Success);
        Assert.Contains("不可删除", deleted.Content);
    }

    [Fact]
    public async Task DeleteProvider_WithModels_ReturnsFailure()
    {
        var providerId = await CreateProviderAsync();
        await configuration.CreateModelAsync(
            Guid.Parse(providerId),
            new ModelInput("gpt-4o", "GPT-4o", null, "gpt-4", null, null, 128000, 4096, false, null, null, true, null, false, null, null),
            CancellationToken.None);

        var result = await InvokeAsync("loomx.delete_provider", $$"""{"id":"{{providerId}}"}""");

        Assert.False(result.Success);
        Assert.Contains("模型", result.Content);
    }

    [Fact]
    public async Task TestProvider_UsesConfiguredProbe()
    {
        await CreateProviderAsync();

        var result = await InvokeAsync("loomx.test_provider", """{"id":"demo"}""");

        Assert.True(result.Success, result.Content);
        var json = JsonNode.Parse(result.Content)!.AsObject();
        Assert.Equal("provider_ok", json["diagnosis"]?.GetValue<string>());
        Assert.Equal(1, json["models_found"]?.GetValue<int>());
        Assert.DoesNotContain(PlaintextApiKey, result.Content);
    }

    [Fact]
    public async Task SkillTools_ListAndLoad()
    {
        var listed = await InvokeAsync("skill.list");
        Assert.True(listed.Success);
        Assert.Contains("test-skill", listed.Content);

        var loaded = await InvokeAsync("skill.load", """{"category":"providers","name":"test-skill"}""");
        Assert.True(loaded.Success);
        Assert.Contains("测试 Skill 内容", loaded.Content);

        var missing = await InvokeAsync("skill.load", """{"category":"providers","name":"no-such-skill"}""");
        Assert.False(missing.Success);
        Assert.Contains("不存在", missing.Content);
    }
    [Fact]
    public async Task GetProvider不存在_真实Handler不回显原始Id()
    {
        const string privateId = "private-header-value";

        var result = await InvokeAsync("loomx.get_provider", $$"""{"id":"{{privateId}}"}""");

        Assert.False(result.Success);
        Assert.DoesNotContain(privateId, result.Content, StringComparison.Ordinal);
        Assert.Contains("不存在", result.Content, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory.FullName, .. segments]));
    }

}
