using LoomX.Plugin.Storage.Sqlite;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>唯一生产组合点：身份、数据根和实际能力实现同时绑定到插件实例。</summary>
public sealed class PluginHostBridgeFactory(string dataRoot, ToastService toastService, ILoggerFactory loggerFactory)
{
    public PluginBridgeScope CreateScope(PluginManifest manifest, string dataDirectory)
    {
        var expected = Path.GetFullPath(Path.Combine(dataRoot, manifest.Id));
        if (!string.Equals(expected, Path.GetFullPath(dataDirectory), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new PluginBridgeException(PluginBridgeError.InvalidArgument, "插件数据目录与绑定身份不匹配。");
        var capabilities = manifest.HostCapabilities;
        return new(manifest.Id, capabilities,
            capabilities.Contains("storage.sqlite/v1", StringComparer.Ordinal)
                ? new PluginSqliteStorageBridge(manifest.Id, dataRoot, loggerFactory.CreateLogger<PluginSqliteStorageBridge>()) : null,
            capabilities.Contains("logging/v1", StringComparer.Ordinal)
                ? new PluginLogBridge(manifest.Id, loggerFactory.CreateLogger<PluginLogBridge>()) : null,
            capabilities.Contains("notifications/v1", StringComparer.Ordinal)
                ? new PluginNotificationBridge(manifest.Id, toastService, loggerFactory.CreateLogger<PluginNotificationBridge>()) : null,
            capabilities.Contains("protection/v1", StringComparer.Ordinal)
                ? new PluginProtectionBridge(manifest.Id, loggerFactory.CreateLogger<PluginProtectionBridge>()) : null);
    }
}