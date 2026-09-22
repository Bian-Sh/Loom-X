using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>插件目录发现与 Manifest 验证（spec: plugin-runtime-pipeline / 插件发现与 Manifest 验证）。</summary>
public sealed class PluginCatalogTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-plugin-catalog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
    }

    private string WritePlugin(string directoryName, string manifestJson)
    {
        var directory = Path.Combine(rootDirectory, directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ManifestParser.ManifestFileName), manifestJson);
        return directory;
    }

    private const string ValidManifest = """
        {
          "id": "demo.plugin",
          "version": "1.0.0",
          "assembly": "Demo.Plugin.dll",
          "plugin_type": "Demo.Plugin.Plugin",
          "capabilities": ["demo.capability"],
          "extensions": [
            { "id": "demo.request", "kind": "request", "pipeline": "request",
              "failure_policy": "fail-closed", "capabilities": ["demo.capability"] }
          ]
        }
        """;

    [Fact]
    public void ValidPlugin_IsDiscoveredAndValidated()
    {
        WritePlugin("demo", ValidManifest);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        var plugin = Assert.Single(discovered);
        Assert.Equal("demo.plugin", plugin.Manifest.Id);
        Assert.Equal("1.0.0", plugin.Manifest.Version);
        var extension = Assert.Single(plugin.Manifest.Extensions);
        Assert.Equal(ExtensionKind.Request, extension.Kind);
        Assert.Equal("request", extension.Pipeline);
        Assert.Equal(ExtensionFailurePolicy.FailClosed, extension.FailurePolicy);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MissingId_RejectedAndIsolated()
    {
        WritePlugin("bad", """
            { "version": "1.0.0", "assembly": "A.dll", "plugin_type": "A.B",
              "capabilities": ["x"],
              "extensions": [ { "id": "e", "kind": "request", "pipeline": "request" } ] }
            """);
        WritePlugin("good", ValidManifest);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        var plugin = Assert.Single(discovered);
        Assert.Equal("demo.plugin", plugin.Manifest.Id);
        Assert.Contains(diagnostics, item => item.Contains("id"));
    }

    [Fact]
    public void MissingExtensions_Rejected()
    {
        WritePlugin("bad", """
            { "id": "demo.bad", "version": "1.0.0", "assembly": "A.dll", "plugin_type": "A.B",
              "capabilities": ["x"] }
            """);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        Assert.Empty(discovered);
        Assert.Contains(diagnostics, item => item.Contains("extensions"));
    }

    [Fact]
    public void MissingCapabilities_Rejected()
    {
        WritePlugin("bad", """
            { "id": "demo.bad", "version": "1.0.0", "assembly": "A.dll", "plugin_type": "A.B",
              "extensions": [ { "id": "e", "kind": "request", "pipeline": "request" } ] }
            """);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        Assert.Empty(discovered);
        Assert.Contains(diagnostics, item => item.Contains("capabilities"));
    }

    [Fact]
    public void InvalidJson_RejectedAndIsolated()
    {
        WritePlugin("broken", "{ not json");
        WritePlugin("good", ValidManifest);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        Assert.Single(discovered);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void DirectoryWithoutManifest_IsSkipped()
    {
        Directory.CreateDirectory(Path.Combine(rootDirectory, "random-folder"));
        WritePlugin("good", ValidManifest);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        Assert.Single(discovered);
        Assert.Contains(diagnostics, item => item.Contains("random-folder"));
    }

    [Fact]
    public void DuplicatePluginIds_AllRejected()
    {
        WritePlugin("one", ValidManifest);
        WritePlugin("two", ValidManifest);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        Assert.Empty(discovered);
        Assert.Contains(diagnostics, item => item.Contains("重复"));
    }

    [Fact]
    public void UnknownExtensionKind_Rejected()
    {
        WritePlugin("bad", """
            { "id": "demo.bad", "version": "1.0.0", "assembly": "A.dll", "plugin_type": "A.B",
              "capabilities": ["x"],
              "extensions": [ { "id": "e", "kind": "telemetry", "pipeline": "persistence" } ] }
            """);
        var diagnostics = new List<string>();

        var discovered = PluginCatalog.Discover(rootDirectory, diagnostics);

        Assert.Empty(discovered);
        Assert.Contains(diagnostics, item => item.Contains("kind"));
    }
}
