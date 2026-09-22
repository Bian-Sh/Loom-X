using Microsoft.Extensions.Logging;
using LoomX.Logging;
using Xunit;

namespace LoomX.Tests.Logging;

public sealed class RuntimeLogBufferTests
{
    [Fact]
    public void Append_PreservesEntryAndPublishesChange()
    {
        var buffer = new RuntimeLogBuffer();
        RuntimeLogEntry? received = null;
        buffer.EntryAdded += (_, entry) => received = entry;

        buffer.Append(LogLevel.Error, "ActivityViewModel", "活动加载失败");

        var entry = Assert.Single(buffer.Snapshot());
        Assert.Same(entry, received);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("ActivityViewModel", entry.Category);
        Assert.Equal("活动加载失败", entry.Message);
    }
}
