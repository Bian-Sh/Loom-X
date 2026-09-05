using Microsoft.Extensions.Logging;
using System.Collections.Specialized;
using LoomX.ViewModels;
using LoomX.Logging;
using Xunit;

namespace LoomX.Tests.ViewModels;

public sealed class ConsoleViewModelTests
{
    [Fact]
    public void TryParse_TabDelimitedLineKeepsColumns()
    {
        Assert.True(ConsoleViewModel.TryParse("2026-08-30 12:34:56.789 +08:00\t[ERR]\tActivityViewModel\t活动加载失败", out var entry));

        Assert.Equal("12:34:56.789", entry.Time);
        Assert.Equal("Error", entry.LevelLabel);
        Assert.Equal("ActivityViewModel", entry.Module);
        Assert.Equal("活动加载失败", entry.Message);
    }

    [Fact]
    public void Toggles_FilterByLevelAndExposeCounts()
    {
        var buffer = new RuntimeLogBuffer();
        buffer.Append(LogLevel.Information, "Info", "普通");
        buffer.Append(LogLevel.Debug, "Debug", "调试");
        buffer.Append(LogLevel.Warning, "Warn", "警告");
        buffer.Append(LogLevel.Error, "Error", "错误");

        using var viewModel = new ConsoleViewModel(buffer);

        Assert.Equal(2, viewModel.InfoCount);
        Assert.Equal(1, viewModel.WarningCount);
        Assert.Equal(1, viewModel.ErrorCount);
        Assert.Equal(4, viewModel.VisibleLogs.Count);

        viewModel.ShowInfo = false;
        Assert.Equal(2, viewModel.VisibleLogs.Count);
        Assert.DoesNotContain(viewModel.VisibleLogs, item => item.LevelLabel is "Info" or "Debug");
    }

    [Fact]
    public void ClearSearchCommand_ClearsSearchText()
    {
        using var viewModel = new ConsoleViewModel(new RuntimeLogBuffer()) { SearchText = "request" };

        viewModel.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.False(viewModel.HasSearchText);
    }

    [Fact]
    public void ScrollState_PersistsOffsetAndFollowTailFlag()
    {
        using var viewModel = new ConsoleViewModel(new RuntimeLogBuffer());

        Assert.True(viewModel.FollowTail);
        Assert.Equal(0, viewModel.ScrollOffsetY);

        viewModel.UpdateScrollState(128.5, false);

        Assert.False(viewModel.FollowTail);
        Assert.Equal(128.5, viewModel.ScrollOffsetY);
    }

    [Fact]
    public void CountLabel_OnlyShowsVisibleLogCount()
    {
        var buffer = new RuntimeLogBuffer();
        buffer.Append(LogLevel.Information, "Info", "普通");
        buffer.Append(LogLevel.Error, "Error", "错误");

        using var viewModel = new ConsoleViewModel(buffer);

        Assert.Equal("共 2 条", viewModel.CountLabel);
    }

    [Fact]
    public void AppendedLogUpdatesVisibleCollectionIncrementally()
    {
        var buffer = new RuntimeLogBuffer();
        buffer.Append(LogLevel.Information, "Info", "已有");
        using var viewModel = new ConsoleViewModel(buffer);
        NotifyCollectionChangedAction? action = null;
        viewModel.VisibleLogs.CollectionChanged += (_, args) => action ??= args.Action;

        viewModel.AddEntry(new RuntimeLogEntry(DateTimeOffset.Now, LogLevel.Information, "Info", "新增"));

        Assert.Equal(NotifyCollectionChangedAction.Add, action);
        Assert.Equal(2, viewModel.VisibleLogs.Count);
    }

    [Fact]
    public void LargeLogStreamStaysBoundedWithoutResetNotifications()
    {
        using var viewModel = new ConsoleViewModel(new RuntimeLogBuffer());
        var actions = new List<NotifyCollectionChangedAction>();
        viewModel.VisibleLogs.CollectionChanged += (_, args) => actions.Add(args.Action);

        for (var index = 0; index < 5001; index++)
            viewModel.AddEntry(new RuntimeLogEntry(DateTimeOffset.Now, LogLevel.Information, "Info", $"日志 {index}"));

        Assert.Equal(5000, viewModel.VisibleLogs.Count);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }
}
