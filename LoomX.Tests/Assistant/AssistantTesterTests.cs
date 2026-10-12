using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Microsoft.Extensions.Logging;

namespace LoomX.Tests.Assistant;

public sealed class AssistantTesterTests : IAsyncLifetime
{
    private string databasePath = string.Empty;
    private ConfigurationDbContext startupContext = null!;
    private ConfigurationManagementService configuration = null!;
    private TestDbContextFactory factory = null!;
    private Func<HttpRequestMessage, HttpResponseMessage> responder = _ => new HttpResponseMessage(HttpStatusCode.OK);

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"loomx-tester-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        await using (var context = new ConfigurationDbContext(options))
        {
            await ConfigurationDatabase.InitializeAsync(context);
        }

        startupContext = new ConfigurationDbContext(options);
        var configurationProvider = new DatabaseConfigurationProvider(startupContext);
        await configurationProvider.ReloadAsync();
        factory = new TestDbContextFactory(options);
        configuration = new ConfigurationManagementService(factory, configurationProvider);
    }

    public async Task DisposeAsync()
    {
        await startupContext.DisposeAsync();
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    private AssistantTester CreateTester() =>
        new(new HttpClient(new DelegateHttpHandler(request => responder(request))), configuration, factory);

    private async Task<string> CreateProviderAsync()
    {
        var provider = await configuration.CreateProviderAsync(
            new ProviderInput("demo", "演示", "https://api.example.com/v1", "openai", true, LoomXToolsTests.PlaintextApiKey, false, null, EndpointFormat: "chat_completions"),
            CancellationToken.None);
        return provider.Id.ToString();
    }

    [Fact]
    public async Task TestUrl_日志不包含查询凭据或响应正文()
    {
        var logger = new ProbeLogger();
        responder = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("敏感响应测试值") };
        var tester = new AssistantTester(new HttpClient(new DelegateHttpHandler(request => responder(request))), configuration, factory, logger);
        var result = await tester.TestUrlAsync("https://example.com/test?api_key=敏感查询测试值", CancellationToken.None);
        Assert.DoesNotContain("敏感查询测试值", string.Join("\n", logger.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("敏感响应测试值", string.Join("\n", logger.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("敏感查询测试值", result.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("敏感响应测试值", result.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(logger.Messages, item => item.Contains("网址测试未通过", StringComparison.Ordinal));
    }

    private sealed class ProbeLogger : ILogger<AssistantTester>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception) + " " + exception?.ToString());
    }

    [Theory]
    [InlineData("file:///C:/test")]
    [InlineData("https://user:secret@example.com")]
    public async Task TestUrl_拒绝非Http和携带凭据的地址(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateTester().TestUrlAsync(url, CancellationToken.None));
    }

    [Fact]
    public async Task TestUrl_可达与Http成功分开判断且不返回响应正文()
    {
        responder = request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("敏感测试值") };
        };
        var result = await CreateTester().TestUrlAsync("https://example.com", CancellationToken.None);
        Assert.True(result["reachable"]!.GetValue<bool>());
        Assert.False(result["http_success"]!.GetValue<bool>());
        Assert.Equal(401, result["status_code"]!.GetValue<int>());
        Assert.DoesNotContain("敏感测试值", result.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestUrl_不支持Head时退回Get但不读取正文()
    {
        var methods = new List<HttpMethod>();
        responder = request =>
        {
            methods.Add(request.Method);
            return new HttpResponseMessage(request.Method == HttpMethod.Head ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK);
        };
        var result = await CreateTester().TestUrlAsync("https://example.com", CancellationToken.None);
        Assert.Equal(new[] { HttpMethod.Head, HttpMethod.Get }, methods);
        Assert.True(result["http_success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TestModel_使用Responses接口与模型覆盖Header并返回真实接口摘要()
    {
        var providerId = await CreateProviderAsync();
        var provider = (await configuration.ListProvidersAsync()).Single();
        await configuration.UpdateProviderAsync(provider.Id, new ProviderInput(provider.BusinessId, provider.DisplayName,
            provider.BaseUrl, "openai", true, null, false, new() { ["X-Test"] = "Provider" }, EndpointFormat: "responses"));
        var model = await configuration.CreateModelAsync(Guid.Parse(providerId), new ModelInput("测试模型", "测试模型", null,
            "openai", null, null, 4096, 128, false, null, null, true, null, false, new() { ["X-Test"] = "Model" }, null));
        responder = request =>
        {
            Assert.Equal("/v1/responses", request.RequestUri!.AbsolutePath);
            Assert.Equal("Model", request.Headers.GetValues("X-Test").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"output_text\":\"pong\"}") };
        };
        var result = await CreateTester().TestModelAsync(model.Id, CancellationToken.None);
        Assert.True(result["chat_test"]!.GetValue<bool>());
        Assert.Equal("/v1/responses", result["request_path"]!.GetValue<string>());
        var api = await CreateTester().GetModelApiAsync(model.Id, CancellationToken.None);
        Assert.Equal("responses", api["endpoint_format"]!.GetValue<string>());
        Assert.Equal("/v1/responses", api["request_path"]!.GetValue<string>());
        Assert.DoesNotContain(LoomXToolsTests.PlaintextApiKey, api.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestProvider_Ok_ReturnsModelCount()
    {
        await CreateProviderAsync();
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"a"},{"id":"b"},{"id":"c"}]}""", Encoding.UTF8, "application/json"),
        };

        var result = await CreateTester().TestProviderAsync("demo", CancellationToken.None);

        Assert.Equal("provider_ok", result["diagnosis"]?.GetValue<string>());
        Assert.Equal(3, result["models_found"]?.GetValue<int>());
        Assert.Equal(new[] { "a", "b", "c" }, result["models"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.True(result["reachable"]!.GetValue<bool>());
        Assert.True(result["authenticated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TestProvider_SendsBearerKeyButNeverReturnsIt()
    {
        await CreateProviderAsync();
        string? authorization = null;
        responder = request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json"),
            };
        };

        var result = await CreateTester().TestProviderAsync("demo", CancellationToken.None);

        Assert.Equal($"Bearer {LoomXToolsTests.PlaintextApiKey}", authorization);
        Assert.DoesNotContain(LoomXToolsTests.PlaintextApiKey, result.ToJsonString());
    }

    [Fact]
    public async Task TestProvider_Unauthorized_ReturnsAuthFailed()
    {
        await CreateProviderAsync();
        responder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await CreateTester().TestProviderAsync("demo", CancellationToken.None);

        Assert.Equal("auth_failed", result["diagnosis"]?.GetValue<string>());
        Assert.False(result["authenticated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TestProvider_Unreachable_ReturnsUnreachable()
    {
        await CreateProviderAsync();
        responder = _ => throw new HttpRequestException("连接失败");

        var result = await CreateTester().TestProviderAsync("demo", CancellationToken.None);

        Assert.Equal("unreachable", result["diagnosis"]?.GetValue<string>());
        Assert.False(result["reachable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TestProvider_InvalidModelList_ReturnsInvalidModelList()
    {
        await CreateProviderAsync();
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html"),
        };

        var result = await CreateTester().TestProviderAsync("demo", CancellationToken.None);

        Assert.Equal("invalid_model_list", result["diagnosis"]?.GetValue<string>());
    }

    [Fact]
    public async Task TestModel_Ok_ReturnsModelOk()
    {
        var providerId = await CreateProviderAsync();
        var model = await configuration.CreateModelAsync(
            Guid.Parse(providerId),
            new ModelInput("gpt-4o", "GPT-4o", null, "gpt-4", null, null, 128000, 4096, false, null, null, true, null, false, null, null),
            CancellationToken.None);
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"pong"}}]}""", Encoding.UTF8, "application/json"),
        };

        var result = await CreateTester().TestModelAsync(model.Id, CancellationToken.None);

        Assert.Equal("model_ok", result["diagnosis"]?.GetValue<string>());
        Assert.True(result["chat_test"]!.GetValue<bool>());
        Assert.DoesNotContain(LoomXToolsTests.PlaintextApiKey, result.ToJsonString());
    }

    [Fact]
    public async Task TestModel_NotFoundOnUpstream_ReturnsModelNotFound()
    {
        var providerId = await CreateProviderAsync();
        var model = await configuration.CreateModelAsync(
            Guid.Parse(providerId),
            new ModelInput("gpt-4o", "GPT-4o", null, "gpt-4", null, null, 128000, 4096, false, null, null, true, null, false, null, null),
            CancellationToken.None);
        responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await CreateTester().TestModelAsync(model.Id, CancellationToken.None);

        Assert.Equal("model_not_found", result["diagnosis"]?.GetValue<string>());
        Assert.False(result["chat_test"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TestModel_MissingModel_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateTester().TestModelAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task TestEndpoint_DisabledEndpoint_ReturnsDisabled()
    {
        await configuration.SetGatewayEndpointEnabledAsync("ollama", false, CancellationToken.None);

        var result = await CreateTester().TestEndpointAsync("ollama", CancellationToken.None);

        Assert.Equal("endpoint_disabled", result["diagnosis"]?.GetValue<string>());
    }

    [Fact]
    public async Task TestEndpoint_EnabledWithoutCombos_ReturnsNoEnabledCombo()
    {
        var result = await CreateTester().TestEndpointAsync("ollama", CancellationToken.None);

        Assert.Equal("no_enabled_combo", result["diagnosis"]?.GetValue<string>());
    }

    [Fact]
    public async Task TestEndpoint_ApiKeyOnlyExposesConfiguredState()
    {
        var result = await CreateTester().TestEndpointAsync("openai", CancellationToken.None);

        Assert.NotNull(result["api_key_configured"]);
        _ = result["api_key_configured"]!.GetValue<bool>();
        Assert.Null(result["api_key"]);
        Assert.DoesNotContain("secret://", result.ToJsonString(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task 模型目录使用自定义URL且元数据与日志不含额外响应内容()
    {
        var id = Guid.Parse(await CreateProviderAsync());
        await configuration.UpdateProviderModelListUrlAsync(id, "https://catalog.example.com/custom?trace=敏感查询");
        responder = request =>
        {
            Assert.Equal("catalog.example.com", request.RequestUri!.Host);
            Assert.Equal("/custom", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer " + LoomXToolsTests.PlaintextApiKey, request.Headers.Authorization!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"real-model","owned_by":"relay","context_length":64000,"max_output_tokens":8000,"capabilities":["vision"]}],"private":"敏感响应"}""") };
        };
        var logger = new ProbeLogger();
        var tester = new AssistantTester(new HttpClient(new DelegateHttpHandler(request => responder(request))), configuration, factory, logger);
        var result = await tester.GetProviderModelsAsync("demo", false, CancellationToken.None);
        Assert.Equal("real-model", result["models"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(64000, result["models"]![0]!["context_length"]!.GetValue<int>());
        Assert.Equal(8000, result["models"]![0]!["max_tokens"]!.GetValue<int>());
        Assert.True(result["models"]![0]!["vision"]!.GetValue<bool>());
        Assert.Empty((await configuration.ListProvidersAsync()).Single().Models);
        Assert.DoesNotContain("敏感响应", result.ToJsonString());
        Assert.DoesNotContain("敏感查询", string.Join("\n", logger.Messages));
        Assert.DoesNotContain(LoomXToolsTests.PlaintextApiKey, string.Join("\n", logger.Messages));
        responder = _ => throw new HttpRequestException("包含敏感查询及敏感响应的错误");
        var failed = await tester.GetProviderModelsAsync("demo", true, CancellationToken.None);
        Assert.Equal("unreachable", failed["diagnosis"]!.GetValue<string>());
        Assert.False(failed["synced"]!.GetValue<bool>());
        Assert.DoesNotContain("敏感查询", string.Join("\n", logger.Messages));
        Assert.DoesNotContain("敏感响应", string.Join("\n", logger.Messages));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task 模型列表异常不会同步任何模型(int statusCode)
    {
        await CreateProviderAsync();
        responder = _ => new HttpResponseMessage((HttpStatusCode)statusCode) { Content = new StringContent("不是模型列表") };
        var result = await CreateTester().GetProviderModelsAsync("demo", true, CancellationToken.None);
        Assert.False(result["synced"]!.GetValue<bool>());
        Assert.Empty((await configuration.ListProvidersAsync()).Single().Models);
    }

}
