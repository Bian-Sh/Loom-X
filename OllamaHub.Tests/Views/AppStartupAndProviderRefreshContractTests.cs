using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class AppStartupAndProviderRefreshContractTests
{
    [Fact]
    public void ShutdownPathsReturnWithoutReenteringAvaloniaFrameworkInitialization()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "App.axaml.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("desktop.Shutdown(0);\n                            base.OnFrameworkInitializationCompleted();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("desktop.Shutdown(0);\n                    base.OnFrameworkInitializationCompleted();", source, StringComparison.Ordinal);
        Assert.Contains("Environment.Exit(0);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellBootstrapUsesExplorerShellContext()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "App.axaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("FileName = \"explorer.exe\"", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRefreshRequestsAreCoalesced()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("private readonly object refreshSync", source, StringComparison.Ordinal);
        Assert.Contains("private Task? refreshTask", source, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshLoopAsync()", source, StringComparison.Ordinal);
        Assert.Contains("refreshRequested = true", source, StringComparison.Ordinal);
    }
}
