using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class SplashStartupContractTests
{
    [Fact]
    public void 应用启动先显示独立闪屏再切换主窗口()
    {
        var appSource = ReadDesktopFile("App.axaml.cs");

        Assert.Contains("new SplashWindow", appSource, StringComparison.Ordinal);
        Assert.Contains("desktop.MainWindow = splashWindow", appSource, StringComparison.Ordinal);
        Assert.Contains("StartupOrchestrator", appSource, StringComparison.Ordinal);
        Assert.Contains("splashWindow.Close()", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 闪屏显示真实启动阶段和总体进度()
    {
        var viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "SplashWindow.axaml");
        var viewModelPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "SplashViewModel.cs");
        var view = File.ReadAllText(viewPath);
        var viewModel = File.ReadAllText(viewModelPath);

        Assert.Contains("ProgressValue", view, StringComparison.Ordinal);
        Assert.Contains("StageItems", view, StringComparison.Ordinal);
        Assert.Contains("StartupPhase.PluginCompile", viewModel, StringComparison.Ordinal);
        Assert.Contains("StartupPhase.PluginLoad", viewModel, StringComparison.Ordinal);
        Assert.Contains("StartupPhase.Gateway", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void 主窗口构造不会隐式初始化助手服务()
    {
        var source = ReadDesktopFile("ViewModels", "AssistantViewModel.cs");

        var constructorStart = source.IndexOf("    public AssistantViewModel(", StringComparison.Ordinal);
        var constructorEnd = source.IndexOf("        StartElapsedTimer();", constructorStart, StringComparison.Ordinal);
        Assert.True(constructorStart >= 0 && constructorEnd > constructorStart);
        var constructor = source[constructorStart..constructorEnd];

        Assert.DoesNotContain("RefreshModelSummary();", constructor, StringComparison.Ordinal);
    }

    [Fact]
    public void 助手模型摘要优先复用数据中心内存快照()
    {
        // desktop-startup-splash「主界面初始化不读库」：启动首刷的模型分组必须先取内存快照，
        // 数据库读取（ListAvailableModelsAsync）只能是数据中心不可用时的回落。
        var source = ReadDesktopFile("ViewModels", "AssistantViewModel.cs");
        var methodStart = source.IndexOf("    private async void RefreshModelSummary()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "未找到 RefreshModelSummary。");
        var nextMethod = source.IndexOf("    private void UpdateModelSummary()", methodStart, StringComparison.Ordinal);
        Assert.True(nextMethod > methodStart, "未找到 RefreshModelSummary 的结束边界。");
        var method = source[methodStart..nextMethod];

        Assert.Contains("dataStore is { IsInitialized: true }", method, StringComparison.Ordinal);
        Assert.Contains("GroupAvailableModels", method, StringComparison.Ordinal);
        Assert.Contains("ListAvailableModelsAsync", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("dataStore is { IsInitialized: true }", StringComparison.Ordinal)
            < method.IndexOf("ListAvailableModelsAsync", StringComparison.Ordinal),
            "内存快照必须是首选数据源，数据库读取只能是回落。");
    }

    private static string ReadDesktopFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]));
}
