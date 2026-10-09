using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class AppStartupAndProviderRefreshContractTests
{
    [Fact]
    public void ShutdownPathsReturnWithoutReenteringAvaloniaFrameworkInitialization()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "App.axaml.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("desktop.Shutdown(0);\n                            base.OnFrameworkInitializationCompleted();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("desktop.Shutdown(0);\n                    base.OnFrameworkInitializationCompleted();", source, StringComparison.Ordinal);
        Assert.Contains("Environment.Exit(0);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellBootstrapUsesExplorerShellContext()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "App.axaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("FileName = \"explorer.exe\"", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRefreshRequestsAreCoalesced()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("private readonly object refreshSync", source, StringComparison.Ordinal);
        Assert.Contains("private Task? refreshTask", source, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshLoopAsync()", source, StringComparison.Ordinal);
        Assert.Contains("refreshRequested = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationReloadIsExplicitInsteadOfPeriodicOrFileDriven()
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "LoomXHost.cs");
        var providerPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Configuration", "DatabaseConfigurationProvider.cs");
        var snapshotPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Services", "ConfigSnapshotService.cs");
        var storePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Services", "AppDataStore.cs");

        Assert.DoesNotContain("ConfigurationRefreshService", File.ReadAllText(hostPath), StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicTimer", File.ReadAllText(providerPath), StringComparison.Ordinal);
        Assert.DoesNotContain("FileSystemWatcher", File.ReadAllText(snapshotPath), StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalChangeDetected", File.ReadAllText(snapshotPath), StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalChangeDetected", File.ReadAllText(storePath), StringComparison.Ordinal);
    }

    [Fact]
    public void 应用启动接入Windows自启动与网关意图恢复()
    {
        var appSource = ReadDesktopFile("App.axaml.cs");
        var mainViewModelSource = ReadDesktopFile("ViewModels", "MainWindowViewModel.cs");
        var initializeMethod = Slice(mainViewModelSource, "private async Task InitializeDataStoreAsync()", "private void OnConfigurationChanged");

        Assert.Contains("var windowsStartupService = new WindowsStartupService(", appSource, StringComparison.Ordinal);
        Assert.Contains("windowsStartupService: windowsStartupService", appSource, StringComparison.Ordinal);
        Assert.Contains("IWindowsStartupService? windowsStartupService = null", mainViewModelSource, StringComparison.Ordinal);
        Assert.Contains("windowsStartupService: this.windowsStartupService", mainViewModelSource, StringComparison.Ordinal);
        Assert.Contains("new ApplicationStartupCoordinator(", mainViewModelSource, StringComparison.Ordinal);
        Assert.Contains("await dataStore.InitializeAsync();", initializeMethod, StringComparison.Ordinal);
        Assert.Contains("dataStore.CurrentConfig.Server.Urls.FirstOrDefault()", initializeMethod, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:11434", initializeMethod, StringComparison.Ordinal);
        Assert.Contains("await startupCoordinator.RestoreAsync(", initializeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SetGatewayRunningAsync", initializeMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void 应用退出仅停止网关且不清零运行意图()
    {
        var appSource = ReadDesktopFile("App.axaml.cs");
        var exitHandler = Slice(appSource, "desktop.Exit +=", "base.OnFrameworkInitializationCompleted();");

        Assert.Contains("await gatewayService.StopAsync();", exitHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("SetGatewayRunningAsync", exitHandler, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadDesktopFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]));
}
