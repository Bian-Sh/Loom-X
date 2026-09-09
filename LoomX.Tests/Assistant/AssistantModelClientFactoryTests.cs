using Xunit;
using LoomX.Assistant;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

/// <summary>
/// 助手模型工厂选择逻辑：启用过滤、api_mode 过滤、模型级 base_url 覆盖。
/// </summary>
public sealed class AssistantModelClientFactoryTests : IAsyncLifetime
{
    private string databasePath = string.Empty;
    private string preferencesPath = string.Empty;
    private ConfigurationDbContext startupContext = null!;
    private ConfigurationManagementService configuration = null!;
    private AssistantPreferencesStore preferencesStore = null!;
    private AssistantModelClientFactory factory = null!;

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"loomx-modelfactory-{Guid.NewGuid():N}.db");
        preferencesPath = Path.Combine(Path.GetTempPath(), $"loomx-modelfactory-prefs-{Guid.NewGuid():N}.json");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        await using (var context = new ConfigurationDbContext(options))
        {
            await ConfigurationDatabase.InitializeAsync(context);
        }

        startupContext = new ConfigurationDbContext(options);
        var configurationProvider = new DatabaseConfigurationProvider(startupContext);
        await configurationProvider.ReloadAsync();
        configuration = new ConfigurationManagementService(new TestDbContextFactory(options), configurationProvider);
        preferencesStore = new AssistantPreferencesStore(preferencesPath);
        factory = new AssistantModelClientFactory(
            new DelegateHttpClientFactory(),
            configuration,
            NullLogger<OpenAiCompatibleModelClient>.Instance,
            NullLogger<AssistantModelClientFactory>.Instance,
            preferencesStore: preferencesStore);
    }

    public async Task DisposeAsync()
    {
        await startupContext.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix); } catch (IOException) { }
        }

        try { if (File.Exists(preferencesPath)) File.Delete(preferencesPath); } catch (IOException) { }
    }

    [Fact]
    public async Task NoProviders_ReturnsNull()
    {
        Assert.Null(await factory.TryCreateAsync(CancellationToken.None));
        Assert.Null(await factory.DescribeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnthropicOnlyProvider_Skipped()
    {
        var provider = await CreateProviderAsync("claude-only", "anthropic");
        await CreateModelAsync(provider.Id, "claude-sonnet-4-5");

        Assert.Null(await factory.TryCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisabledProvider_Skipped()
    {
        var provider = await CreateProviderAsync("disabled-openai", "openai", enabled: false);
        await CreateModelAsync(provider.Id, "gpt-4o");

        Assert.Null(await factory.TryCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnabledOpenAiProviderWithModel_Selected()
    {
        var provider = await CreateProviderAsync("main-openai", "openai");
        await CreateModelAsync(provider.Id, "gpt-4o");

        var client = await factory.TryCreateAsync(CancellationToken.None);
        var info = await factory.DescribeAsync(CancellationToken.None);

        Assert.NotNull(client);
        Assert.NotNull(info);
        Assert.Equal("main-openai", info.ProviderBusinessId);
        Assert.Equal("gpt-4o", info.ModelId);
        Assert.Equal("https://api.example.com/v1", info.BaseUrl);
    }

    [Fact]
    public async Task ModelLevelMultiApiMode_IncludingOpenAi_Selected()
    {
        var provider = await CreateProviderAsync("multi", "anthropic");
        await CreateModelAsync(provider.Id, "gpt-4o-mini", apiMode: "anthropic;openai");

        Assert.NotNull(await factory.TryCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ModelLevelApiMode_WithoutOpenAi_Skipped()
    {
        var provider = await CreateProviderAsync("mixed", "openai");
        await CreateModelAsync(provider.Id, "claude-via-openai-provider", apiMode: "anthropic");

        Assert.Null(await factory.TryCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ModelLevelBaseUrl_OverridesProvider()
    {
        var provider = await CreateProviderAsync("override", "openai");
        var model = await CreateModelAsync(provider.Id, "gpt-4o", baseUrl: "https://model-specific.example.com/v1");

        var info = await factory.DescribeAsync(CancellationToken.None);

        Assert.Equal("https://model-specific.example.com/v1", info!.BaseUrl);
    }

    [Fact]
    public async Task DisabledModel_SkippedToNextEnabled()
    {
        var provider = await CreateProviderAsync("two-models", "openai");
        await CreateModelAsync(provider.Id, "disabled-model", enabled: false);
        await CreateModelAsync(provider.Id, "enabled-model");

        var info = await factory.DescribeAsync(CancellationToken.None);

        Assert.Equal("enabled-model", info!.ModelId);
    }

    [Fact]
    public async Task PreferredModel_SelectedOverAutomatic()
    {
        var first = await CreateProviderAsync("first-openai", "openai");
        await CreateModelAsync(first.Id, "auto-model");
        var second = await CreateProviderAsync("second-openai", "openai");
        await CreateModelAsync(second.Id, "wanted-model");
        factory.SetPreferredSelection("second-openai", "wanted-model");

        var info = await factory.DescribeAsync(CancellationToken.None);

        Assert.Equal("second-openai", info!.ProviderBusinessId);
        Assert.Equal("wanted-model", info.ModelId);
    }

    [Fact]
    public async Task PreferredModelDisabled_FallsBackToAutomatic()
    {
        var first = await CreateProviderAsync("first-openai", "openai");
        await CreateModelAsync(first.Id, "auto-model");
        var second = await CreateProviderAsync("second-openai", "openai");
        await CreateModelAsync(second.Id, "wanted-model", enabled: false);
        factory.SetPreferredSelection("second-openai", "wanted-model");

        var info = await factory.DescribeAsync(CancellationToken.None);

        Assert.Equal("first-openai", info!.ProviderBusinessId);
        Assert.Equal("auto-model", info.ModelId);
    }

    [Fact]
    public async Task ClearPreferredSelection_RestoresAutomatic()
    {
        var first = await CreateProviderAsync("first-openai", "openai");
        await CreateModelAsync(first.Id, "auto-model");
        var second = await CreateProviderAsync("second-openai", "openai");
        await CreateModelAsync(second.Id, "wanted-model");
        factory.SetPreferredSelection("second-openai", "wanted-model");
        factory.ClearPreferredSelection();

        var info = await factory.DescribeAsync(CancellationToken.None);

        Assert.Equal("first-openai", info!.ProviderBusinessId);
    }

    [Fact]
    public async Task ListAvailable_GroupsOnlyEnabledOpenAiModels()
    {
        var openai = await CreateProviderAsync("main-openai", "openai");
        await CreateModelAsync(openai.Id, "gpt-4o");
        await CreateModelAsync(openai.Id, "gpt-4o-mini");
        var anthropic = await CreateProviderAsync("claude", "anthropic");
        await CreateModelAsync(anthropic.Id, "claude-sonnet-4-5");
        var disabled = await CreateProviderAsync("disabled-openai", "openai", enabled: false);
        await CreateModelAsync(disabled.Id, "hidden-model");

        var groups = await factory.ListAvailableAsync(CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal("main-openai", group.ProviderBusinessId);
        Assert.Equal(2, group.Models.Count);
        Assert.DoesNotContain(group.Models, model => model.ModelId == "hidden-model");
    }

    private async Task<ProviderResponse> CreateProviderAsync(string businessId, string apiMode, bool enabled = true) =>
        await configuration.CreateProviderAsync(
            new ProviderInput(businessId, businessId, "https://api.example.com/v1", apiMode, enabled,
                "sk-test-secret-12345", false, null, false, null, "responses"),
            CancellationToken.None);

    private async Task<ModelResponse> CreateModelAsync(Guid providerId, string modelId, bool enabled = true, string? baseUrl = null, string? apiMode = null) =>
        await configuration.CreateModelAsync(providerId,
            new ModelInput(modelId, modelId, null, "gpt", baseUrl, apiMode, 128000, 4096, false, null, null, enabled, null, false, null, null),
            CancellationToken.None);

    private sealed class DelegateHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
