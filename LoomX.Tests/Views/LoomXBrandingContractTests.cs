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

        Assert.Contains("Title=\"Loom-X\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"Loom-X\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("控制中心", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"Loom-X\"", settingsView, StringComparison.Ordinal);
        Assert.Contains("Loom-X 为本地与云端 AI 服务提供统一接入层，支持基于 OpenAI、Anthropic、Ollama 三种Endpoint，让 IDE 与 GitHub Copilot 能接入你选择的模型。把模型选择权交还给开发者，让日常编码更自由、更高效，不再 Token 焦虑。", settingsView, StringComparison.Ordinal);
        Assert.DoesNotContain("控制中心", settingsView, StringComparison.Ordinal);
        Assert.Contains("Loom-X 诊断摘要", settingsViewModel, StringComparison.Ordinal);
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

    [Fact]
    public void 发布脚本只发布Loomx唯一入口()
    {
        var script = ReadRepositoryFile("scripts", "publish-desktop.ps1");

        Assert.Contains("LoomX\\LoomX.csproj", script, StringComparison.Ordinal);
        Assert.Contains("$executables.Count -ne 1", script, StringComparison.Ordinal);
        Assert.Contains("LoomX.exe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("OllamaHub.Desktop", script, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
    {
        return ReadRepositoryFile(["LoomX", .. segments]);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. segments]);
        return File.ReadAllText(Path.GetFullPath(path));
    }
}
