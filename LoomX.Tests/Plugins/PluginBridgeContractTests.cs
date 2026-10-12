using System.Reflection;
using System.Text.Json;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginBridgeContractTests
{
    private static Assembly Contracts => typeof(ILoomXPlugin).Assembly;

    [Theory]
    [InlineData("IPluginBridge")]
    [InlineData("IPluginStorageBridge")]
    [InlineData("IPluginSqliteDatabase")]
    [InlineData("IPluginSqliteTransaction")]
    [InlineData("IPluginLogBridge")]
    [InlineData("IPluginNotificationBridge")]
    [InlineData("IPluginProtectionBridge")]
    [InlineData("PluginSqlValue")]
    [InlineData("PluginSqlCommand")]
    [InlineData("PluginSqlRow")]
    [InlineData("PluginDiagnostic")]
    public void 公共类型存在并归属契约程序集(string name) =>
        Assert.NotNull(Contracts.GetType("LoomX.Plugins." + name));

    [Fact]
    public void 初始化上下文保留旧构造并提供Bridge()
    {
        Assert.NotNull(typeof(PluginInitializationContext).GetConstructor([typeof(string), typeof(string)]));
        var context = new PluginInitializationContext("example.safe", "测试目录");
        var property = typeof(PluginInitializationContext).GetProperty("Bridge");
        Assert.NotNull(property);
        Assert.NotNull(property.GetValue(context));
        Assert.Equal("example.safe", context.PluginId);
        Assert.Equal("测试目录", context.DataDirectory);
    }

    [Fact]
    public void 桥接公开方法不暴露不可传输类型()
    {
        var bridge = Contracts.GetType("LoomX.Plugins.IPluginBridge");
        Assert.NotNull(bridge);
        var types = new[] { "IPluginBridge", "IPluginStorageBridge", "IPluginSqliteDatabase",
            "IPluginSqliteTransaction", "IPluginLogBridge", "IPluginNotificationBridge", "IPluginProtectionBridge" };
        foreach (var name in types)
        {
            var type = Contracts.GetType("LoomX.Plugins." + name);
            Assert.NotNull(type);
            foreach (var method in type.GetMethods())
            {
                AssertSafeType(method.ReturnType);
                foreach (var parameter in method.GetParameters()) AssertSafeType(parameter.ParameterType);
            }
        }
    }

    [Fact]
    public void SQL值使用封闭五种类型并可JSON往返()
    {
        var kind = Contracts.GetType("LoomX.Plugins.PluginSqlValueKind");
        Assert.NotNull(kind);
        Assert.Equal(new[] { "Null", "Integer", "Real", "Text", "Blob" }, Enum.GetNames(kind));
        var valueType = Contracts.GetType("LoomX.Plugins.PluginSqlValue");
        Assert.NotNull(valueType);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var value = JsonSerializer.Deserialize("{\"kind\":3,\"text\":\"测试文本\"}", valueType, options);
        var json = JsonSerializer.Serialize(value, valueType, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("测试文本", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("kind").GetInt32());
    }

    [Fact]
    public void 结构化诊断可JSON往返且不依赖宿主()
    {
        var type = Contracts.GetType("LoomX.Plugins.PluginDiagnostic");
        Assert.NotNull(type);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var value = JsonSerializer.Deserialize("{\"plugin_id\":\"example.safe\",\"stage\":0,\"severity\":2,\"code\":\"测试错误\",\"message\":\"安全摘要\",\"relative_path\":\"src/Main.cs\",\"line\":3,\"column\":4}", type, options);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, type, options));
        Assert.Equal("example.safe", doc.RootElement.GetProperty("plugin_id").GetString());
        Assert.Equal("src/Main.cs", doc.RootElement.GetProperty("relative_path").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("line").GetInt32());
    }

    private static void AssertSafeType(Type type)
    {
        Assert.NotEqual(typeof(object), type);
        Assert.False(typeof(Stream).IsAssignableFrom(type));
        Assert.False(typeof(Delegate).IsAssignableFrom(type));
        Assert.False(typeof(IServiceProvider).IsAssignableFrom(type));
        Assert.DoesNotContain("DbConnection", type.FullName ?? string.Empty);
        Assert.DoesNotContain("DbTransaction", type.FullName ?? string.Empty);
        Assert.DoesNotContain("LoomX.Plugins.Host", type.FullName ?? string.Empty);
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments()) AssertSafeType(argument);
    }
}
