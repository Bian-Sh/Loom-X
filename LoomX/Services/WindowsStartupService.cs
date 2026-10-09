using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace LoomX.Services;

public interface IWindowsStartupService
{
    void Apply(bool enabled);
}

public sealed class WindowsStartupService : IWindowsStartupService
{
    internal const string ValueName = "LoomX";
    private readonly IStartupRegistryStore registryStore;
    private readonly Func<string?> processPathProvider;
    private readonly ILogger<WindowsStartupService> logger;

    public WindowsStartupService(ILogger<WindowsStartupService>? logger = null)
        : this(new CurrentUserStartupRegistryStore(), () => Environment.ProcessPath, logger)
    {
    }

    internal WindowsStartupService(
        IStartupRegistryStore registryStore,
        Func<string?> processPathProvider,
        ILogger<WindowsStartupService>? logger = null)
    {
        this.registryStore = registryStore;
        this.processPathProvider = processPathProvider;
        this.logger = logger ?? NullLogger<WindowsStartupService>.Instance;
    }

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            registryStore.DeleteValue(ValueName);
            logger.LogInformation("Windows 开机自启动已更新 {Enabled}", false);
            return;
        }

        var processPath = processPathProvider();
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
            throw new InvalidOperationException("无法确定 Loom-X 可执行文件路径。");

        registryStore.SetValue(ValueName, BuildCommand(processPath));
        logger.LogInformation("Windows 开机自启动已更新 {Enabled}", true);
    }

    internal static string BuildCommand(string processPath) => $"\"{processPath}\"";
}

internal interface IStartupRegistryStore
{
    void SetValue(string name, string value);
    void DeleteValue(string name);
}

internal sealed class CurrentUserStartupRegistryStore : IStartupRegistryStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void SetValue(string name, string value)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("开机自启动仅支持 Windows。");
        SetWindowsValue(name, value);
    }

    public void DeleteValue(string name)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("开机自启动仅支持 Windows。");
        DeleteWindowsValue(name);
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 自启动注册表项。");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteWindowsValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
