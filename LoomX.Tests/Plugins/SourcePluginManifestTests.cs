using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>源码 Manifest、宿主能力声明及旧程序集入口的兼容约束。</summary>
public sealed class SourcePluginManifestTests
{
    private const string LegacyJson = """
        { "id": "demo.plugin", "version": "1.0.0", "assembly": "Demo.dll",
          "plugin_type": "Demo.Plugin", "capabilities": ["demo"],
          "extensions": [{ "id": "demo.request", "kind": "request", "pipeline": "request" }] }
        """;
    private const string SourceJson = """
        { "id": "demo.source", "version": "1.0.0",
          "runtime": { "kind": "csharp-source", "entry_type": "Demo.Plugin",
                       "sources": ["src/Models.cs", "src/Plugin.cs"] },
          "host_capabilities": ["storage.sqlite/v1", "logging/v1", "notifications/v1"],
          "capabilities": [], "extensions": [] }
        """;

    [Fact]
    public void SourceRuntime_ParsesExplicitSourcesAndEmptyExtensionTemplate()
    {
        var manifest = ParseValid(SourceJson);
        var runtime = Property<object>(manifest, "Runtime");

        Assert.Equal("csharp-source", Property<string>(runtime, "Kind"));
        Assert.Equal("Demo.Plugin", Property<string>(runtime, "EntryType"));
        Assert.Null(runtime.GetType().GetProperty("Assembly")!.GetValue(runtime));
        Assert.Equal(new[] { "src/Models.cs", "src/Plugin.cs" }, Property<IReadOnlyList<string>>(runtime, "Sources"));
        Assert.Equal(new[] { "storage.sqlite/v1", "logging/v1", "notifications/v1" },
            Property<IReadOnlyList<string>>(manifest, "HostCapabilities"));
        Assert.Empty(manifest.Capabilities);
        Assert.Empty(manifest.Extensions);
        Assert.Equal(string.Empty, manifest.Assembly);
        Assert.Equal("Demo.Plugin", manifest.PluginType);
    }

    [Fact]
    public void LegacyAssembly_NormalizesRuntimeWithoutChangingExistingFields()
    {
        var manifest = ParseValid(LegacyJson);
        var runtime = Property<object>(manifest, "Runtime");

        Assert.Equal("assembly", Property<string>(runtime, "Kind"));
        Assert.Equal(manifest.Assembly, Property<string>(runtime, "Assembly"));
        Assert.Equal(manifest.PluginType, Property<string>(runtime, "EntryType"));
        Assert.Equal("Demo.dll", manifest.Assembly);
        Assert.Equal("Demo.Plugin", manifest.PluginType);
        Assert.Null(runtime.GetType().GetProperty("Sources")!.GetValue(runtime));
        Assert.Empty(Property<IReadOnlyList<string>>(manifest, "HostCapabilities"));
        Assert.Single(manifest.Extensions);
    }

    [Fact]
    public void AssemblyRuntime_ParsesSnakeCaseEntryAndPreservesLegacyLoadingFields()
    {
        var json = Legacy();
        json.Remove("assembly");
        json.Remove("plugin_type");
        json["runtime"] = JsonNode.Parse("""
            { "kind": "assembly", "entry_type": "Demo.Plugin", "assembly": "Demo.dll" }
            """);

        var manifest = ParseValid(json.ToJsonString());

        Assert.Equal("Demo.dll", manifest.Assembly);
        Assert.Equal("Demo.Plugin", manifest.PluginType);
        Assert.Equal("assembly", Property<string>(Property<object>(manifest, "Runtime"), "Kind"));
    }

    [Fact]
    public void ManifestModel_PreservesPositionalConstructorAndDeconstruction()
    {
        var constructorTypes = new[] { typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(IReadOnlyList<string>), typeof(IReadOnlyList<PluginManifestExtension>), typeof(PluginManifestUi) };
        Assert.NotNull(typeof(PluginManifest).GetConstructor(constructorTypes));
        var manifest = new PluginManifest("demo", "1", "Demo.dll", "Demo.Plugin", [], []);
        var (id, version, assembly, pluginType, capabilities, extensions, ui) = manifest;

        Assert.Equal(("demo", "1", "Demo.dll", "Demo.Plugin"), (id, version, assembly, pluginType));
        Assert.Empty(capabilities);
        Assert.Empty(extensions);
        Assert.Null(ui);
        AssertInitProperty(typeof(PluginManifest), "Runtime");
        AssertInitProperty(typeof(PluginManifest), "HostCapabilities");
        Assert.Null(typeof(PluginManifest).GetProperty("Runtime")!.GetValue(manifest));
        Assert.Empty(Property<IReadOnlyList<string>>(manifest, "HostCapabilities"));
    }

    [Fact]
    public void RuntimeModel_IsValueRecordWithOptionalAssemblyAndSourcesAndNoHostBinding()
    {
        var type = typeof(PluginManifest).Assembly.GetType("LoomX.Plugins.PluginRuntimeManifest");
        Assert.NotNull(type);
        var constructor = type.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>) });
        Assert.NotNull(constructor);
        Assert.True(constructor.GetParameters()[2].HasDefaultValue);
        Assert.True(constructor.GetParameters()[3].HasDefaultValue);
        var first = constructor.Invoke(new object?[] { "assembly", "Demo.Plugin", "Demo.dll", null });
        var second = constructor.Invoke(new object?[] { "assembly", "Demo.Plugin", "Demo.dll", null });
        Assert.Equal(first, second);
        Assert.All(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), property =>
            Assert.True(property.PropertyType == typeof(string) || property.PropertyType == typeof(IReadOnlyList<string>)));
    }

    [Fact]
    public void ManifestModel_RoundTripsNewValuesWithSnakeCaseNaming()
    {
        var manifest = ParseValid(SourceJson);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var json = JsonSerializer.Serialize(manifest, options);
        var copy = JsonSerializer.Deserialize<PluginManifest>(json, options);

        Assert.NotNull(copy);
        var runtime = Property<object>(copy, "Runtime");
        Assert.Equal("Demo.Plugin", Property<string>(runtime, "EntryType"));
        Assert.Equal(Property<IReadOnlyList<string>>(Property<object>(manifest, "Runtime"), "Sources"),
            Property<IReadOnlyList<string>>(runtime, "Sources"));
        Assert.Equal(Property<IReadOnlyList<string>>(manifest, "HostCapabilities"),
            Property<IReadOnlyList<string>>(copy, "HostCapabilities"));
        Assert.Equal(json, JsonSerializer.Serialize(copy, options));
    }

    [Theory]
    [InlineData("""{}""", "kind")]
    [InlineData("""{"kind":"unknown"}""", "kind")]
    [InlineData("""{"kind":"CSharp-Source"}""", "kind")]
    [InlineData("""{"kind":"csharp-source","sources":["Plugin.cs"]}""", "entry_type")]
    [InlineData("""{"kind":"csharp-source","entry_type":"  ","sources":["Plugin.cs"]}""", "entry_type")]
    [InlineData("""{"kind":"csharp-source","entry_type":"Demo.Plugin"}""", "sources")]
    [InlineData("""{"kind":"csharp-source","entry_type":"Demo.Plugin","sources":null}""", "sources")]
    [InlineData("""{"kind":"csharp-source","entry_type":"Demo.Plugin","sources":[]}""", "sources")]
    [InlineData("""{"kind":"csharp-source","entry_type":"Demo.Plugin","sources":[""]}""", "sources")]
    [InlineData("""{"kind":"csharp-source","entry_type":"Demo.Plugin","sources":["  "]}""", "sources")]
    [InlineData("""{"kind":"csharp-source","entry_type":"Demo.Plugin","sources":[null]}""", "sources")]
    [InlineData("""{"kind":"assembly","entry_type":"Demo.Plugin"}""", "assembly")]
    [InlineData("""{"kind":"assembly","entry_type":"Demo.Plugin","assembly":"  "}""", "assembly")]
    [InlineData("""{"kind":"assembly","assembly":"Demo.dll"}""", "entry_type")]
    public void InvalidRuntime_IsRejectedWithFieldDiagnostic(string runtimeJson, string field)
    {
        var json = Source();
        json["runtime"] = JsonNode.Parse(runtimeJson);
        AssertRejected(json, field);
    }

    [Theory]
    [InlineData("assembly", """ "Legacy.dll" """)]
    [InlineData("plugin_type", """ "Legacy.Plugin" """)]
    [InlineData("assembly", """ "" """)]
    [InlineData("plugin_type", "null")]
    public void RuntimeAndLegacyEntryDeclarations_AreRejected(string field, string value)
    {
        var json = Source();
        json[field] = JsonNode.Parse(value);
        AssertRejected(json, "runtime");
    }

    [Theory]
    [InlineData("src/Plugin.cs", "src/Plugin.cs")]
    [InlineData("src/Plugin.cs", "SRC/PLUGIN.CS")]
    [InlineData("src/Plugin.cs", "src\\Plugin.cs")]
    public void DuplicateSources_AreRejected(string first, string second)
    {
        var json = Source();
        json["runtime"]!["sources"] = JsonSerializer.SerializeToNode(new[] { first, second });
        AssertRejected(json, "sources");
    }

    [Theory]
    [InlineData("logging")]
    [InlineData("logging/v0")]
    [InlineData("logging/v01")]
    [InlineData("logging/v-1")]
    [InlineData("logging/v+1")]
    [InlineData("logging/v1.0")]
    [InlineData("logging/V1")]
    [InlineData("logging/v１")]
    [InlineData("logging/v1\n")]
    [InlineData(" logging/v1")]
    [InlineData("logging /v1")]
    [InlineData("logging/v1 ")]
    [InlineData("/v1")]
    [InlineData("logging/extra/v1")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidHostCapability_IsRejectedEvenForLegacyManifest(string? capability)
    {
        var json = Legacy();
        json["host_capabilities"] = JsonSerializer.SerializeToNode(new[] { capability });
        AssertRejected(json, "host_capabilities");
    }

    [Fact]
    public void DuplicateHostCapabilities_AreRejectedInsteadOfSilentlyDeduplicated()
    {
        var json = Legacy();
        json["host_capabilities"] = JsonSerializer.SerializeToNode(new[] { "logging/v1", "logging/v1" });
        AssertRejected(json, "host_capabilities");
    }

    [Fact]
    public void ValidUnknownCapabilityAndDifferentVersions_ArePreservedForHostNegotiation()
    {
        var json = Legacy();
        var capabilities = new[] { "custom.feature/v2", "logging/v1", "logging/v2", "future/v99999999999999999999" };
        json["host_capabilities"] = JsonSerializer.SerializeToNode(capabilities);
        Assert.Equal(capabilities, Property<IReadOnlyList<string>>(ParseValid(json.ToJsonString()), "HostCapabilities"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("demo.plugin ")]
    [InlineData("demo.plugin.")]
    [InlineData("demo*plugin")]
    [InlineData("demo|plugin")]
    [InlineData("../other")]
    [InlineData("demo..plugin")]
    [InlineData("demo/plugin")]
    [InlineData("demo\\plugin")]
    [InlineData("C:demo")]
    [InlineData("C:\\demo")]
    [InlineData("demo\u0001plugin")]
    [InlineData("demo\nplugin")]
    public void UnsafePluginId_IsRejectedBeforeHostUsesItAsPath(string id)
    {
        var json = Legacy();
        json["id"] = id;
        AssertRejected(json, "id");
    }

    [Theory]
    [InlineData("capabilities", "null")]
    [InlineData("extensions", "null")]
    [InlineData("capabilities", "[]")]
    [InlineData("extensions", "[]")]
    public void LegacyRequiredCollections_KeepExistingConstraints(string field, string value)
    {
        var json = Legacy();
        json[field] = JsonNode.Parse(value);
        AssertRejected(json, field);
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("extensions")]
    public void SourceTemplate_StillRequiresCollectionDeclarations(string field)
    {
        var json = Source();
        json.Remove(field);
        AssertRejected(json, field);
    }

    [Fact]
    public void SourceExtensions_KeepFailurePolicyAndCapabilitySemantics()
    {
        var json = Source();
        json["capabilities"] = JsonNode.Parse("""["demo",""]""");
        json["extensions"] = JsonNode.Parse("""
            [{ "id": "demo.request", "kind": "request", "pipeline": "request", "failure_policy": "fail-closed",
               "capabilities": ["demo", " "] },
             { "id": "demo.response", "kind": "response", "pipeline": "response" }]
            """);
        var manifest = ParseValid(json.ToJsonString());

        Assert.Equal(new[] { "demo" }, manifest.Capabilities);
        Assert.Collection(manifest.Extensions,
            item => { Assert.Equal(ExtensionFailurePolicy.FailClosed, item.FailurePolicy); Assert.Equal(new[] { "demo" }, item.Capabilities); },
            item => { Assert.Equal(ExtensionKind.Response, item.Kind); Assert.Equal(ExtensionFailurePolicy.ContinueOnError, item.FailurePolicy); });
    }

    [Theory]
    [InlineData("kind", "telemetry")]
    [InlineData("pipeline", "")]
    [InlineData("failure_policy", "ignore")]
    public void InvalidSourceExtension_StillRejectsManifest(string field, string value)
    {
        var json = Source();
        var extension = new JsonObject { ["id"] = "demo.request", ["kind"] = "request", ["pipeline"] = "request" };
        extension[field] = value;
        json["extensions"] = new JsonArray(extension);
        AssertRejected(json, field);
    }

    [Fact]
    public void InvalidOptionalUi_IsIgnoredAndDoesNotRejectSourceRuntime()
    {
        var json = Source();
        json["ui"] = JsonNode.Parse("""
            { "contributions": [{ "id": "summary", "slot": "card-body", "interactive": true },
              { "id": "summary", "slot": "detail-body" }, { "id": "bad", "slot": "floating-window" },
              { "slot": "card-body" }] }
            """);
        var errors = new List<string>();
        var diagnostics = new List<string>();
        var manifest = ManifestParser.Parse(json.ToJsonString(), errors, diagnostics);

        Assert.NotNull(manifest);
        Assert.Empty(errors);
        var contribution = Assert.Single(manifest.Ui!.Contributions);
        Assert.Equal("summary", contribution.Id);
        Assert.True(contribution.Interactive);
        Assert.Equal(3, diagnostics.Count);
        Assert.NotNull(ManifestParser.Parse(json.ToJsonString(), new List<string>()));
    }

    [Fact]
    public void InvalidFields_AreCollectedWithoutClearingExistingErrors()
    {
        var json = Source();
        json["id"] = "../bad";
        json["version"] = "";
        json["runtime"]!["entry_type"] = "";
        json["runtime"]!["sources"] = new JsonArray();
        json["host_capabilities"] = JsonNode.Parse("""["logging/v0","logging/v1","logging/v1"]""");
        var errors = new List<string> { "已有错误" };

        Assert.Null(ManifestParser.Parse(json.ToJsonString(), errors));
        Assert.Equal("已有错误", errors[0]);
        foreach (var field in new[] { "id", "version", "entry_type", "sources", "host_capabilities" })
            Assert.Contains(errors, error => error.Contains(field, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{ broken")]
    [InlineData("""{"runtime":42}""")]
    public void InvalidJson_StillReportsErrorsInsteadOfThrowing(string json)
    {
        var errors = new List<string>();
        Assert.Null(ManifestParser.Parse(json, errors));
        Assert.NotEmpty(errors);
    }

    private static JsonObject Legacy() => JsonNode.Parse(LegacyJson)!.AsObject();
    private static JsonObject Source() => JsonNode.Parse(SourceJson)!.AsObject();

    private static PluginManifest ParseValid(string json)
    {
        var errors = new List<string>();
        var manifest = ManifestParser.Parse(json, errors);
        Assert.True(manifest is not null, "有效 Manifest 被拒绝：" + string.Join("；", errors));
        Assert.Empty(errors);
        return manifest;
    }

    private static void AssertRejected(JsonObject json, string field)
    {
        var errors = new List<string>();
        Assert.Null(ManifestParser.Parse(json.ToJsonString(), errors));
        Assert.Contains(errors, error => error.Contains(field, StringComparison.Ordinal));
    }

    private static T Property<T>(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(owner));
    }

    private static void AssertInitProperty(Type type, string name)
    {
        var property = type.GetProperty(name);
        Assert.NotNull(property);
        Assert.NotNull(property.SetMethod);
        Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit),
            property.SetMethod.ReturnParameter.GetRequiredCustomModifiers());
    }
}
