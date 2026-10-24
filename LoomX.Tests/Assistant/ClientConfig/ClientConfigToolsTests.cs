using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.ClientConfig;
using LoomX.Assistant.Configuration;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant.ClientConfig;

/// <summary>
/// clientconfig.* 工具端到端：真实 ConfigurationManagementService（SQLite 临时库）+
/// fake home 写入。验证 Endpoint Key 服务端解析、Combo 模型解析、文件落盘与凭据不回显。
/// </summary>
public sealed class ClientConfigToolsTests : IAsyncLifetime
{
    private string databasePath = string.Empty;
    private string fakeHome = string.Empty;
    private ConfigurationDbContext startupContext = null!;
    private ConfigurationManagementService configuration = null!;
    private DatabaseConfigurationProvider configurationProvider = null!;
    private TestDbContextFactory factory = null!;
    private ToolRegistry registry = null!;
    private string endpointApiKey = string.Empty;

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"loomx-clientconfig-{Guid.NewGuid():N}.db");
        fakeHome = Path.Combine(Path.GetTempPath(), $"codextest-home-{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        await using (var context = new ConfigurationDbContext(options))
        {
            await ConfigurationDatabase.InitializeAsync(context);
        }

        startupContext = new ConfigurationDbContext(options);
        configurationProvider = new DatabaseConfigurationProvider(startupContext);
        await configurationProvider.ReloadAsync();
        factory = new TestDbContextFactory(options);
        configuration = new ConfigurationManagementService(factory, configurationProvider);

        registry = new ToolRegistry();
        ClientConfigTools.RegisterAll(
            registry,
            new ClientConfigService(new TomlDocumentService(NullLogger<TomlDocumentService>.Instance), NullLogger<ClientConfigService>.Instance),
            configuration,
            configurationProvider);
    }

    public async Task DisposeAsync()
    {
        await startupContext.DisposeAsync();
        TryDelete(databasePath);
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        if (Directory.Exists(fakeHome))
        {
            Directory.Delete(fakeHome, recursive: true);
        }
    }

    /// <summary>准备：Provider+模型+Combo 路由、Endpoint 绑定 Combo、Endpoint API Key 轮换。</summary>
    private async Task SetupGatewayAsync()
    {
        var provider = await configuration.CreateProviderAsync(
            new ProviderInput("demo", "演示", "https://api.example.com/v1", "openai", true, null, false, null),
            CancellationToken.None);
        var model = await configuration.CreateModelAsync(provider.Id, new ModelInput(
            "glm-5.3", "GLM 5.3", null, "glm", null, null, 200_000, 128_000, true, null, null, true, null, false, null, null), CancellationToken.None);
        var combo = await configuration.CreateGatewayComboAsync(new GatewayComboInput("main", true, 0), CancellationToken.None);
        await configuration.CreateGatewayRouteAsync(combo.Id, new GatewayRouteInput(model.Id, true, 0), CancellationToken.None);
        await configuration.UpdateGatewayEndpointComboBindingsAsync(
            "openai",
            new GatewayEndpointComboSelectionInput([combo.Id]),
            CancellationToken.None);
        var rotated = await configuration.RotateGatewayApiKeyAsync("openai", CancellationToken.None);
        endpointApiKey = rotated.ApiKey!;
    }

    private async Task<ToolResult> InvokeAsync(string toolName, string argumentsJson)
    {
        var tool = registry.All.FirstOrDefault(item => item.Name == toolName)
            ?? throw new InvalidOperationException($"工具 '{toolName}' 未注册。");
        return await tool.Handler(JsonNode.Parse(argumentsJson), CancellationToken.None);
    }

    [Fact]
    public void 注册三个clientconfig工具()
    {
        Assert.Equal(3, registry.All.Count);
        Assert.True(registry.TryGet("clientconfig.list_targets", out var listTargets));
        Assert.Equal(ToolRiskLevel.Read, listTargets!.RiskLevel);
        Assert.True(registry.TryGet("clientconfig.inspect", out var inspect));
        Assert.Equal(ToolRiskLevel.Read, inspect!.RiskLevel);
        Assert.True(registry.TryGet("clientconfig.write", out var write));
        Assert.Equal(ToolRiskLevel.Write, write!.RiskLevel);
        // 参数投影不携带 api_key。
        var projection = write.SafeArgumentsProjector!(JsonNode.Parse(
            $$"""{"client":"codex","api_key":"{{endpointApiKey}}","combo_ids":["a","b"]}"""));
        Assert.DoesNotContain("sk-", projection!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(2, projection["combo_id_count"]!.GetValue<int>());
    }

    [Fact]
    public async Task ListTargets_列出全部客户端与配置路径()
    {
        var result = await InvokeAsync("clientconfig.list_targets", "{}");

        Assert.True(result.Success, result.Content);
        var json = JsonNode.Parse(result.Content)!;
        var keys = json["targets"]!.AsArray().Select(node => node!["target_key"]!.GetValue<string>()).ToArray();
        Assert.Equal(
            ["claude-code", "codex", "gemini-cli", "grok", "hermes", "openclaw", "opencode", "zcode-claude", "zcode-codex"],
            keys);
        var codex = json["targets"]!.AsArray().First(node => node!["target_key"]!.GetValue<string>() == "codex");
        // 默认路径指向真实用户主目录下的 .codex（仅展示，不触发写入）。
        Assert.EndsWith(Path.Combine(".codex", "config.toml"), codex["config_path"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_报告缺失状态()
    {
        var result = await InvokeAsync("clientconfig.inspect", $$"""{"client":"codex","home_root":"{{fakeHome.Replace("\\", "\\\\")}}"}""");

        Assert.True(result.Success, result.Content);
        var json = JsonNode.Parse(result.Content)!;
        Assert.Equal("missing", json["file_status"]!.GetValue<string>());
        Assert.StartsWith(fakeHome, json["config_path"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Write_Codex端到端_服务端解析Key_写配置与Catalog_不回显Key()
    {
        await SetupGatewayAsync();

        var result = await InvokeAsync("clientconfig.write", $$"""{"client":"codex","home_root":"{{fakeHome.Replace("\\", "\\\\")}}"}""");

        Assert.True(result.Success, result.Content);
        Assert.DoesNotContain(endpointApiKey, result.Content, StringComparison.Ordinal);
        var json = JsonNode.Parse(result.Content)!;
        Assert.StartsWith(fakeHome, json["config_path"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["glm-5.3"], json["models_written"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());

        var config = (await File.ReadAllTextAsync(json["config_path"]!.GetValue<string>()))
            .Replace("\"", string.Empty, StringComparison.Ordinal);
        Assert.Contains(endpointApiKey, config, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:11434/openai/v1", config, StringComparison.Ordinal);
        Assert.Contains("model = glm-5.3", config, StringComparison.Ordinal);
        var catalog = await File.ReadAllTextAsync(json["catalog_path"]!.GetValue<string>());
        Assert.DoesNotContain(endpointApiKey, catalog, StringComparison.Ordinal);
        Assert.Contains("\"slug\": \"glm-5.3\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_ZCode按Combo解析模型并带托管标记()
    {
        await SetupGatewayAsync();

        var result = await InvokeAsync("clientconfig.write", $$"""{"client":"zcode-codex","home_root":"{{fakeHome.Replace("\\", "\\\\")}}"}""");

        Assert.True(result.Success, result.Content);
        Assert.DoesNotContain(endpointApiKey, result.Content, StringComparison.Ordinal);
        var json = JsonNode.Parse(result.Content)!;
        var content = await File.ReadAllTextAsync(json["config_path"]!.GetValue<string>());
        Assert.Contains("loomx-codex", content, StringComparison.Ordinal);
        Assert.Contains("x-loomx-managed", content, StringComparison.Ordinal);
        Assert.Contains("glm-5.3", content, StringComparison.Ordinal);
        Assert.Contains(endpointApiKey, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_未配置Key的Endpoint返回安全失败()
    {
        await SetupGatewayAsync();
        // 初始化时会为 openai/azure 预生成 Key；清空 azure 的 Key 以触发缺 Key 分支。
        await using (var context = await factory.CreateDbContextAsync())
        {
            var azure = await context.GatewayEndpoints.SingleAsync(item => item.Key == "azure");
            azure.ProtectedApiKey = null;
            await context.SaveChangesAsync();
        }

        var result = await InvokeAsync("clientconfig.write", $$"""{"client":"claude-code","endpoint":"azure","home_root":"{{fakeHome.Replace("\\", "\\\\")}}"}""");

        Assert.False(result.Success);
        Assert.Contains("API Key", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(endpointApiKey, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_未知客户端与无Combo模型分别安全失败()
    {
        await SetupGatewayAsync();

        var unknown = await InvokeAsync("clientconfig.write", $$"""{"client":"nope","home_root":"{{fakeHome.Replace("\\", "\\\\")}}"}""");
        Assert.False(unknown.Success);
        Assert.Contains("clientconfig.list_targets", unknown.Content, StringComparison.Ordinal);

        // ollama Endpoint 默认无 Combo 绑定，也无法从参数拿到模型 → 给出指引。
        var noModels = await InvokeAsync("clientconfig.write", $$"""{"client":"opencode","endpoint":"ollama","home_root":"{{fakeHome.Replace("\\", "\\\\")}}"}""");
        Assert.False(noModels.Success);
        Assert.Contains("没有可写入的模型", noModels.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_未知客户端安全失败()
    {
        var result = await InvokeAsync("clientconfig.inspect", """{"client":"nope"}""");
        Assert.False(result.Success);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
