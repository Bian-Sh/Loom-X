using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class MainWindowSidebarAutoCollapseTests
{
    private const double Threshold = 1000;

    private static bool Evaluate(
        double previousWidth,
        double width,
        double accumulatedDelta,
        bool isCollapsed,
        out double nextAccumulatedDelta) =>
        MainWindow.ShouldAutoCollapseSidebar(previousWidth, width, accumulatedDelta, isCollapsed, out nextAccumulatedDelta);

    [Theory]
    [InlineData(-1)] // 尚未记录上次宽度（首次布局）
    [InlineData(0)]
    public void FirstLayoutBelowThreshold_Collapses(double previousWidth)
    {
        Assert.True(Evaluate(previousWidth, 950, 0, isCollapsed: false, out var accumulated));
        Assert.Equal(0, accumulated);
    }

    [Fact]
    public void FirstLayoutAboveThreshold_DoesNotCollapse()
    {
        Assert.False(Evaluate(-1, 1200, 0, isCollapsed: false, out _));
    }

    [Fact]
    public void SingleLargeNarrowingBelowThreshold_Collapses()
    {
        Assert.True(Evaluate(1200, 950, 0, isCollapsed: false, out var accumulated));
        Assert.Equal(0, accumulated); // 确认意图后累计清零
    }

    [Fact]
    public void SlowNarrowingAccumulatesAcrossEvents_ThenCollapses()
    {
        // 模拟慢速拖动：每步仅 -2，单步不触发，累计越过 -5 才折叠。
        Assert.False(Evaluate(950, 948, 0, isCollapsed: false, out var accumulated));
        Assert.Equal(-2, accumulated);
        Assert.False(Evaluate(948, 946, accumulated, isCollapsed: false, out accumulated));
        Assert.Equal(-4, accumulated);
        Assert.True(Evaluate(946, 943, accumulated, isCollapsed: false, out accumulated));
        Assert.Equal(0, accumulated);
    }

    [Fact]
    public void WideningBelowThreshold_NeverCollapses()
    {
        // 本次问题场景：窄窗口下手动展开侧栏后拖宽窗口，中间宽度均低于阈值。
        Assert.False(Evaluate(940, 950, 0, isCollapsed: false, out _));
        Assert.False(Evaluate(950, 990, 0, isCollapsed: false, out _));
        Assert.False(Evaluate(990, 1200, 0, isCollapsed: false, out _));
    }

    [Fact]
    public void WideningBeyondDeadzone_ResetsAccumulation()
    {
        Assert.False(Evaluate(950, 958, 0, isCollapsed: false, out var accumulated));
        Assert.Equal(0, accumulated); // 确认"在拉宽"后累计清零
    }

    [Fact]
    public void JitterWithinDeadzone_NeverCollapses()
    {
        // ±2 往复抖动：方向不断反转，累计值始终达不到死区。
        var accumulated = 0.0;
        var width = 950.0;
        for (var i = 0; i < 20; i++)
        {
            var next = width + (i % 2 == 0 ? -2 : 2);
            Assert.False(Evaluate(width, next, accumulated, isCollapsed: false, out accumulated));
            width = next;
        }
    }

    [Fact]
    public void DirectionChange_RestartsAccumulation()
    {
        // 先收窄 4（未达死区），反向拉宽 2 后累计重置为 +2，再收窄 3 时从 +2 反转为 -3，仍不折叠。
        Assert.False(Evaluate(950, 946, 0, isCollapsed: false, out var accumulated));
        Assert.Equal(-4, accumulated);
        Assert.False(Evaluate(946, 948, accumulated, isCollapsed: false, out accumulated));
        Assert.Equal(2, accumulated);
        Assert.False(Evaluate(948, 945, accumulated, isCollapsed: false, out accumulated));
        Assert.Equal(-3, accumulated);
    }

    [Fact]
    public void DeadzoneBoundary_ExactlyFivePixels_DoesNotCollapse()
    {
        Assert.False(Evaluate(950, 945, 0, isCollapsed: false, out var accumulated));
        Assert.Equal(-5, accumulated);
        Assert.True(Evaluate(945, 944, accumulated, isCollapsed: false, out _));
    }

    [Fact]
    public void CompressingAboveThreshold_DoesNotCollapse()
    {
        Assert.False(Evaluate(1200, 1100, 0, isCollapsed: false, out _));
    }

    [Fact]
    public void AlreadyCollapsed_NeverCollapsesAgain()
    {
        Assert.False(Evaluate(1200, 950, 0, isCollapsed: true, out _));
    }

    [Fact]
    public void ZeroDelta_DoesNotCollapseAndKeepsAccumulation()
    {
        Assert.False(Evaluate(950, 950, -3, isCollapsed: false, out var accumulated));
        Assert.Equal(-3, accumulated);
    }
}