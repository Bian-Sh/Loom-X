using Xunit;
using LoomX.Assistant;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LoomX.Tests.Assistant;

/// <summary>
/// 助手偏好持久化：读写往返、缺表回落默认、思考等级归一化。
/// </summary>
public sealed class AssistantPreferencesStoreTests : IAsyncLifetime
{
    private string databasePath = string.Empty;
    private TestDbContextFactory dbContextFactory = null!;

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"loomx-assistant-prefs-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        await using (var context = new ConfigurationDbContext(options))
        {
            await ConfigurationDatabase.InitializeAsync(context);
        }
        dbContextFactory = new TestDbContextFactory(options);
    }

    public Task DisposeAsync()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix); } catch (IOException) { }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public void MissingRow_ReturnsDefaults()
    {
        var store = new AssistantPreferencesStore(dbContextFactory);

        var preferences = store.Load();

        Assert.Null(preferences.ProviderBusinessId);
        Assert.Null(preferences.ModelId);
        Assert.Equal(AssistantPreferences.DefaultReasoningEffort, preferences.ReasoningEffort);
        Assert.Equal(AssistantPermissionMode.AutoApprove, preferences.PermissionMode);
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var store = new AssistantPreferencesStore(dbContextFactory);
        store.Save(new AssistantPreferences
        {
            ProviderBusinessId = "main-openai",
            ModelId = "gpt-4o",
            ReasoningEffort = "high",
            PermissionMode = AssistantPermissionMode.AskEachTime,
        });

        var loaded = store.Load();

        Assert.Equal("main-openai", loaded.ProviderBusinessId);
        Assert.Equal("gpt-4o", loaded.ModelId);
        Assert.Equal("high", loaded.ReasoningEffort);
        Assert.Equal(AssistantPermissionMode.AskEachTime, loaded.PermissionMode);
    }

    [Fact]
    public void Update_MutatesAtomically()
    {
        var store = new AssistantPreferencesStore(dbContextFactory);
        store.Save(new AssistantPreferences { ModelId = "gpt-4o", ProviderBusinessId = "p1" });

        var updated = store.Update(current => current with { PermissionMode = AssistantPermissionMode.AskEachTime });

        Assert.Equal(AssistantPermissionMode.AskEachTime, updated.PermissionMode);
        var loaded = store.Load();
        Assert.Equal(AssistantPermissionMode.AskEachTime, loaded.PermissionMode);
        Assert.Equal("gpt-4o", loaded.ModelId);
    }

    [Fact]
    public void ClearModelSelection_PersistsNulls()
    {
        var store = new AssistantPreferencesStore(dbContextFactory);
        store.Save(new AssistantPreferences { ModelId = "gpt-4o", ProviderBusinessId = "p1" });
        store.Save(new AssistantPreferences { ModelId = null, ProviderBusinessId = null });

        var loaded = store.Load();

        Assert.Null(loaded.ProviderBusinessId);
        Assert.Null(loaded.ModelId);
    }

    [Theory]
    [InlineData("HIGH", "high")]
    [InlineData(" minimal ", "minimal")]
    [InlineData("extreme", "default")]
    [InlineData(null, "default")]
    public void NormalizeReasoningEffort_ClampsToKnownValues(string? input, string expected)
    {
        Assert.Equal(expected, AssistantPreferences.NormalizeReasoningEffort(input));
    }
}
