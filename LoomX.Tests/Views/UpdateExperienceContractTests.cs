using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class UpdateExperienceContractTests
{
    [Fact]
    public void 更新安装请求复用正常退出路径和共享服务()
    {
        var app = NormalizeLineEndings(ReadDesktopFile("App.axaml.cs"));
        var mainViewModel = NormalizeLineEndings(ReadDesktopFile("ViewModels", "MainWindowViewModel.cs"));
        var settingsViewModel = NormalizeLineEndings(ReadDesktopFile("ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("requestApplicationExit: () => desktop.Shutdown()", app, StringComparison.Ordinal);
        Assert.Contains("AssistantViewModel? assistantViewModel = null,\n        Action? requestApplicationExit = null)", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("private readonly ReleaseHistoryViewModel releaseHistoryViewModel;", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("IUpdateService updateService = new UpdateService(", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("new UpdateCoordinator(\n            this.dataStore,\n            updateService,", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("new ReleaseHistoryViewModel(\n            updateService,\n            this.dataStore.GetUpdateProxySettingsAsync,", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("releaseHistoryViewModel.Dispose();", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("updateCoordinator.Dispose();", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("if (ownsUpdateCoordinator) updateCoordinator.Dispose();", settingsViewModel, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }

    private static string NormalizeLineEndings(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
}