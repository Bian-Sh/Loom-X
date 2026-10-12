using LoomX.Logging;
using LoomX.Plugins;
using LoomX.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginFeedbackOutputTests
{
    [Fact]
    public async Task 日志不能覆盖Serilog来源且实际控制台和文件保留绑定身份()
    {
        using var capture = new Capture();
        var first = capture.Log("example.first");
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await first.WriteAsync(new(PluginLogLevel.Information, "完成",
            [new("SourceContext", new(PluginLogValueKind.Text, Text: "LoomX.Services.ProviderExecutionPipeline"))])));
        await first.WriteAsync(new(PluginLogLevel.Information, "完成"));
        await capture.Log("example.second").WriteAsync(new(PluginLogLevel.Information, "完成"));
        var visible = capture.Buffer.Snapshot();
        Assert.Contains(visible, entry => entry.Message.Contains("example.first", StringComparison.Ordinal));
        Assert.Contains(visible, entry => entry.Message.Contains("example.second", StringComparison.Ordinal));
        Assert.DoesNotContain(visible, entry => entry.Category == "LoomX.Services.ProviderExecutionPipeline");
        var formatter = Assert.IsAssignableFrom<ITextFormatter>(Activator.CreateInstance(
            typeof(ToastService).Assembly.GetType("LoomX.Logging.RuntimeLogTextFormatter")!, nonPublic: true));
        using var output = new StringWriter();
        foreach (var entry in capture.Events) formatter.Format(entry, output);
        Assert.Contains("example.first", output.ToString());
        Assert.Contains("example.second", output.ToString());
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("ghp_0123456789abcdefghijklmnopqrstuvwxyz")]
    [InlineData("github_pat_0123456789abcdefghijklmnopqrstuvwxyz")]
    public async Task 已知凭据形态不会进入真实日志与Toast(string sentinel)
    {
        using var capture = new Capture();
        var service = new ToastService();
        var messages = new List<ToastNotification>();
        service.Requested += (_, message) => messages.Add(message);
        var notification = new PluginNotificationBridge("example.first", service, capture.Factory.CreateLogger<PluginNotificationBridge>());
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await capture.Log("example.first").WriteAsync(new(PluginLogLevel.Information,
            "事件 {Outcome}", [new("Outcome", new(PluginLogValueKind.Text, Text: sentinel))])));
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await notification.ShowAsync(new(PluginNotificationLevel.Info, sentinel)));
        Assert.Empty(messages);
        Assert.DoesNotContain(capture.Events, entry => entry.RenderMessage().Contains(sentinel, StringComparison.Ordinal) || entry.Properties.Values.Any(value => value.ToString().Contains(sentinel, StringComparison.Ordinal)));
    }

    public static TheoryData<string> ShortCredentialCases => new()
    {
        "Basic dTpw", "Bearer abc123",
        "bAsIc dTpw", "bEaReR abc123",
        "BASIC  dTpw", "BEARER  abc123",
        "Basic\tdTpw", "Bearer\tabc123",
        "Basic\u00a0dTpw", "Bearer\u00a0abc123",
        "Basic Y", "Bearer x",
    };

    [Theory]
    [MemberData(nameof(ShortCredentialCases))]
    public async Task 短凭据模板不会进入真实日志输出(string sentinel)
    {
        using var capture = new Capture();
        var error = await Record.ExceptionAsync(async () => await capture.Log("example.first")
            .WriteAsync(new(PluginLogLevel.Information, "事件 " + sentinel)));
        AssertSafeRejection(capture, error, sentinel);
    }

    [Theory]
    [MemberData(nameof(ShortCredentialCases))]
    public async Task 短凭据字段不会进入真实日志输出(string sentinel)
    {
        using var capture = new Capture();
        var error = await Record.ExceptionAsync(async () => await capture.Log("example.first")
            .WriteAsync(new(PluginLogLevel.Information, "事件 {Outcome}",
                [new("Outcome", new(PluginLogValueKind.Text, Text: sentinel))])));
        AssertSafeRejection(capture, error, sentinel);
    }

    [Theory]
    [InlineData("Basic {Outcome}", "dTpw", "Basic dTpw")]
    [InlineData("Bearer {Outcome}", "abc123", "Bearer abc123")]
    [InlineData("bAsIc\t{Outcome}", "dTpw", "bAsIc\tdTpw")]
    [InlineData("bEaReR  {Outcome}", "abc123", "bEaReR  abc123")]
    public async Task 模板展开拼出的短凭据不会进入真实日志输出(string template, string value, string sentinel)
    {
        using var capture = new Capture();
        var error = await Record.ExceptionAsync(async () => await capture.Log("example.first")
            .WriteAsync(new(PluginLogLevel.Information, template,
                [new("Outcome", new(PluginLogValueKind.Text, Text: value))])));
        AssertSafeRejection(capture, error, sentinel);
    }

    [Theory]
    [MemberData(nameof(ShortCredentialCases))]
    public async Task 短凭据不会进入真实Toast订阅(string sentinel)
    {
        using var capture = new Capture();
        var service = new ToastService();
        var messages = new List<ToastNotification>();
        service.Requested += (_, message) => messages.Add(message);
        var bridge = new PluginNotificationBridge("example.first", service,
            capture.Factory.CreateLogger<PluginNotificationBridge>());
        var error = await Record.ExceptionAsync(async () => await bridge
            .ShowAsync(new(PluginNotificationLevel.Info, sentinel)));
        // 先核验真实投递结果，旧逻辑必须因泄漏失败，而不是因为夹具或缺少异常失败。
        Assert.DoesNotContain(messages, message => message.Message.Contains(sentinel, StringComparison.Ordinal));
        Assert.Empty(messages);
        AssertSafeRejection(capture, error, sentinel);
    }

    private static void AssertSafeRejection(Capture capture, Exception? error, string sentinel)
    {
        // 真实控制台、Serilog 原始模板/字段和正式文件格式化器都不得输出原值。
        Assert.DoesNotContain(capture.Buffer.Snapshot(), entry =>
            entry.Level != LogLevel.Warning || entry.Message.Contains(sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Events, entry => entry.Level != LogEventLevel.Warning ||
            entry.MessageTemplate.Text.Contains(sentinel, StringComparison.Ordinal) ||
            entry.Properties.Values.OfType<ScalarValue>().Any(value =>
                value.Value is string text && text.Contains(sentinel, StringComparison.Ordinal)));
        var formatter = Assert.IsAssignableFrom<ITextFormatter>(Activator.CreateInstance(
            typeof(ToastService).Assembly.GetType("LoomX.Logging.RuntimeLogTextFormatter")!, nonPublic: true));
        using var output = new StringWriter();
        foreach (var entry in capture.Events) formatter.Format(entry, output);
        Assert.DoesNotContain(sentinel, output.ToString());
        var rejection = Assert.IsType<PluginBridgeException>(error);
        Assert.Equal(PluginBridgeError.InvalidArgument, rejection.Error);
        Assert.DoesNotContain(sentinel, rejection.Message);
        Assert.NotEmpty(capture.Buffer.Snapshot());
        Assert.All(capture.Buffer.Snapshot(), entry =>
        {
            Assert.Contains("example.first", entry.Message);
            Assert.Contains(nameof(PluginBridgeError.InvalidArgument), entry.Message);
        });
    }

    [Theory]
    [InlineData("{Count:Q}")]
    [InlineData("{Count:D10000}")]
    [InlineData("{Count,10000}")]
    public async Task 模板格式及展开预算在进入Serilog之前被拒绝(string template)
    {
        using var capture = new Capture();
        var error = await Assert.ThrowsAsync<PluginBridgeException>(async () => await capture.Log("example.first").WriteAsync(new(PluginLogLevel.Information,
            template, [new("Count", new(PluginLogValueKind.Integer, Integer: 1))])));
        Assert.Equal(PluginBridgeError.InvalidArgument, error.Error);
        Assert.DoesNotContain(capture.Buffer.Snapshot(), entry => entry.Message.Length > 512);
    }

    [Fact]
    public async Task 重复占位展开超预算被安全拒绝()
    {
        using var capture = new Capture();
        await Assert.ThrowsAsync<PluginBridgeException>(async () => await capture.Log("example.first").WriteAsync(new(PluginLogLevel.Information,
            "{Outcome}{Outcome}{Outcome}", [new("Outcome", new(PluginLogValueKind.Text, Text: new string('x', 256)))])));
    }

    [Fact]
    public async Task 敏感日志拒绝生成限频安全诊断且不含原值()
    {
        using var capture = new Capture();
        var bridge = capture.Log("example.first");
        for (var index = 0; index < 20; index++)
            await Assert.ThrowsAsync<PluginBridgeException>(async () => await bridge.WriteAsync(new(PluginLogLevel.Error, "事件",
                [new("Authorization", new(PluginLogValueKind.Text, Text: "sk-sensitive-denied-sentinel"))])));
        var diagnostics = capture.Buffer.Snapshot();
        Assert.InRange(diagnostics.Count, 1, 10);
        Assert.All(diagnostics, entry =>
        {
            Assert.Contains("example.first", entry.Message);
            Assert.DoesNotContain("sk-sensitive-denied-sentinel", entry.Message);
        });
    }

    [Fact]
    public async Task Toast来源由宿主绑定且故障订阅者不泄密也不阻断健康链()
    {
        var service = new ToastService();
        var messages = new List<ToastNotification>();
        service.Requested += (_, _) => throw new InvalidOperationException("sk-toast-failure-sensitive-sentinel");
        service.Requested += (_, message) => messages.Add(message);
        var first = new PluginNotificationBridge("example.first", service, NullLogger<PluginNotificationBridge>.Instance);
        var second = new PluginNotificationBridge("example.second", service, NullLogger<PluginNotificationBridge>.Instance);
        var error = await Assert.ThrowsAsync<PluginBridgeException>(async () => await first.ShowAsync(new(PluginNotificationLevel.Success, "保存完成")));
        Assert.Equal(PluginBridgeError.OperationFailed, error.Error);
        Assert.DoesNotContain("sk-toast-failure-sensitive-sentinel", error.Message);
        await second.ShowAsync(new(PluginNotificationLevel.Success, "保存完成"));
        Assert.Equal(2, messages.Count);
        Assert.Contains("example.first", messages[0].Message);
        Assert.Contains("example.second", messages[1].Message);
    }

    private sealed class Capture : ILogEventSink, IDisposable
    {
        private readonly Serilog.Core.Logger logger;
        public RuntimeLogBuffer Buffer { get; } = new();
        public List<LogEvent> Events { get; } = [];
        public ILoggerFactory Factory { get; }
        public Capture()
        {
            logger = new LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext()
                .WriteTo.Sink(new RuntimeLogSink(Buffer)).WriteTo.Sink(this).CreateLogger();
            Factory = LoggerFactory.Create(builder => builder.AddSerilog(logger, dispose: false));
        }
        public PluginLogBridge Log(string id) => new(id, Factory.CreateLogger<PluginLogBridge>());
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; Factory.Dispose(); logger.Dispose(); }
    }
}