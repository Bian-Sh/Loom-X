using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginProtectionBackendTests
{
    private static IPluginProtectionBridge Create(ILoggerFactory? log = null)
    {
        var type = typeof(PluginBridgeScope).Assembly.GetType("LoomX.Plugins.Host.PluginProtectionBridge");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IPluginProtectionBridge>(Activator.CreateInstance(type, ["example.safe", (log ?? NullLoggerFactory.Instance).CreateLogger<PluginProtectionBridge>()]));
    }

    [Fact]
    public async Task 保护往返并兼容既有Windows密文()
    {
        var bridge = Create();
        if (!OperatingSystem.IsWindows())
        {
            var error = await Assert.ThrowsAsync<PluginBridgeException>(() => bridge.EncryptAsync("测试凭据").AsTask());
            Assert.Equal(PluginBridgeError.CapabilityNotSupported, error.Error);
            return;
        }
        var original = "测试凭据🔐\r\n";
        var ciphertext = await bridge.EncryptAsync(original);
        Assert.StartsWith("dpapi:v1:", ciphertext);
        Assert.DoesNotContain(original, ciphertext);
        Assert.Equal(original, await bridge.DecryptAsync(ciphertext));
        var legacy = "dpapi:v1:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(original), null, DataProtectionScope.CurrentUser));
        Assert.Equal(original, await bridge.DecryptAsync(legacy));
    }

    [Theory]
    [InlineData("敏感测试输入")]
    [InlineData("dpapi:敏感测试输入")]
    [InlineData("dpapi:v2:敏感测试输入")]
    [InlineData("dpapi:v1:敏感测试输入")]
    [InlineData("dpapi:v1:AA==")]
    public async Task 非密文损坏和未知版本不原样回传且异常不泄密(string input)
    {
        var bridge = Create();
        var error = await Assert.ThrowsAsync<PluginBridgeException>(() => bridge.DecryptAsync(input).AsTask());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(input, error.ToString());
        Assert.DoesNotContain("敏感测试输入", error.ToString());
    }

    [Fact]
    public async Task 成功失败日志与异常均不包含明文密文或底层原值()
    {
        var logger = new CaptureLogger();
        var bridge = new PluginProtectionBridge("example.safe", logger);
        const string canary = "日志敏感哨兵-9b9c-credential";
        if (!OperatingSystem.IsWindows()) return;
        var ciphertext = await bridge.EncryptAsync(canary);
        Assert.Equal(canary, await bridge.DecryptAsync(ciphertext));
        await Assert.ThrowsAsync<PluginBridgeException>(() => bridge.DecryptAsync("dpapi:v1:" + canary).AsTask());
        var log = string.Join("\n", logger.Events);
        Assert.DoesNotContain(canary, log);
        Assert.DoesNotContain(ciphertext, log);
        Assert.Contains("example.safe", log);
        Assert.Contains("完成", log);
        Assert.Contains("失败", log);
        await bridge.DisposeAsync();
        Assert.Equal(PluginBridgeError.Disposed, (await Assert.ThrowsAsync<PluginBridgeException>(() => bridge.EncryptAsync(canary).AsTask())).Error);
    }

    [Fact]
    public async Task 历史引用高频恢复只写采样诊断而不逐token记录()
    {
        if (!OperatingSystem.IsWindows()) return;
        var logger = new CaptureLogger();
        await using var bridge = new PluginProtectionBridge("example.safe", logger);
        var ciphertext = await bridge.EncryptAsync("合成非敏感数据");
        for (var index = 0; index < 128; index++)
            Assert.Equal("合成非敏感数据", await bridge.DecryptAsync(ciphertext));
        Assert.True(logger.Events.Count <= 5, "高频恢复不能逐token写日志，实际=" + logger.Events.Count);
    }
    private sealed class CaptureLogger : ILogger<PluginProtectionBridge>
    {
        public List<string> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Add(formatter(state, exception) + exception?.ToString());
    }
    [Fact]
    public async Task Scope检查声明取消和退休且旧默认不可用()
    {
        var bridge = Create();
        await using var scope = new PluginBridgeScope("example.safe", ["protection/v1"], protection: bridge);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Protection.EncryptAsync("secret", cancelled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Protection.DecryptAsync("secret", cancelled.Token).AsTask());
        await using var denied = new PluginBridgeScope("example.other", [], protection: bridge);
        Assert.Equal(PluginBridgeError.CapabilityNotDeclared, (await Assert.ThrowsAsync<PluginBridgeException>(() => denied.Protection.EncryptAsync("secret").AsTask())).Error);
        await scope.DisposeAsync();
        Assert.Equal(PluginBridgeError.Disposed, (await Assert.ThrowsAsync<PluginBridgeException>(() => scope.Protection.DecryptAsync("secret").AsTask())).Error);
        Assert.Equal(PluginBridgeError.CapabilityNotSupported, (await Assert.ThrowsAsync<PluginBridgeException>(() => PluginBridgeDefaults.Unavailable.Protection.EncryptAsync("secret").AsTask())).Error);
    }
}