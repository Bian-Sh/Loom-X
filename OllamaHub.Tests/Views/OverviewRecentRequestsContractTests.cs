using OllamaHub.Activity;
using OllamaHub.Desktop.ViewModels;
using Xunit;

namespace OllamaHub.Tests.Views;

public sealed class OverviewRecentRequestsContractTests
{
    [Fact]
    public void DatabaseRecordMapsToRecentRequestWithStatusAndTimestamp()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-02T09:10:11+08:00");
        var item = OverviewRecentRequestViewModel.From(new ActivityEventRecord(
            7,
            createdAt,
            "req_database",
            "POST",
            "/v1/chat/completions",
            "OpenAI",
            "OpenAI 直通",
            "provider-a",
            "model-a",
            200,
            123,
            512,
            false,
            null));

        Assert.Equal("req_database", item.RequestId);
        Assert.Equal(createdAt, item.CreatedAt);
        Assert.Equal("OpenAI", item.Endpoint);
        Assert.Equal("model-a", item.Model);
        Assert.Equal("成功", item.Status);
        Assert.Equal("123 ms", item.Latency);
    }

    [Fact]
    public void OverviewViewContainsPersistedQueryAndStatusColumn()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OllamaHub.Desktop", "Views", "OverviewView.axaml");
        var source = File.ReadAllText(path);

        Assert.Contains("ActivityQueryService", File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "..", "ViewModels", "MainWindowViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("new ActivityQuery(Limit: 8)", File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "..", "ViewModels", "MainWindowViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("RecentRequestsEmpty", File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "..", "ViewModels", "MainWindowViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("Text=\"暂无请求活动\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Status}\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"0\" Text=\"{Binding Time}\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\" Text=\"{Binding Endpoint}\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\" Text=\"{Binding Model}\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"3\" Text=\"{Binding Status}\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"4\" Text=\"{Binding Latency}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedAndRealtimeRowsAreMergedByRequestIdAndLimitedToEight()
    {
        var persisted = Enumerable.Range(0, 8)
            .Select(index => OverviewRecentRequestViewModel.From(CreateRecord($"req_{index}", index)))
            .ToArray();
        var duplicate = OverviewRecentRequestViewModel.From(CreateRecord("req_3", 99));
        var realtime = Enumerable.Range(8, 3)
            .Select(index => OverviewRecentRequestViewModel.From(CreateRecord($"req_{index}", index)))
            .Append(duplicate);

        var merged = OverviewRecentRequestViewModel.Merge(persisted, realtime);

        Assert.Equal(8, merged.Count);
        Assert.Equal("req_3", merged[0].RequestId);
        Assert.Equal(1, merged.Count(item => item.RequestId == "req_3"));
    }

    private static ActivityEventRecord CreateRecord(string requestId, int seconds) => new(
        seconds,
        DateTimeOffset.Parse("2026-09-02T09:00:00+08:00").AddSeconds(seconds),
        requestId,
        "POST",
        "/v1/chat/completions",
        "OpenAI",
        "OpenAI 直通",
        "provider-a",
        "model-a",
        200,
        100,
        0,
        false,
        null);
}
