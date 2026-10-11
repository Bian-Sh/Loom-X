using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.ToolResultCompression;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 压缩插件设置页：节省对比、强度预设与策略开关，以及跨会话的统计累计。
/// </summary>
public sealed class ToolResultCompressionSettingsTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> Metadata =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["api_mode"] = "openai" };

    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-trc-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果。
        }
    }

    private ToolResultCompressionPlugin CreatePlugin()
    {
        Directory.CreateDirectory(dataDirectory);
        var plugin = new ToolResultCompressionPlugin();
        plugin.Initialize(new PluginInitializationContext(ToolResultCompressionPlugin.PluginId, dataDirectory));
        plugin.CreateExtensions().ToArray();
        return plugin;
    }

    private static async Task<PluginUiCommandResult> RunAsync(
        ToolResultCompressionPlugin plugin,
        string commandId,
        params (string Key, string Value)[] arguments)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            map[key] = value;

        return await plugin.ExecuteUiCommandAsync(
            PluginUiCommand.Create(CompressionSettingsUi.SettingsContributionId, commandId, map));
    }

    private static string LongToolResult(int lines = 200) =>
        string.Join('\n', Enumerable.Repeat("INFO  heartbeat ok", lines));

    private static string BuildPayload()
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "go" },
            new JsonObject { ["role"] = "tool", ["content"] = LongToolResult() },
            new JsonObject { ["role"] = "tool", ["content"] = LongToolResult() },
            new JsonObject { ["role"] = "tool", ["content"] = LongToolResult() },
        };
        return new JsonObject { ["model"] = "m", ["messages"] = messages }.ToJsonString();
    }

    [Fact]
    public void SettingsPage_OffersCardAndInteractiveDetailBody()
    {
        var plugin = CreatePlugin();
        var contributions = plugin.GetUiContributions(new PluginUiContext("zh-CN"));

        Assert.Equal(2, contributions.Count);
        var card = Assert.Single(contributions, item => item.Slot == PluginUiSlot.CardBody);
        Assert.Equal(ToolResultCompressionPlugin.OverviewContributionId, card.Id);

        var detail = Assert.Single(contributions, item => item.Slot == PluginUiSlot.DetailBody);
        Assert.Equal(ToolResultCompressionPlugin.SettingsContributionId, detail.Id);

        // 交互节点只在 v2 下合法；声明为 interactive 的贡献必须整棵通过校验。
        Assert.True(PluginUiValidator.Validate(detail).IsValid);
        Assert.True(PluginUiValidator.Validate(card).IsValid);
    }

    [Fact]
    public async Task ToggleEnabled_WritesConfigAndIsReportedBack()
    {
        var plugin = CreatePlugin();

        var disabled = await RunAsync(plugin, CompressionSettingsUi.ToggleEnabled,
            (CompressionSettingsUi.EnabledField, "false"));

        Assert.True(disabled.Handled);
        Assert.False(plugin.OptionsStore.Current.Enabled);

        var enabled = await RunAsync(plugin, CompressionSettingsUi.ToggleEnabled,
            (CompressionSettingsUi.EnabledField, "true"));

        Assert.True(enabled.Handled);
        Assert.True(plugin.OptionsStore.Current.Enabled);
    }

    [Fact]
    public async Task SetLevel_SwitchesPresetNumbers()
    {
        var plugin = CreatePlugin();

        await RunAsync(plugin, CompressionSettingsUi.SetLevel,
            (CompressionSettingsUi.LevelField, CompressionLevel.Aggressive));

        var aggressive = plugin.OptionsStore.Current;
        Assert.Equal(ToolResultCompressionOptions.AggressivePreset.MinChars, aggressive.MinChars);
        Assert.Equal(CompressionLevel.Aggressive, aggressive.MatchLevel());

        await RunAsync(plugin, CompressionSettingsUi.SetLevel,
            (CompressionSettingsUi.LevelField, CompressionLevel.Conservative));

        Assert.Equal(CompressionLevel.Conservative, plugin.OptionsStore.Current.MatchLevel());
    }

    [Fact]
    public async Task ApplyPolicy_UpdatesFidelityWindowAndThreshold()
    {
        var plugin = CreatePlugin();

        var result = await RunAsync(plugin, CompressionSettingsUi.ApplyPolicy,
            (CompressionSettingsUi.KeepRecentField, "5"),
            (CompressionSettingsUi.MinSavingField, "25"));

        Assert.True(result.Handled);
        Assert.Equal(5, plugin.OptionsStore.Current.KeepRecentToolResults);
        Assert.Equal(0.25, plugin.OptionsStore.Current.MinSavingsRatio, 6);
    }

    [Fact]
    public async Task ApplyPolicy_RejectsNonNumericInput()
    {
        var plugin = CreatePlugin();

        var result = await RunAsync(plugin, CompressionSettingsUi.ApplyPolicy,
            (CompressionSettingsUi.KeepRecentField, "many"),
            (CompressionSettingsUi.MinSavingField, "12"));

        Assert.True(result.Handled);
        Assert.Equal(PluginUiTone.Danger, result.Tone);
        Assert.Equal(ToolResultCompressionOptions.Default.KeepRecentToolResults,
            plugin.OptionsStore.Current.KeepRecentToolResults);
    }

    [Fact]
    public async Task ResetStats_ClearsCountersButKeepsConfig()
    {
        var plugin = CreatePlugin();
        // 保真窗口要小于工具结果条数，否则末尾几条被保留、本次请求无可压缩内容。
        await RunAsync(plugin, CompressionSettingsUi.ApplyPolicy,
            (CompressionSettingsUi.KeepRecentField, "1"),
            (CompressionSettingsUi.MinSavingField, "10"));

        var extension = (IRequestExtension)plugin.CreateExtensions().Single();
        await extension.ProcessRequestAsync(
            new PipelineContext("request", ToolResultCompressionPlugin.PluginId, "toolresult.request", Metadata),
            BuildPayload(),
            CancellationToken.None);

        Assert.True(plugin.Observability.Snapshot().SavedChars > 0);

        var result = await RunAsync(plugin, CompressionSettingsUi.ResetStats);

        Assert.True(result.Handled);
        var stats = plugin.Observability.Snapshot();
        Assert.Equal(0, stats.SavedChars);
        Assert.Equal(0, stats.SavedTokens);
        // 统计重置不影响配置。
        Assert.Equal(1, plugin.OptionsStore.Current.KeepRecentToolResults);
    }

    [Fact]
    public async Task Stats_AccumulateAcrossPluginRestarts()
    {
        var first = CreatePlugin();
        var extension = (IRequestExtension)first.CreateExtensions().Single();
        await extension.ProcessRequestAsync(
            new PipelineContext("request", ToolResultCompressionPlugin.PluginId, "toolresult.request", Metadata),
            BuildPayload(),
            CancellationToken.None);

        var before = first.Observability.Snapshot();
        Assert.True(before.SavedChars > 0);

        // 重新 Initialize 相当于重启应用：统计应从插件自有目录续上。
        var second = new ToolResultCompressionPlugin();
        second.Initialize(new PluginInitializationContext(ToolResultCompressionPlugin.PluginId, dataDirectory));

        var after = second.Observability.Snapshot();
        Assert.Equal(before.TotalRequests, after.TotalRequests);
        Assert.Equal(before.SavedChars, after.SavedChars);
        Assert.Equal(before.SavedTokens, after.SavedTokens);
        Assert.NotEmpty(after.RecentEntries);
    }

    [Fact]
    public void TokenEstimator_CountsCjkDenserThanLatin()
    {
        var latin = CompressionTokenEstimator.Estimate(new string('a', 400));
        var cjk = CompressionTokenEstimator.Estimate(new string('中', 400));

        Assert.Equal(100, latin);
        Assert.True(cjk > latin);
    }

    [Fact]
    public async Task UnknownCommand_IsReportedAsUnhandled()
    {
        var plugin = CreatePlugin();

        var result = await RunAsync(plugin, "no_such_command");

        Assert.False(result.Handled);
    }
}
