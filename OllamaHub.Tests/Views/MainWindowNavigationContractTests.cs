using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class MainWindowNavigationContractTests
{
    [Fact]
    public void MainWindowCreatesAndReusesAllLongLivedPageViewModels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        foreach (var field in new[]
        {
            "private readonly OverviewViewModel overviewViewModel;",
            "private readonly ProvidersViewModel providersViewModel;",
            "private readonly GatewayViewModel gatewayViewModel;",
            "private readonly ActivityViewModel activityViewModel;",
            "private readonly ConsoleViewModel consoleViewModel;",
            "private readonly SettingsViewModel settingsViewModel;"
        })
            Assert.Contains(field, source, StringComparison.Ordinal);

        Assert.Contains("overviewViewModel = new OverviewViewModel", source, StringComparison.Ordinal);
        Assert.Contains("providersViewModel = new ProvidersViewModel", source, StringComparison.Ordinal);
        Assert.Contains("gatewayViewModel = new GatewayViewModel", source, StringComparison.Ordinal);
        Assert.Contains("activityViewModel = new ActivityViewModel", source, StringComparison.Ordinal);
        Assert.Contains("consoleViewModel = new ConsoleViewModel", source, StringComparison.Ordinal);
        Assert.Contains("settingsViewModel = new SettingsViewModel", source, StringComparison.Ordinal);

        Assert.Contains("CurrentView = overviewViewModel", source, StringComparison.Ordinal);
        Assert.Contains("CurrentView = providersViewModel", source, StringComparison.Ordinal);
        Assert.Contains("CurrentView = gatewayViewModel", source, StringComparison.Ordinal);
        Assert.Contains("CurrentView = activityViewModel", source, StringComparison.Ordinal);
        Assert.Contains("CurrentView = consoleViewModel", source, StringComparison.Ordinal);
        Assert.Contains("CurrentView = settingsViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentView = new OverviewViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentView = new ProvidersViewModel", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(() => _ = RefreshAsync())", source, StringComparison.Ordinal);
    }
}
