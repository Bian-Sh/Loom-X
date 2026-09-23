using LoomX.Plugins;

namespace LoomX.Tests.Plugins;

public sealed class RuntimeUiTestPlugin : ILoomXPlugin, IPluginUiContributionProvider
{
    public string Id => "test.ui";

    public event EventHandler? UiInvalidated;

    public void Initialize(PluginInitializationContext context) { }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        yield return new InvalidatingRequestExtension(() => UiInvalidated?.Invoke(this, EventArgs.Empty));
    }

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) =>
    [
        new(
            PluginUiContribution.CurrentSchemaVersion,
            "summary",
            PluginUiSlot.CardBody,
            new PluginUiTextNode($"{context.CultureName}:42", PluginUiTextRole.Metric)),
        new(
            PluginUiContribution.CurrentSchemaVersion,
            "undeclared",
            PluginUiSlot.DetailBody,
            new PluginUiTextNode("不应渲染")),
    ];

    private sealed class InvalidatingRequestExtension(Action invalidate) : IRequestExtension
    {
        public string ExtensionId => "test.request";
        public ExtensionKind Kind => ExtensionKind.Request;
        public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
        public IReadOnlyList<string> Capabilities => [];

        public ValueTask<PipelineResult> ProcessRequestAsync(
            PipelineContext context,
            string payload,
            CancellationToken cancellationToken)
        {
            invalidate();
            return ValueTask.FromResult(PipelineResult.Pass(payload));
        }
    }
}

public sealed class OversizedRuntimeUiTestPlugin : ILoomXPlugin, IPluginUiContributionProvider
{
    public const string SensitiveMarker = "sensitive-marker-must-not-enter-diagnostics";

    public string Id => "test.ui.oversized";

    public event EventHandler? UiInvalidated;

    public void Initialize(PluginInitializationContext context) { }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        yield return new PassRequestExtension();
    }

    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context) =>
    [
        new(
            PluginUiContribution.CurrentSchemaVersion,
            "summary",
            PluginUiSlot.CardBody,
            new PluginUiTextNode(SensitiveMarker + new string('x', 5000))),
    ];

    private sealed class PassRequestExtension : IRequestExtension
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
