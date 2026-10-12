using System.Collections;
using LoomX.Plugins;
using LoomX.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginFeedbackBridgeTests
{
    [Fact]
    public async Task 结构化日志使用宿主身份且保留安全字段()
    {
        var logger = new CaptureLogger();
        var bridge = CreateLog(logger);
        await bridge.WriteAsync(new(PluginLogLevel.Information, "插件操作完成 {StatusCode}",
            [new("StatusCode", new(PluginLogValueKind.Integer, Integer: 200))]));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("[example.safe] 插件操作完成 200", entry.Message);
        Assert.Contains(entry.Fields, field => field.Key == "PluginId" && Equals(field.Value, "example.safe"));
        Assert.Contains(entry.Fields, field => field.Key == "StatusCode" && Equals(field.Value, 200L));
    }

    [Fact]
    public async Task 日志敏感字段和凭据消息不进入日志()
    {
        var logger = new CaptureLogger();
        var bridge = CreateLog(logger);
        const string sentinel = "sk-secret-sentinel-0123456789012345";
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(
            PluginLogLevel.Error, "操作失败", [new("Authorization", new(PluginLogValueKind.Text, Text: sentinel))])));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(PluginLogLevel.Error, sentinel)));
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains(sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries.SelectMany(entry => entry.Fields), field => Equals(field.Value, sentinel));
    }

    [Fact]
    public async Task 插件不得通过字段覆盖绑定身份()
    {
        var bridge = CreateLog(new CaptureLogger());
        var error = await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(
            PluginLogLevel.Information, "事件", [new("PluginId", new(PluginLogValueKind.Text, Text: "other.plugin"))])));
        Assert.Equal(PluginBridgeError.InvalidArgument, error.Error);
    }

    [Fact]
    public async Task 通知映射到现有Toast且不允许敏感内容()
    {
        var service = new ToastService();
        var messages = new List<ToastNotification>();
        service.Requested += (_, value) => messages.Add(value);
        var bridge = CreateNotification(service, new CaptureLogger());
        await bridge.ShowAsync(new(PluginNotificationLevel.Success, "保存完成"));
        Assert.Equal(new ToastNotification("[example.safe] 保存完成", ToastLevel.Success), Assert.Single(messages));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.ShowAsync(new(
            PluginNotificationLevel.Info, "Authorization: Bearer sk-secret-sentinel-0123456789012345")));
        Assert.Single(messages);
    }

    [Fact]
    public async Task 通知受到长度与频率上限约束()
    {
        var bridge = CreateNotification(new ToastService(), new CaptureLogger());
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.ShowAsync(new(PluginNotificationLevel.Info, new string('a', 513))));
        for (var index = 0; index < 10; index++) await bridge.ShowAsync(new(PluginNotificationLevel.Info, "安全摘要"));
        var error = await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.ShowAsync(new(PluginNotificationLevel.Info, "第十一条")));
        Assert.Equal(PluginBridgeError.ResourceLimit, error.Error);
    }

    [Fact]
    public async Task 日志未知值类型与非法级别被拒绝()
    {
        var bridge = CreateLog(new CaptureLogger());
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new((PluginLogLevel)99, "事件")));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(PluginLogLevel.Information, "事件",
            [new("Field", new((PluginLogValueKind)99))])));
    }

    [Fact]
    public async Task 日志超长字段非有限数及高频调用均受限()
    {
        var bridge = CreateLog(new CaptureLogger());
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(PluginLogLevel.Information, "事件",
            [new("Outcome", new(PluginLogValueKind.Text, Text: new string('x', 257)))])));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(PluginLogLevel.Information, "事件",
            [new("ElapsedMs", new(PluginLogValueKind.Real, Real: double.NaN))])));
        for (var index = 0; index < 100; index++) await bridge.WriteAsync(new(PluginLogLevel.Debug, "安全摘要"));
        var error = await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(PluginLogLevel.Debug, "超限")));
        Assert.Equal(PluginBridgeError.ResourceLimit, error.Error);
    }
    [Theory]
    [InlineData(PluginLogValueKind.Null)]
    [InlineData(PluginLogValueKind.Boolean)]
    [InlineData(PluginLogValueKind.Integer)]
    [InlineData(PluginLogValueKind.Real)]
    public async Task 非Text联合值忽略未选中的Text成员且不输出原值(PluginLogValueKind kind)
    {
        var logger = new CaptureLogger();
        const string sentinel = "Bearer abc123";
        await CreateLog(logger).WriteAsync(new(PluginLogLevel.Information, "事件 {Outcome}",
            [new("Outcome", new(kind, Boolean: true, Integer: 200, Real: 1.5, Text: sentinel))]));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.DoesNotContain(sentinel, entry.Message);
        Assert.DoesNotContain(entry.Fields, field => Equals(field.Value, sentinel));
    }

    private static IPluginLogBridge CreateLog(CaptureLogger logger)
    {
        var type = typeof(ToastService).Assembly.GetType("LoomX.Services.PluginLogBridge");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IPluginLogBridge>(Activator.CreateInstance(type, ["example.safe", logger]));
    }
    private static IPluginNotificationBridge CreateNotification(ToastService service, CaptureLogger logger)
    {
        var type = typeof(ToastService).Assembly.GetType("LoomX.Services.PluginNotificationBridge");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IPluginNotificationBridge>(Activator.CreateInstance(type, ["example.safe", service, logger]));
    }

    private sealed record Captured(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Fields);
    private sealed class CaptureLogger : ILogger<PluginLogBridge>, ILogger<PluginNotificationBridge>
    {
        public List<Captured> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToArray() : [];
            Entries.Add(new(logLevel, formatter(state, exception), fields));
        }
    }
}
