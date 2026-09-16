using System.Text.Json;
using LoomX.Assistant.Configuration;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class TomlModelsTests
{
    [Fact]
    public void TomlPath_拒绝空路径()
    {
        var error = Assert.Throws<ArgumentException>(() => new TomlPath([]));

        Assert.Contains("路径", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TomlPath_拒绝空片段()
    {
        var error = Assert.Throws<ArgumentException>(() => new TomlPath(["model_providers", ""]));

        Assert.Contains("路径", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TomlPath_复制输入以保持不可变()
    {
        var segments = new[] { "model_providers", "loomx" };
        var path = new TomlPath(segments);

        segments[1] = "changed";

        Assert.Equal(["model_providers", "loomx"], path.Segments);
    }

    [Theory]
    [InlineData("\"loomx\"", TomlValueKind.String, "loomx")]
    [InlineData("9223372036854775807", TomlValueKind.Integer, 9223372036854775807L)]
    [InlineData("1.25", TomlValueKind.Float, 1.25)]
    [InlineData("true", TomlValueKind.Boolean, true)]
    public void TomlValue_转换标量JSON值(string json, TomlValueKind expectedKind, object expectedValue)
    {
        using var document = JsonDocument.Parse(json);

        var value = TomlValue.FromJsonElement(document.RootElement);

        Assert.Equal(expectedKind, value.Kind);
        Assert.Equal(expectedValue, value.Value);
    }

    [Fact]
    public void TomlValue_转换数组JSON值()
    {
        using var document = JsonDocument.Parse("[\"loomx\", 42, false]");

        var value = TomlValue.FromJsonElement(document.RootElement);

        Assert.Equal(TomlValueKind.Array, value.Kind);
        var items = Assert.IsAssignableFrom<IReadOnlyList<TomlValue>>(value.Value);
        Assert.Collection(
            items,
            item => Assert.Equal("loomx", item.Value),
            item => Assert.Equal(42L, item.Value),
            item => Assert.Equal(false, item.Value));
    }

    [Fact]
    public void TomlValue_递归转换对象JSON值()
    {
        using var document = JsonDocument.Parse("""
            {
              "model": "loomx",
              "provider": {
                "enabled": true
              }
            }
            """);

        var value = TomlValue.FromJsonElement(document.RootElement);

        Assert.Equal(TomlValueKind.Object, value.Kind);
        var root = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(value.Value);
        Assert.Equal("loomx", root["model"].Value);
        var provider = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(root["provider"].Value);
        Assert.Equal(true, provider["enabled"].Value);
    }

    [Fact]
    public void TomlValue_拒绝超出Int64范围的JSON整数()
    {
        using var document = JsonDocument.Parse("9223372036854775808");

        var error = Assert.Throws<ArgumentException>(() => TomlValue.FromJsonElement(document.RootElement));

        Assert.Contains("Int64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TomlValue_拒绝JSON空值()
    {
        using var document = JsonDocument.Parse("null");

        var error = Assert.Throws<ArgumentException>(() => TomlValue.FromJsonElement(document.RootElement));

        Assert.Contains("null", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TomlValue_拒绝任意CLR对象扩展()
    {
        var error = Assert.Throws<ArgumentException>(() => TomlValue.FromObject(new Version(1, 2)));

        Assert.Contains("不支持", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TomlPatchOperation_明确校验设置与删除值()
    {
        var path = new TomlPath(["model"]);
        var value = TomlValue.FromObject("loomx");

        Assert.Equal(value, new TomlPatchOperation(TomlPatchKind.Set, path, value).Value);
        Assert.Null(new TomlPatchOperation(TomlPatchKind.Delete, path).Value);
        Assert.Throws<ArgumentException>(() => new TomlPatchOperation(TomlPatchKind.Set, path));
        Assert.Throws<ArgumentException>(() => new TomlPatchOperation(TomlPatchKind.Delete, path, value));
    }
}
