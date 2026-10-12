using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.ToolResultCompression;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class ToolResultCompressionPluginTests
{
    private static readonly IReadOnlyDictionary<string, string> Metadata =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["api_mode"] = "openai" };

    private static string RepeatedToolResult(string tag) =>
        string.Join('\n', Enumerable.Repeat($"INFO  {tag} heartbeat ok", 200));

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loomx-trc-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ToolResultCompressionPlugin CreateInitializedPlugin(string? directory = null)
    {
        var plugin = new ToolResultCompressionPlugin();
        plugin.Initialize(new PluginInitializationContext(
            ToolResultCompressionPlugin.PluginId,
            directory ?? CreateTempDirectory()));
        return plugin;
    }

    private static string BuildRequestPayload()
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "run it" },
            new JsonObject { ["role"] = "tool", ["tool_call_id"] = "call_1", ["content"] = RepeatedToolResult("alpha") },
            new JsonObject { ["role"] = "tool", ["tool_call_id"] = "call_2", ["content"] = RepeatedToolResult("beta") },
            new JsonObject { ["role"] = "tool", ["tool_call_id"] = "call_3", ["content"] = RepeatedToolResult("gamma") },
        };
        return new JsonObject { ["model"] = "m", ["messages"] = messages }.ToJsonString();
    }

    [Fact]
    public void Plugin_RegistersSingleRequestExtension()
    {
        var plugin = CreateInitializedPlugin();

        var extensions = plugin.CreateExtensions().ToArray();

        Assert.Single(extensions);
        var extension = extensions[0];
        Assert.Equal("toolresult.request", extension.ExtensionId);
        Assert.Equal(ExtensionKind.Request, extension.Kind);
        Assert.Equal(ExtensionFailurePolicy.ContinueOnError, extension.FailurePolicy);
        Assert.Contains("toolresult.compress", extension.Capabilities);
    }

    [Fact]
    public async Task Extension_CompressesToolResultsInRequestPayload()
    {
        var plugin = CreateInitializedPlugin();
        var extension = (IRequestExtension)plugin.CreateExtensions().Single();
        var context = new PipelineContext("request", ToolResultCompressionPlugin.PluginId, "toolresult.request", Metadata);

        var result = await extension.ProcessRequestAsync(context, BuildRequestPayload(), CancellationToken.None);

        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        var parsed = JsonNode.Parse(result.Payload)!.AsObject();
        Assert.Contains("×200", parsed["messages"]![1]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain("×200", parsed["messages"]![3]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extension_PassesOriginalPayloadThroughWhenNothingCompressible()
    {
        var plugin = CreateInitializedPlugin();
        var extension = (IRequestExtension)plugin.CreateExtensions().Single();
        var context = new PipelineContext("request", ToolResultCompressionPlugin.PluginId, "toolresult.request", Metadata);
        var payload = new string('x', 5000);

        var result = await extension.ProcessRequestAsync(context, payload, CancellationToken.None);

        Assert.Equal(PipelineOutcome.Passed, result.Outcome);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public async Task Extension_PassesOriginalPayloadThroughWhenPluginDisabled()
    {
        var directory = CreateTempDirectory();
        var plugin = CreateInitializedPlugin(directory);
        plugin.OptionsStore.Save(ToolResultCompressionOptions.Default with { Enabled = false });
        var extension = (IRequestExtension)plugin.CreateExtensions().Single();
        var context = new PipelineContext("request", ToolResultCompressionPlugin.PluginId, "toolresult.request", Metadata);
        var payload = BuildRequestPayload();

        var result = await extension.ProcessRequestAsync(context, payload, CancellationToken.None);

        Assert.Equal(PipelineOutcome.Passed, result.Outcome);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Plugin_WritesDefaultConfigIntoPluginDataDirectory()
    {
        var directory = CreateTempDirectory();

        var plugin = CreateInitializedPlugin(directory);

        Assert.True(File.Exists(plugin.OptionsStore.ConfigPath));
        Assert.True(plugin.OptionsStore.Current.Enabled);
    }

    [Fact]
    public void Plugin_ProvidesCardAndDetailUiSnapshots()
    {
        var plugin = CreateInitializedPlugin();

        var contributions = plugin.GetUiContributions(new PluginUiContext("zh-CN"));

        Assert.Equal(2, contributions.Count);
        var overview = contributions[0];
        Assert.Equal("observability", overview.Id);
        Assert.Equal(PluginUiSlot.CardBody, overview.Slot);
        Assert.Equal(PluginUiContribution.CurrentSchemaVersion, overview.SchemaVersion);
        Assert.IsType<PluginUiGridNode>(overview.Root);

        var settings = contributions[1];
        Assert.Equal("settings", settings.Id);
        Assert.Equal(PluginUiSlot.DetailBody, settings.Slot);
        Assert.IsType<PluginUiStackNode>(settings.Root);
    }

    [Fact]
    public void Observability_CountsCompressedRequestsAndSavedChars()
    {
        var observability = new ToolResultCompressionObservability();

        observability.RecordRequest(new ToolResultRewriteReport(3, 2, 10_000, 4_000, [ToolResultCompressor.StrategyDedupe]));
        observability.RecordRequest();
        observability.RecordError();

        var snapshot = observability.Snapshot();
        Assert.Equal(2, snapshot.TotalRequests);
        Assert.Equal(1, snapshot.CompressedRequests);
        Assert.Equal(2, snapshot.CompressedToolResults);
        Assert.Equal(6000, snapshot.SavedChars);
        Assert.Equal(0.6, snapshot.SavingsRatio, 3);
        Assert.Equal(1, snapshot.Errors);
    }

    [Fact]
    public async Task PluginRuntime_LoadsPluginAndCompressesThroughRequestPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), "loomx-trc-runtime", Guid.NewGuid().ToString("n"));
        FirstPartySourceTestPayload.Stage(Path.Combine(root, "plugins"), ToolResultCompressionPlugin.PluginId);

        var runtime = PluginRuntime.Start(
            new PluginRuntimeOptions
            {
                PluginDirectory = Path.Combine(root, "plugins"),
                DataRootDirectory = Path.Combine(root, "data"),
                SourceCompiler = new SourcePluginCompiler(Path.Combine(root, "cache")),
            },
            NullLoggerFactory.Instance);

        var pipeline = runtime.GetPipeline("request");
        Assert.NotNull(pipeline);

        var result = await ((IContextualPipeline)pipeline!).ExecuteAsync(
            BuildRequestPayload(), Metadata, CancellationToken.None);

        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        Assert.Empty(runtime.Diagnostics);
        Assert.Contains(
            runtime.PluginInfos,
            info => info.Id == ToolResultCompressionPlugin.PluginId && info.HasCardUi);
    }

    [Fact]
    public async Task PluginRuntime_ForwardsInteractiveSettingsCommand()
    {
        var (runtime, dataRoot) = StartRuntimeWithPlugin();

        var result = await runtime.ExecuteUiCommandAsync(
            ToolResultCompressionPlugin.PluginId,
            PluginUiCommand.Create(
                CompressionSettingsUi.SettingsContributionId,
                CompressionSettingsUi.ToggleEnabled,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CompressionSettingsUi.EnabledField] = "false",
                }),
            CancellationToken.None);

        Assert.True(result.Handled);
        // 命令经宿主转发后由插件落库：配置写在插件自有数据目录，不碰宿主配置。
        var configPath = Path.Combine(
            dataRoot, ToolResultCompressionPlugin.PluginId, "config.json");
        Assert.True(File.Exists(configPath));
        Assert.False(ToolResultCompressionOptions.FromJson(File.ReadAllText(configPath))!.Enabled);
        Assert.Empty(runtime.Diagnostics);
    }

    private static (PluginRuntime Runtime, string DataRoot) StartRuntimeWithPlugin()
    {
        var root = Path.Combine(Path.GetTempPath(), "loomx-trc-runtime", Guid.NewGuid().ToString("n"));
        FirstPartySourceTestPayload.Stage(Path.Combine(root, "plugins"), ToolResultCompressionPlugin.PluginId);

        return (PluginRuntime.Start(
            new PluginRuntimeOptions
            {
                PluginDirectory = Path.Combine(root, "plugins"),
                DataRootDirectory = Path.Combine(root, "data"),
                SourceCompiler = new SourcePluginCompiler(Path.Combine(root, "cache")),
            },
            NullLoggerFactory.Instance), Path.Combine(root, "data"));
    }

}
