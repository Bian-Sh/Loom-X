using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class DesktopLongListVirtualizationContractTests
{
    [Fact]
    public void ConsoleAndActivityLongListsUseVirtualizingListControls()
    {
        var consoleSource = ReadDesktopFile("Views", "ConsoleView.axaml");
        var activitySource = ReadDesktopFile("Views", "ActivityView.axaml");

        Assert.Contains("<ListBox", consoleSource, StringComparison.Ordinal);
        Assert.Contains("Classes=\"log-list\"", consoleSource, StringComparison.Ordinal);
        Assert.Contains("<ListBox", activitySource, StringComparison.Ordinal);
        Assert.Contains("Classes=\"activity-list\"", activitySource, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel", consoleSource, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel", activitySource, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] parts)
    {
        var path = Path.Combine(new[]
        {
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "OllamaHub.Desktop"
        }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }
}
