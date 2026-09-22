using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
            new ProviderInput("demo", "演示", "https://api.example.com/v1", "openai", true, LoomXToolsTests.PlaintextApiKey, false, null),
            CancellationToken.None);
        return provider.Id.ToString();
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
    public async Task TestEndpoint_ApiKeyOnlyExposesSecretRef()
    {
        var result = await CreateTester().TestEndpointAsync("openai", CancellationToken.None);

        var apiKey = result["api_key"]!.AsObject();
        var configured = apiKey["configured"]!.GetValue<bool>();
        Assert.Equal(configured, apiKey["secret_ref"] is not null);
        if (configured)
        {
            Assert.StartsWith("secret://endpoint/openai/", apiKey["secret_ref"]!.GetValue<string>());
        }
    }
}
