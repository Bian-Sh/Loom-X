using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 声明式 UI 交互协议（schema v2）的宿主校验与命令回传边界。
/// 注意：插件实例运行在独立 ALC，测试侧静态字段不会与插件侧共享，
/// 因此所有断言都通过命令返回值、快照内容与 Runtime 诊断来观察。
/// </summary>
public sealed class PluginUiInteractiveRuntimeTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-plugin-ui-interactive-" + Guid.NewGuid().ToString("N"));

    private string PluginDirectory => Path.Combine(rootDirectory, "plugins");

    private string DataDirectory => Path.Combine(rootDirectory, "data");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // collectible ALC 可能仍等待 GC，临时目录交由系统后续清理。
        }
    }

    [Fact]
    public void Runtime_IsolatesInteractiveNodesDeclaredAsSchemaV1()
    {
        StagePlugin(
            typeof(LegacyInteractiveTestPlugin),
            LegacyInteractiveTestPlugin.PluginIdValue,
            """
            "ui": { "contributions": [ { "id": "panel", "slot": "detail-body", "interactive": true } ] },
            """);

        var runtime = StartRuntime();

        Assert.Empty(runtime.GetUiContributions(
            LegacyInteractiveTestPlugin.PluginIdValue, PluginUiSlot.DetailBody, "zh-CN"));
        Assert.Contains(
            runtime.Diagnostics,
            item => item.Contains("INTERACTIVE_NODE_REQUIRES_V2", StringComparison.Ordinal));
        // UI 被隔离不影响插件 Runtime 能力
        Assert.NotNull(runtime.GetPipeline("request"));
    }

    [Fact]
    public async Task Runtime_RejectsCommandsWhenContributionIsNotDeclaredInteractive()
    {
        StagePlugin(
            typeof(InteractiveTestPlugin),
            InteractiveTestPlugin.PluginIdValue,
            """
            "ui": { "contributions": [ { "id": "panel", "slot": "detail-body" } ] },
            """);

        var runtime = StartRuntime();

        var contribution = Assert.Single(runtime.GetUiContributions(
            InteractiveTestPlugin.PluginIdValue, PluginUiSlot.DetailBody, "zh-CN"));
        Assert.Equal("panel", contribution.Id);

        var result = await runtime.ExecuteUiCommandAsync(
            InteractiveTestPlugin.PluginIdValue, PluginUiCommand.Create("panel", "submit"));

        Assert.False(result.Handled);
    }

    [Fact]
    public async Task Runtime_RoutesCommandsForDeclaredInteractiveContribution()
    {
        StagePlugin(
            typeof(InteractiveTestPlugin),
            InteractiveTestPlugin.PluginIdValue,
            """
            "ui": { "contributions": [ { "id": "panel", "slot": "detail-body", "interactive": true } ] },
            """);

        var runtime = StartRuntime();

        var info = Assert.Single(runtime.PluginInfos);
        Assert.Contains("panel", info.InteractiveUiContributions ?? []);

        var result = await runtime.ExecuteUiCommandAsync(
            InteractiveTestPlugin.PluginIdValue,
            PluginUiCommand.Create(
                "panel",
                "submit",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = "值" }));

        Assert.True(result.Handled);
        Assert.Equal(PluginUiTone.Success, result.Tone);
        Assert.Equal("ok:submit", result.Message);
        Assert.Empty(runtime.Diagnostics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Runtime_RejectsBlankCommandIds(string commandId)
    {
        StagePlugin(
            typeof(InteractiveTestPlugin),
            InteractiveTestPlugin.PluginIdValue,
            """
            "ui": { "contributions": [ { "id": "panel", "slot": "detail-body", "interactive": true } ] },
            """);

        var runtime = StartRuntime();
        var result = await runtime.ExecuteUiCommandAsync(
            InteractiveTestPlugin.PluginIdValue, PluginUiCommand.Create("panel", commandId));

        Assert.False(result.Handled);
    }

    [Fact]
    public async Task Runtime_RejectsOversizedCommandArguments()
    {
        StagePlugin(
            typeof(InteractiveTestPlugin),
            InteractiveTestPlugin.PluginIdValue,
            """
            "ui": { "contributions": [ { "id": "panel", "slot": "detail-body", "interactive": true } ] },
            """);

        var runtime = StartRuntime();
        var result = await runtime.ExecuteUiCommandAsync(
            InteractiveTestPlugin.PluginIdValue,
            PluginUiCommand.Create(
                "panel",
                "submit",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = new string('x', 5000) }));

        Assert.False(result.Handled);
    }

    [Fact]
    public async Task Runtime_IsolatesFailingCommandHandlers()
    {
        StagePlugin(
            typeof(InteractiveTestPlugin),
            InteractiveTestPlugin.PluginIdValue,
            """
            "ui": { "contributions": [ { "id": "panel", "slot": "detail-body", "interactive": true } ] },
            """);

        var runtime = StartRuntime();
        var result = await runtime.ExecuteUiCommandAsync(
            InteractiveTestPlugin.PluginIdValue, PluginUiCommand.Create("panel", "boom"));

        Assert.False(result.Handled);
        Assert.Contains(
            runtime.Diagnostics,
            item => item.Contains("COMMAND_FAILURE", StringComparison.Ordinal));

        // 命令失败不影响插件其余能力
        var pipelineResult = await runtime.GetPipeline("request")!.ExecuteAsync("payload");
        Assert.Equal(PipelineOutcome.Passed, pipelineResult.Outcome);
    }

    private void StagePlugin(Type pluginType, string pluginId, string uiFragment)
    {
        var directory = Path.Combine(PluginDirectory, pluginId);
        Directory.CreateDirectory(directory);
        var sourceAssembly = pluginType.Assembly.Location;
        var assemblyName = Path.GetFileName(sourceAssembly);
        File.Copy(sourceAssembly, Path.Combine(directory, assemblyName), overwrite: true);
        var depsSource = Path.ChangeExtension(sourceAssembly, ".deps.json");
        if (File.Exists(depsSource))
            File.Copy(depsSource, Path.Combine(directory, Path.GetFileName(depsSource)), overwrite: true);

        File.WriteAllText(
            Path.Combine(directory, ManifestParser.ManifestFileName),
            $$"""
            {
              "id": "{{pluginId}}",
              "version": "1.0.0",
              "assembly": "{{assemblyName}}",
              "plugin_type": "{{pluginType.FullName}}",
              "capabilities": ["test.ui"],
              {{uiFragment}}
              "extensions": [
                {
                  "id": "test.request",
                  "kind": "request",
                  "pipeline": "request",
                  "failure_policy": "continue-on-error",
                  "capabilities": []
                }
              ]
            }
            """);
    }

    private PluginRuntime StartRuntime() => PluginRuntime.Start(
        new PluginRuntimeOptions
        {
            PluginDirectory = PluginDirectory,
            DataRootDirectory = DataDirectory,
        },
        NullLoggerFactory.Instance);
}

/// <summary>v2 交互插件：命令结果里回显命令标识，便于在宿主侧断言回传链路。</summary>
public sealed class InteractiveTestPlugin : ILoomXPlugin, IPluginUiContributionProvider, IPluginUiCommandHandler
{
    public const string PluginIdValue = "test.interactive";

    public string Id => PluginIdValue;

    public event EventHandler? UiInvalidated;

    public void Initialize(PluginInitializationContext context) { }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        yield return new InteractivePassExtension();
    }

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) =>
    [
        new(
            PluginUiContribution.CurrentSchemaVersion,
            "panel",
            PluginUiSlot.DetailBody,
            new PluginUiStackNode(
                [
                    new PluginUiTextFieldNode("note", "备注", "初始值"),
                    new PluginUiToggleFieldNode("switch", "开关", true, CommandId: "switch"),
                    new PluginUiButtonNode("提交", "submit"),
                    new PluginUiChoiceFieldNode("mode", "模式", ["A", "B"], "A", CommandId: "mode"),
                ],
                Spacing: 6)),
    ];

    public ValueTask<PluginUiCommandResult> ExecuteUiCommandAsync(
        PluginUiCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(command.CommandId, "boom", StringComparison.Ordinal))
            throw new InvalidOperationException("命令处理失败");

        return ValueTask.FromResult(PluginUiCommandResult.Success($"ok:{command.CommandId}"));
    }

    private sealed class InteractivePassExtension : IRequestExtension
    {
        public string ExtensionId => "test.request";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => [];

        public ValueTask<PipelineResult> ProcessRequestAsync(
            PipelineContext context,
            string payload,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}

/// <summary>声明 v1 却返回交互节点的插件：宿主必须隔离其 UI，但保留其 Runtime 能力。</summary>
public sealed class LegacyInteractiveTestPlugin : ILoomXPlugin, IPluginUiContributionProvider
{
    public const string PluginIdValue = "test.interactive.v1";

    public string Id => PluginIdValue;

    public event EventHandler? UiInvalidated;

    public void Initialize(PluginInitializationContext context) { }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        yield return new LegacyInteractivePassExtension();
    }

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) =>
    [
        new(
            1,
            "panel",
            PluginUiSlot.DetailBody,
            new PluginUiStackNode([new PluginUiButtonNode("提交", "submit")])),
    ];

    private sealed class LegacyInteractivePassExtension : IRequestExtension
    {
        public string ExtensionId => "test.request";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => [];

        public ValueTask<PipelineResult> ProcessRequestAsync(
            PipelineContext context,
            string payload,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}
