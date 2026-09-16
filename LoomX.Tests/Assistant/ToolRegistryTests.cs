using System.Text.Json.Nodes;
using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class ToolRegistryTests
{
    [Fact]
    public void Register_ThenTryGet_ReturnsTool()
    {
        var registry = new ToolRegistry();
        var tool = MockTools.CreateListProvidersTool();

        registry.Register(tool);

        Assert.True(registry.TryGet("mock.list_providers", out var found));
        Assert.Same(tool, found);
        Assert.Single(registry.All);
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());

        Assert.True(registry.TryGet("MOCK.LIST_PROVIDERS", out _));
    }

    [Fact]
    public void Register_DuplicateName_Throws()
    {
        var registry = new ToolRegistry();
        registry.Register(MockTools.CreateListProvidersTool());

        Assert.Throws<InvalidOperationException>(() => registry.Register(MockTools.CreateListProvidersTool()));
    }

    [Fact]
    public void Register_EmptyName_Throws()
    {
        var registry = new ToolRegistry();
        var tool = MockTools.CreateListProvidersTool();
        tool = new ToolDefinition
        {
            Name = " ",
            Description = tool.Description,
            ParametersSchema = tool.ParametersSchema,
            Handler = tool.Handler,
        };

        Assert.Throws<ArgumentException>(() => registry.Register(tool));
    }

    [Fact]
    public void TryGet_UnknownTool_ReturnsFalse()
    {
        var registry = new ToolRegistry();

        Assert.False(registry.TryGet("loomx.list_providers", out var tool));
        Assert.Null(tool);
    }

    [Fact]
    public void TomlTools_RegisterAll_注册六个工具及风险等级()
    {
        var registry = TomlToolsTestSupport.CreateRegistry(new RecordingTomlDocumentService());
        var expected = new Dictionary<string, ToolRiskLevel>
        {
            ["toml.read"] = ToolRiskLevel.Read,
            ["toml.get"] = ToolRiskLevel.Read,
            ["toml.validate"] = ToolRiskLevel.Read,
            ["toml.set"] = ToolRiskLevel.Write,
            ["toml.patch"] = ToolRiskLevel.Write,
            ["toml.delete"] = ToolRiskLevel.Destructive,
        };

        foreach (var item in expected)
        {
            Assert.True(registry.TryGet(item.Key, out var tool));
            Assert.Equal(item.Value, tool!.RiskLevel);
        }
    }

    [Fact]
    public void TomlTools_Schema限制路径键路径和Patch操作()
    {
        var registry = TomlToolsTestSupport.CreateRegistry(new RecordingTomlDocumentService());

        foreach (var name in new[] { "toml.read", "toml.get", "toml.validate", "toml.set", "toml.patch", "toml.delete" })
        {
            var schema = GetSchema(registry, name);
            Assert.Contains("path", RequiredNames(schema));
            var pathSchema = schema["properties"]!["path"]!.AsObject();
            Assert.Equal("string", pathSchema["type"]!.GetValue<string>());
            Assert.Equal(1, pathSchema["minLength"]!.GetValue<int>());
            Assert.True(pathSchema["maxLength"]!.GetValue<int>() > 0);
        }

        foreach (var name in new[] { "toml.get", "toml.set", "toml.delete" })
        {
            var schema = GetSchema(registry, name);
            Assert.Contains("key_path", RequiredNames(schema));
            AssertBoundedStringArray(schema["properties"]!["key_path"]!);
        }

        var patchSchema = GetSchema(registry, "toml.patch");
        Assert.Contains("operations", RequiredNames(patchSchema));
        var operations = patchSchema["properties"]!["operations"]!.AsObject();
        Assert.Equal(1, operations["minItems"]!.GetValue<int>());
        Assert.True(operations["maxItems"]!.GetValue<int>() > 0);
        var operation = operations["items"]!.AsObject();
        var allowedOperations = operation["properties"]!["op"]!["enum"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        Assert.Equal(["set", "delete"], allowedOperations);
        AssertBoundedStringArray(operation["properties"]!["key_path"]!);
    }

    [Theory]
    [InlineData("toml.set")]
    [InlineData("toml.patch")]
    public void TomlTools_ValueSchema递归限制嵌套字符串和对象属性名(string toolName)
    {
        var registry = TomlToolsTestSupport.CreateRegistry(new RecordingTomlDocumentService());
        var schema = GetSchema(registry, toolName);
        var valueSchema = toolName == "toml.set"
            ? schema["properties"]!["value"]!.AsObject()
            : schema["properties"]!["operations"]!["items"]!["properties"]!["value"]!.AsObject();

        var valueReference = valueSchema["$ref"];
        Assert.NotNull(valueReference);
        Assert.Equal("#/$defs/tomlValue", valueReference.GetValue<string>());
        var definitions = Assert.IsType<JsonObject>(schema["$defs"]);
        var definition = Assert.IsType<JsonObject>(definitions["tomlValue"]);
        var variants = definition["anyOf"]!.AsArray()
            .Select(item => item!.AsObject())
            .ToArray();
        var stringSchema = Assert.Single(variants, item => item["type"]!.GetValue<string>() == "string");
        var arraySchema = Assert.Single(variants, item => item["type"]!.GetValue<string>() == "array");
        var objectSchema = Assert.Single(variants, item => item["type"]!.GetValue<string>() == "object");

        Assert.True(stringSchema["maxLength"]!.GetValue<int>() > 0);
        Assert.Equal("#/$defs/tomlValue", arraySchema["items"]!["$ref"]!.GetValue<string>());
        Assert.Equal("string", objectSchema["propertyNames"]!["type"]!.GetValue<string>());
        Assert.True(objectSchema["propertyNames"]!["maxLength"]!.GetValue<int>() > 0);
        Assert.Equal("#/$defs/tomlValue", objectSchema["additionalProperties"]!["$ref"]!.GetValue<string>());

        Assert.NotNull(JsonNode.Parse(schema.ToJsonString()));
    }

    private static JsonObject GetSchema(ToolRegistry registry, string name)
    {
        Assert.True(registry.TryGet(name, out var tool));
        return tool!.ParametersSchema.AsObject();
    }

    private static string[] RequiredNames(JsonObject schema) =>
        schema["required"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();

    private static void AssertBoundedStringArray(JsonNode node)
    {
        var schema = node.AsObject();
        Assert.Equal("array", schema["type"]!.GetValue<string>());
        Assert.Equal(1, schema["minItems"]!.GetValue<int>());
        Assert.True(schema["maxItems"]!.GetValue<int>() > 0);
        Assert.Equal("string", schema["items"]!["type"]!.GetValue<string>());
        Assert.True(schema["items"]!["maxLength"]!.GetValue<int>() > 0);
    }
}
