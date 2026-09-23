using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginRuntimeUiTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-plugin-ui-runtime-" + Guid.NewGuid().ToString("N"));

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
    public async Task Runtime_ValidatesDeclaredContributions_AndProxiesInvalidation()
    {
        StagePlugin(
            "test.ui",
            typeof(RuntimeUiTestPlugin),
            """
            "ui": { "contributions": [ { "id": "summary", "slot": "card-body" } ] },
            """);
        var runtime = StartRuntime();
        string? invalidatedPluginId = null;
        runtime.PluginUiInvalidated += (_, args) => invalidatedPluginId = args.PluginId;

        var info = Assert.Single(runtime.PluginInfos);
        var contribution = Assert.Single(runtime.GetUiContributions("test.ui", PluginUiSlot.CardBody, "zh-CN"));
        await runtime.GetPipeline("request")!.ExecuteAsync("payload");

        Assert.True(info.HasCardUi);
        Assert.False(info.HasDetailUi);
        Assert.Equal("summary", contribution.Id);
        Assert.Equal("zh-CN:42", Assert.IsType<PluginUiTextNode>(contribution.Root).Text);
        Assert.Equal("test.ui", invalidatedPluginId);
        Assert.Empty(runtime.GetUiContributions("test.ui", PluginUiSlot.DetailBody, "zh-CN"));
        Assert.Contains(runtime.Diagnostics, item => item.Contains("undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_RejectsOversizedNodes_WithoutLeakingNodeText()
    {
        StagePlugin(
            "test.ui.oversized",
            typeof(OversizedRuntimeUiTestPlugin),
            """
            "ui": { "contributions": [ { "id": "summary", "slot": "card-body" } ] },
            """);
        var runtime = StartRuntime();

        var contributions = runtime.GetUiContributions(
            "test.ui.oversized",
            PluginUiSlot.CardBody,
            "zh-CN");

        Assert.Empty(contributions);
        Assert.Contains(runtime.Diagnostics, item => item.Contains("TEXT_TOO_LONG", StringComparison.Ordinal));
        Assert.DoesNotContain(
            runtime.Diagnostics,
            item => item.Contains(OversizedRuntimeUiTestPlugin.SensitiveMarker, StringComparison.Ordinal));
        Assert.NotNull(runtime.GetPipeline("request"));
    }

    private void StagePlugin(string pluginId, Type pluginType, string uiFragment)
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
