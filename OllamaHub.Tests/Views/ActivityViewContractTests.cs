using System.IO;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class ActivityViewContractTests
{
    [Fact]
    public void ActivityViewBindsHistoryLoadingAndPendingActivityControls()
    {
        var viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "ActivityView.axaml");
        var viewSource = File.ReadAllText(viewPath);
        var codePath = Path.Combine(Path.GetDirectoryName(viewPath)!, "ActivityView.axaml.cs");
        var codeSource = File.ReadAllText(codePath);

        Assert.Contains("Command=\"{Binding ReturnToLatestCommand}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding PendingActivityLabel}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"{Binding IsLoadingMore}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("ScrollChanged=\"ActivityScrollViewer_OnScrollChanged\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("NotifyScrollMetrics", codeSource, StringComparison.Ordinal);
        Assert.Contains("ScrollToTopRequested", codeSource, StringComparison.Ordinal);
        Assert.Contains("activityScrollViewer.Offset = new Vector(0, 0)", codeSource, StringComparison.Ordinal);
    }
}
