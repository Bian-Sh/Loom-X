using Xunit;

namespace LoomX.Tests.Views;

public sealed class AppModalHostContractTests
{
    [Fact]
    public void 模态宿主是主窗口内部覆盖层且使用固定遮罩和居中内容()
    {
        var axaml = ReadDesktopFile("Controls", "AppModalHost.axaml");

        Assert.Contains("<UserControl", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Window", axaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#A6000000\"", axaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", axaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\"", axaml, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"Overlay_OnPointerPressed\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void 点击遮罩只触发反馈而不会完成当前请求()
    {
        var source = ReadDesktopFile("Controls", "AppModalHost.axaml.cs");

        var handlerStart = source.IndexOf("Overlay_OnPointerPressed", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0);
        var handlerEnd = source.IndexOf("ConfirmButton_OnClick", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.Contains("RunMaskFeedbackAsync", handler, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCompleteCurrent", handler, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
