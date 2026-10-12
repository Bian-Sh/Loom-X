using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class StartupProgressTests
{
    [Fact]
    public void 阶段推进会按顺序更新当前阶段与整体进度()
    {
        var progress = new StartupProgress();

        progress.AdvanceTo(StartupPhase.Database);
        Assert.Equal(StartupPhase.Database, progress.CurrentPhase);
        Assert.True(progress.ProgressValue > 0);

        progress.AdvanceTo(StartupPhase.PluginCompile);
        Assert.Equal(StartupPhase.PluginCompile, progress.CurrentPhase);
        Assert.True(progress.ProgressValue > 0);

        progress.AdvanceTo(StartupPhase.PluginLoad);
        Assert.Equal(StartupPhase.PluginLoad, progress.CurrentPhase);

        progress.AdvanceTo(StartupPhase.Gateway);
        Assert.Equal(StartupPhase.Gateway, progress.CurrentPhase);

        progress.AdvanceTo(StartupPhase.Shell);
        Assert.Equal(StartupPhase.Shell, progress.CurrentPhase);

        progress.Complete();
        Assert.Equal(StartupPhase.Completed, progress.CurrentPhase);
        Assert.Equal(100, progress.ProgressValue);
    }

    [Fact]
    public void 阶段倒退会被拒绝()
    {
        var progress = new StartupProgress();
        progress.AdvanceTo(StartupPhase.PluginLoad);

        var moved = progress.AdvanceTo(StartupPhase.Database);

        Assert.False(moved);
        Assert.Equal(StartupPhase.PluginLoad, progress.CurrentPhase);
    }

    [Fact]
    public void 失败会进入失败态并保留错误摘要()
    {
        var progress = new StartupProgress();
        progress.AdvanceTo(StartupPhase.PluginCompile);

        progress.Fail("插件编译失败");

        Assert.Equal(StartupPhase.Failed, progress.CurrentPhase);
        Assert.True(progress.IsFailed);
        Assert.Equal("插件编译失败", progress.ErrorMessage);
    }
}
