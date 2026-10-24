using Xunit;

namespace LoomX.Tests.Views;

/// <summary>
/// unify-window-material 的源码契约：闪屏第一帧即最终材质、外观初值零数据库读取、
/// 保存链路原子写缓存、对账仅在漂移时切换一次。
/// </summary>
public sealed class AppWindowMaterialContractTests
{
    [Fact]
    public void 闪屏与对话框构造即注册应用级协调点()
    {
        var splashSource = ReadDesktopFile("Views", "SplashWindow.axaml.cs");
        var dialogSource = ReadDesktopFile("Views", "GlassDialogWindow.axaml.cs");

        var splashConstructor = Slice(splashSource, "public SplashWindow()", "private void OnCloseClick");
        Assert.Contains("InitializeComponent();", splashConstructor, StringComparison.Ordinal);
        Assert.Contains("AppearanceCoordinator.ApplyTo(this)", splashConstructor, StringComparison.Ordinal);
        Assert.True(
            splashConstructor.IndexOf("InitializeComponent();", StringComparison.Ordinal)
            < splashConstructor.IndexOf("AppearanceCoordinator.ApplyTo(this)", StringComparison.Ordinal),
            "闪屏必须先完成控件初始化再注册材质协调点。");

        Assert.Contains("AppearanceCoordinator.ApplyTo(this)", dialogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 外观初值读取与注入先于闪屏显示且不开配置库()
    {
        var appSource = NormalizeLineEndings(ReadDesktopFile("App.axaml.cs"));

        var injectIndex = appSource.IndexOf("ApplyStartupAppearance(startupLogger)", StringComparison.Ordinal);
        var dataIndex = appSource.IndexOf("dataStore = new AppDataStore(", StringComparison.Ordinal);
        var showIndex = appSource.IndexOf("splashWindow.Show()", StringComparison.Ordinal);
        Assert.True(injectIndex >= 0 && dataIndex >= 0 && showIndex > injectIndex,
            "外观初值注入必须先于闪屏显示。");
        Assert.True(dataIndex < injectIndex,
            "AppDataStore 构造（配置库占位）先于外观注入，但外观初值本身不得触发读库。");
        Assert.Contains("AppearanceCache.TryRead(AppDataPaths.AppearanceCachePath", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 配置库加载完成后对账且漂移时仅记一次警告()
    {
        var appSource = NormalizeLineEndings(ReadDesktopFile("App.axaml.cs"));

        var initializeIndex = appSource.IndexOf("await viewModel.InitializeAfterStartupAsync();", StringComparison.Ordinal);
        var reconcileIndex = appSource.IndexOf("ReconcileStartupAppearance(cachedAppearance, startupLogger);", StringComparison.Ordinal);
        Assert.True(initializeIndex >= 0 && reconcileIndex > initializeIndex,
            "对账必须发生在配置库初始化应用之后。");
        Assert.Contains("外观缓存与配置库不一致", appSource, StringComparison.Ordinal);
        Assert.Contains("AppearanceCache.TryWrite(", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 设置保存成功路径原子写入外观缓存()
    {
        var appSource = NormalizeLineEndings(ReadDesktopFile("App.axaml.cs"));
        var storeSource = NormalizeLineEndings(ReadDesktopFile("Services", "AppDataStore.cs"));

        Assert.Contains("appearanceCachePath: AppDataPaths.AppearanceCachePath", appSource, StringComparison.Ordinal);
        Assert.Contains("string? appearanceCachePath = null", storeSource, StringComparison.Ordinal);
        Assert.Contains("AppearanceCache.TryWrite(", storeSource, StringComparison.Ordinal);
        Assert.Contains("WriteAppearanceCache(result);", storeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 主窗口外观入口委托应用级协调点()
    {
        var windowSource = NormalizeLineEndings(ReadDesktopFile("MainWindow.axaml.cs"));

        Assert.Contains("public void ApplyTheme(string theme) => appearanceCoordinator.ApplyTheme(theme);", windowSource, StringComparison.Ordinal);
        Assert.Contains("appearanceCoordinator.Apply(enabled, opacity, blurAmount, algorithm);", windowSource, StringComparison.Ordinal);
        Assert.Contains("appearanceCoordinator.ApplyTo(this);", windowSource, StringComparison.Ordinal);
        Assert.Contains("AppearanceCoordinator => appearanceCoordinator", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 协调器以弱引用注册窗口并在关闭后注销()
    {
        var coordinatorSource = NormalizeLineEndings(ReadDesktopFile("Services", "WindowAppearanceCoordinator.cs"));

        Assert.Contains("List<WeakReference<Window>>", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("window.Closed += RegisteredWindowOnClosed;", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("window.Closed -= RegisteredWindowOnClosed;", coordinatorSource, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到起始标记 {startMarker}。");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"找不到结束标记 {endMarker}。");
        return source[start..end];
    }

    private static string ReadDesktopFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]));

    private static string NormalizeLineEndings(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
}
