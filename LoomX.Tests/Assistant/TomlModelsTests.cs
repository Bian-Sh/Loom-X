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

    [Fact]
    public void TomlPath_相同片段内容具有值相等和相同哈希()
    {
        var first = new TomlPath(new[] { "model_providers", "loomx" });
        var second = new TomlPath(new[] { "model_providers", "loomx" });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void TomlPath_片段顺序或内容不同则不相等()
    {
        var path = new TomlPath(["model_providers", "loomx"]);

        Assert.NotEqual(path, new TomlPath(["loomx", "model_providers"]));
        Assert.NotEqual(path, new TomlPath(["model_providers", "other"]));
    }

    [Fact]
    public void TomlPath_含点单片段与多个片段不相等()
    {
        Assert.NotEqual(new TomlPath(["a.b"]), new TomlPath(["a", "b"]));
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
    public void TomlValue_拒绝直接数组中的空元素()
    {
        var items = new TomlValue[] { TomlValue.FromObject("loomx"), null! };

        Assert.Throws<ArgumentException>(() => TomlValue.FromArray(items));
    }

    [Fact]
    public void TomlValue_拒绝对象内嵌数组中的空元素()
    {
        var source = new Dictionary<string, object?>
        {
            ["models"] = new TomlValue[] { TomlValue.FromObject("loomx"), null! },
        };

        Assert.Throws<ArgumentException>(() => TomlValue.FromObject(source));
    }

    [Fact]
    public void TomlValue_拒绝嵌套数组中的空元素()
    {
        object?[] source = [new TomlValue[] { null! }];

        Assert.Throws<ArgumentException>(() => TomlValue.FromObject(source));
    }

    [Fact]
    public void TomlValue_拒绝任意CLR对象扩展()
    {
        var error = Assert.Throws<ArgumentException>(() => TomlValue.FromObject(new Version(1, 2)));

        Assert.Contains("不支持", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TomlValidationResult_复制错误列表以保持不可变()
    {
        var errors = new List<string> { "第一个错误" };
        var result = new TomlValidationResult(false, 1, 2, errors);

        errors[0] = "已修改";
        errors.Add("新增错误");

        Assert.Equal(["第一个错误"], result.Errors);
    }

    [Fact]
    public void TomlValueResult_复制错误列表以保持不可变()
    {
        var errors = new List<string> { "读取失败" };
        var result = new TomlValueResult(false, null, null, errors);

        errors.Clear();

        Assert.Equal(["读取失败"], result.Errors);
    }

    [Fact]
    public void TomlWriteResult_复制错误列表以保持不可变()
    {
        var errors = new List<string> { "写入失败" };
        var result = new TomlWriteResult(false, false, null, false, errors);

        errors.Add("新增错误");

        Assert.Equal(["写入失败"], result.Errors);
    }

    [Fact]
    public void TOML结果契约_拒绝空错误集合()
    {
        Assert.Throws<ArgumentNullException>(() => new TomlValidationResult(false, null, null, null!));
        Assert.Throws<ArgumentNullException>(() => new TomlValueResult(false, null, null, null!));
        Assert.Throws<ArgumentNullException>(() => new TomlWriteResult(false, false, null, false, null!));
    }

    [Fact]
    public void TOML结果契约_拒绝错误集合中的空项()
    {
        string[] errors = ["有效错误", null!];

        Assert.Throws<ArgumentException>(() => new TomlValidationResult(false, null, null, errors));
        Assert.Throws<ArgumentException>(() => new TomlValueResult(false, null, null, errors));
        Assert.Throws<ArgumentException>(() => new TomlWriteResult(false, false, null, false, errors));
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

    [Fact]
    public void TomlPatchOperation_拒绝未定义的操作类型()
    {
        var path = new TomlPath(["model"]);

        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlPatchOperation((TomlPatchKind)999, path));
    }

    [Fact]
    public void TomlPatchOperation_拒绝默认空路径()
    {
        Assert.Throws<ArgumentException>(() => new TomlPatchOperation(TomlPatchKind.Delete, default));
    }
}
