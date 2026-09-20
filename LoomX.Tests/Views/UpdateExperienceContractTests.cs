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

    [Fact]
    public void 主窗口使用单层更新浮窗并直接展示共享更新说明()
    {
        var source = ReadDesktopFile("MainWindow.axaml");
        var toastStart = source.IndexOf("x:Name=\"toastBorder\"", StringComparison.Ordinal);
        var dialogStart = source.IndexOf("x:Name=\"updateDialogOverlay\"", StringComparison.Ordinal);

        Assert.Contains("xmlns:views=\"using:LoomX.Views\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsDialogVisible}\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"760\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Update.LatestVersion}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Update.ReleaseNotesContent.PublishedAtText}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Update.StatusText}\"", source, StringComparison.Ordinal);
        Assert.Contains("<views:ReleaseNotesView DataContext=\"{Binding Update.ReleaseNotesContent}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsDownloading}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsVerifying}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.CanInstall}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.CanRetry}\"", source, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding Update.DownloadPercent}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Update.InstallAndRestartCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Update.RetryCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Update.DismissDialogCommand}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Update.CardVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Update.ReleaseNotesVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Update.OpenReleaseNotesCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Update.ReleaseNotes}\"", source, StringComparison.Ordinal);
        Assert.True(toastStart >= 0 && dialogStart > toastStart, "Toast 必须保持独立层并位于更新浮窗后方。");
    }

    [Fact]
    public void 更新浮窗支持Escape关闭并在打开后异步聚焦()
    {
        var source = ReadDesktopFile("MainWindow.axaml.cs");

        Assert.Contains("Key.Escape", source, StringComparison.Ordinal);
        Assert.Contains("DismissDialogCommand.Execute(null)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(UpdateCoordinator.IsDialogVisible)", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(FocusUpdateDialogAction", source, StringComparison.Ordinal);
        Assert.Contains("updateInstallButton", source, StringComparison.Ordinal);
        Assert.Contains("updateRetryButton", source, StringComparison.Ordinal);
        Assert.Contains("updateDialogCloseButton", source, StringComparison.Ordinal);
        Assert.Contains("if (IsInsideButton(e.Source)) return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新展示适配在未接入协调器前不创建说明模型()
    {
        using var presentation = new UpdateWindowPresentation();

        Assert.Null(presentation.Update.ReleaseNotesContent);
    }
    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }

    private static string NormalizeLineEndings(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
}