using Xunit;
using LoomX.Assistant;

namespace LoomX.Tests.Assistant;

/// <summary>
/// 助手偏好持久化：读写往返、损坏回落默认、思考等级归一化。
/// </summary>
public sealed class AssistantPreferencesStoreTests : IDisposable
{
    private readonly string filePath = Path.Combine(Path.GetTempPath(), $"loomx-assistant-prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(filePath)) File.Delete(filePath); } catch (IOException) { }
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var store = new AssistantPreferencesStore(filePath);

        var preferences = store.Load();

        Assert.Null(preferences.ProviderBusinessId);
        Assert.Null(preferences.ModelId);
        Assert.Equal(AssistantPreferences.DefaultReasoningEffort, preferences.ReasoningEffort);
        Assert.Equal(AssistantPermissionMode.AutoApprove, preferences.PermissionMode);
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var store = new AssistantPreferencesStore(filePath);
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
    public void CorruptedFile_ReturnsDefaults()
    {
        File.WriteAllText(filePath, "{ not json !!!");
        var store = new AssistantPreferencesStore(filePath);

        var preferences = store.Load();

        Assert.Equal(AssistantPermissionMode.AutoApprove, preferences.PermissionMode);
        Assert.Null(preferences.ModelId);
    }

    [Fact]
    public void Update_MutatesAtomically()
    {
        var store = new AssistantPreferencesStore(filePath);
        store.Save(new AssistantPreferences { ModelId = "gpt-4o", ProviderBusinessId = "p1" });

        var updated = store.Update(current => current with { PermissionMode = AssistantPermissionMode.AskEachTime });

        Assert.Equal(AssistantPermissionMode.AskEachTime, updated.PermissionMode);
        var loaded = store.Load();
        Assert.Equal(AssistantPermissionMode.AskEachTime, loaded.PermissionMode);
        Assert.Equal("gpt-4o", loaded.ModelId);
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
