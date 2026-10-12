using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LoomX.Plugins.Host;

/// <summary>宿主默认字符串保护；Windows 历史 Vault 使用 CurrentUser/null entropy，不是跨插件恶意代码沙箱。</summary>
public sealed class PluginProtectionBridge : IPluginProtectionBridge, IAsyncDisposable
{
    private const string Prefix = "dpapi:v1:";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string pluginId;
    private readonly ILogger<PluginProtectionBridge> logger;
    private int disposed;
    private long completed;

    public PluginProtectionBridge(string pluginId, ILogger<PluginProtectionBridge> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(logger);
        this.pluginId = pluginId;
        this.logger = logger;
    }

    public ValueTask<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default) =>
        Protect(plaintext, decrypt: false, cancellationToken);

    public ValueTask<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default) =>
        Protect(ciphertext, decrypt: true, cancellationToken);

    private ValueTask<string> Protect(string input, bool decrypt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref disposed) != 0)
            throw new PluginBridgeException(PluginBridgeError.Disposed, "插件字符串保护能力已释放。");
        if (input is null)
            throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "字符串保护参数无效。");
        var operation = decrypt ? "解密" : "加密";
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("插件字符串保护后端不可用 {PluginId} {Operation}", pluginId, operation);
            throw new PluginBridgeException(PluginBridgeError.CapabilityNotSupported, "当前平台没有可用的字符串保护后端。");
        }
        byte[]? bytes = null;
        byte[]? result = null;
        try
        {
            if (decrypt)
            {
                if (!input.StartsWith(Prefix, StringComparison.Ordinal))
                    throw new CryptographicException("密文版本无效。");
                bytes = Convert.FromBase64String(input[Prefix.Length..]);
                result = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                var plaintext = Utf8.GetString(result);
                token.ThrowIfCancellationRequested();
                LogCompletion(operation);
                return ValueTask.FromResult(plaintext);
            }
            bytes = Utf8.GetBytes(input);
            result = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var ciphertext = Prefix + Convert.ToBase64String(result);
            token.ThrowIfCancellationRequested();
            LogCompletion(operation);
            return ValueTask.FromResult(ciphertext);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            // 底层异常可能带输入；日志和公开异常均只携带安全摘要，不保留 InnerException。
            logger.LogError(new CryptographicException("字符串保护失败，原值已隐藏。"),
                "插件字符串保护失败 {PluginId} {Operation} {ErrorType}", pluginId, operation, exception.GetType().Name);
            throw new PluginBridgeException(PluginBridgeError.OperationFailed, "字符串保护失败，原值已隐藏。");
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            if (result is not null) CryptographicOperations.ZeroMemory(result);
        }
    }

    private void LogCompletion(string operation)
    {
        // 历史恢复可能逐引用调用；成功只采样 Debug，不逐 token 写运行日志。
        var count = Interlocked.Increment(ref completed);
        if (count == 1 || count % 64 == 0)
            logger.LogDebug("插件字符串保护采样完成 {PluginId} {Operation} {CompletedCount}", pluginId, operation, count);
    }
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        return ValueTask.CompletedTask;
    }
}