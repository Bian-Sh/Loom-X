using Xunit;

namespace LoomX.Tests.Views;

public sealed class LoomXBrandingContractTests
{
    [Fact]
    public void 用户可见品牌使用Loomx而不是技术程序集名()
    {
        var mainWindow = ReadSource("MainWindow.axaml");
        var settingsView = ReadSource("Views", "SettingsView.axaml");
        var settingsViewModel = ReadSource("ViewModels", "SettingsViewModel.cs");

        Assert.Contains("Title=\"Loom-x 控制中心\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"Loom-x\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"Loom-x 控制中心\"", settingsView, StringComparison.Ordinal);
        Assert.Contains("Loom-x 诊断摘要", settingsViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"LoomX\"", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void 运行时身份和启动参数使用Loomx约定()
    {
        var host = ReadSource("LoomXHost.cs");
        var app = ReadSource("App.axaml.cs");
        var logging = ReadSource("Logging", "LoggingBootstrap.cs");
        var settingsCodeBehind = ReadSource("Views", "SettingsView.axaml.cs");

        Assert.Contains("name = \"Loom-x\"", host, StringComparison.Ordinal);
        Assert.Contains("owned_by = \"loomx\"", host, StringComparison.Ordinal);
        Assert.Contains("Local\\LoomX", app, StringComparison.Ordinal);
        Assert.Contains("Local\\LoomX.ShellBootstrap", app, StringComparison.Ordinal);
        Assert.Contains("--loomx-child", app, StringComparison.Ordinal);
        Assert.Contains("--loomx-bootstrap-link=", app, StringComparison.Ordinal);
        Assert.Contains("LOOMX_ALLOW_MULTIPLE_INSTANCES", app, StringComparison.Ordinal);
        Assert.Contains("loomx-.log", logging, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Bian-Sh/Loom-X", settingsCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void 桌面配置服务创建前先执行数据迁移()
    {
        var app = ReadSource("App.axaml.cs");

        var migrationIndex = app.IndexOf("new ApplicationDataMigration", StringComparison.Ordinal);
        var configServiceIndex = app.IndexOf("new ConfigSnapshotService", StringComparison.Ordinal);

        Assert.True(migrationIndex >= 0);
        Assert.True(configServiceIndex > migrationIndex);
    }

    private static string ReadSource(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(Path.GetFullPath(path));
    }
}
