using System.IO;
using System.Reflection;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class UpdateExperienceContractTests
{
    [Fact]
    public void Debug更新预览仅支持固定场景且由编译条件隔离()
    {
        var mainViewModel = NormalizeLineEndings(ReadDesktopFile("ViewModels", "MainWindowViewModel.cs"));
        var previewService = NormalizeLineEndings(ReadDesktopFile("Services", "DebugUpdatePreviewService.cs"));

        Assert.Contains("#if DEBUG\n        updateService = DebugUpdatePreviewService.CreateFromEnvironment(updateService);\n        updatePreviewEnabled = updateService is DebugUpdatePreviewService;\n#endif", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("#if DEBUG\n            if (updatePreviewEnabled) _ = updateCoordinator.CheckNowAsync();\n#endif", mainViewModel, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG\n", previewService, StringComparison.Ordinal);
        Assert.EndsWith("#endif\n", previewService, StringComparison.Ordinal);
        Assert.Contains("LOOMX_UPDATE_PREVIEW", previewService, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n\\nLOOMX_UPDATE_PREVIEW\"", previewService, StringComparison.Ordinal);
        Assert.DoesNotContain("安全链接", previewService, StringComparison.Ordinal);
        Assert.DoesNotContain("独立折叠", previewService, StringComparison.Ordinal);
        Assert.Contains("\"downloading\" or \"verifying\" or \"ready\" or \"error\" or \"history-empty\"", previewService, StringComparison.Ordinal);
        Assert.DoesNotContain("default:", previewService, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新安装请求复用正常退出路径和共享服务()
    {
        var app = NormalizeLineEndings(ReadDesktopFile("App.axaml.cs"));
        var mainViewModel = NormalizeLineEndings(ReadDesktopFile("ViewModels", "MainWindowViewModel.cs"));
        var settingsViewModel = NormalizeLineEndings(ReadDesktopFile("ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("requestApplicationExit: () => desktop.Shutdown()", app, StringComparison.Ordinal);
        Assert.Contains("confirmUpdateInstall: mainWindow.ConfirmUpdateInstallAsync", app, StringComparison.Ordinal);
        Assert.Contains("AssistantViewModel? assistantViewModel = null,\n        Action? requestApplicationExit = null,\n        Func<Task<bool>>? confirmUpdateInstall = null,\n        Action<string>? applyTheme = null,\n        IWindowsStartupService? windowsStartupService = null)", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("private readonly ReleaseHistoryViewModel releaseHistoryViewModel;", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("IUpdateService updateService = new UpdateService(", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("new UpdateCoordinator(\n            this.dataStore,\n            updateService,", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("new ReleaseHistoryViewModel(\n            updateService,\n            this.dataStore.GetUpdateProxySettingsAsync,", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("releaseHistory: releaseHistoryViewModel", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("releaseHistoryViewModel.Dispose();", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("updateCoordinator.Dispose();", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("if (ownsReleaseHistory) ReleaseHistory.Dispose();", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("if (ownsUpdateCoordinator) updateCoordinator.Dispose();", settingsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void 主窗口通过悬停卡片展示更新说明且不再使用更新模态()
    {
        var source = ReadDesktopFile("MainWindow.axaml");
        var popupStart = source.IndexOf("x:Name=\"updateReleasePreviewPopup\"", StringComparison.Ordinal);
        var popupEnd = popupStart >= 0 ? source.IndexOf("</Popup>", popupStart, StringComparison.Ordinal) : -1;

        Assert.Contains("<Popup x:Name=\"updateReleasePreviewPopup\"", source, StringComparison.Ordinal);
        Assert.Contains("PlacementTarget=\"{Binding #updateEntryButton}\"", source, StringComparison.Ordinal);
        Assert.Contains("Placement=\"BottomEdgeAlignedRight\"", source, StringComparison.Ordinal);
        Assert.Contains("<Border Padding=\"16,8,16,24\" Background=\"Transparent\"", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"380\" MaxHeight=\"420\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerEntered=\"UpdateReleasePreview_OnPointerEntered\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerExited=\"UpdateReleasePreview_OnPointerExited\"", source, StringComparison.Ordinal);
        Assert.Contains("<views:ReleaseNotesView DataContext=\"{Binding Update.ReleaseNotesContent}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Update.LatestVersion}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Update.ReleaseNotesContent.PublishedAtText}\"", source, StringComparison.Ordinal);
        Assert.True(popupStart >= 0 && popupEnd > popupStart, "找不到更新说明悬停卡片。");
        Assert.DoesNotContain("<Button", source[popupStart..popupEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("updateDialogOverlay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Update.IsDialogVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExperimentalAcrylicBorder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新入口下载中保持悬停能力且点击使用统一激活命令()
    {
        var source = ReadDesktopFile("MainWindow.axaml");
        var codeBehind = ReadDesktopFile("MainWindow.axaml.cs");

        Assert.Contains("Command=\"{Binding Update.ActivateUpdateEntryCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerEntered=\"UpdateEntryButton_OnPointerEntered\"", source, StringComparison.Ordinal);
        Assert.Contains("PointerExited=\"UpdateEntryButton_OnPointerExited\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Update.IsPreparing}\"", source, StringComparison.Ordinal);
        Assert.Contains("updateReleasePreviewPopup.IsOpen = true", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleDialogCommand", source + codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DismissDialogCommand", source + codeBehind, StringComparison.Ordinal);
        Assert.Contains("<controls:AppModalHost x:Name=\"appModalHost\"", source, StringComparison.Ordinal);
        Assert.Contains("ZIndex=\"300\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新展示适配在未接入协调器前不创建说明模型()
    {
        using var presentation = new UpdateWindowPresentation();

        Assert.Null(presentation.Update.ReleaseNotesContent);
    }

    [Fact]
    public void 下载百分比变化只转发对应展示属性()
    {
        using var source = UpdateCoordinatorOwner.Create();
        using var presentation = new UpdateWindowPresentation();
        presentation.Attach(source.Coordinator);
        var notifications = new List<string?>();
        presentation.Update.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        RaiseCoordinatorPropertyChanged(source.Coordinator, nameof(UpdateCoordinator.DownloadPercent));

        Assert.Equal([nameof(UpdateWindowPresentationAdapter.DownloadPercent)], notifications);
        Assert.DoesNotContain(notifications, string.IsNullOrEmpty);
        Assert.DoesNotContain(nameof(UpdateWindowPresentationAdapter.UpdateEntryText), notifications);
        Assert.DoesNotContain(nameof(UpdateWindowPresentationAdapter.StatusText), notifications);
        Assert.DoesNotContain(nameof(UpdateWindowPresentationAdapter.ProgressText), notifications);
        Assert.DoesNotContain(nameof(UpdateWindowPresentationAdapter.SpeedText), notifications);
        Assert.DoesNotContain(nameof(UpdateWindowPresentationAdapter.IsDownloading), notifications);
        Assert.DoesNotContain(nameof(UpdateWindowPresentationAdapter.ReleaseNotesContent), notifications);
    }

    [Fact]
    public void 本地化展示属性变化按名称精确转发()
    {
        using var source = UpdateCoordinatorOwner.Create();
        using var presentation = new UpdateWindowPresentation();
        presentation.Attach(source.Coordinator);
        var notifications = new List<string?>();
        presentation.Update.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        RaiseCoordinatorPropertyChanged(source.Coordinator, nameof(UpdateCoordinator.StatusText));
        RaiseCoordinatorPropertyChanged(source.Coordinator, nameof(UpdateCoordinator.UpdateEntryText));

        Assert.Equal(
            [nameof(UpdateWindowPresentationAdapter.StatusText), nameof(UpdateWindowPresentationAdapter.UpdateEntryText)],
            notifications);
        Assert.DoesNotContain(notifications, string.IsNullOrEmpty);
    }

    [Fact]
    public void 替换脱离和销毁后旧协调器事件不再转发()
    {
        using var first = UpdateCoordinatorOwner.Create();
        using var second = UpdateCoordinatorOwner.Create();
        var presentation = new UpdateWindowPresentation();
        presentation.Attach(first.Coordinator);
        var notifications = new List<string?>();
        presentation.Update.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        presentation.Attach(second.Coordinator);
        Assert.NotEmpty(notifications);
        Assert.DoesNotContain(notifications, string.IsNullOrEmpty);
        notifications.Clear();
        RaiseCoordinatorPropertyChanged(first.Coordinator, nameof(UpdateCoordinator.DownloadPercent));
        Assert.Empty(notifications);

        RaiseCoordinatorPropertyChanged(second.Coordinator, nameof(UpdateCoordinator.DownloadPercent));
        Assert.Equal([nameof(UpdateWindowPresentationAdapter.DownloadPercent)], notifications);

        notifications.Clear();
        presentation.Attach(null);
        Assert.NotEmpty(notifications);
        Assert.DoesNotContain(notifications, string.IsNullOrEmpty);
        notifications.Clear();
        RaiseCoordinatorPropertyChanged(second.Coordinator, nameof(UpdateCoordinator.DownloadPercent));
        Assert.Empty(notifications);

        presentation.Attach(first.Coordinator);
        notifications.Clear();
        presentation.Dispose();
        RaiseCoordinatorPropertyChanged(first.Coordinator, nameof(UpdateCoordinator.DownloadPercent));
        Assert.Empty(notifications);
    }

    private static void RaiseCoordinatorPropertyChanged(UpdateCoordinator coordinator, string propertyName)
    {
        var method = typeof(NotifyViewModel).GetMethod(
            "OnPropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到属性通知入口。");
        method.Invoke(coordinator, [propertyName]);
    }

    private sealed class UpdateCoordinatorOwner : IDisposable
    {
        private readonly string directory;
        private readonly ConfigSnapshotService configService;
        private readonly GatewayProcessService gatewayService;
        private readonly AppDataStore dataStore;

        private UpdateCoordinatorOwner(
            string directory,
            ConfigSnapshotService configService,
            GatewayProcessService gatewayService,
            AppDataStore dataStore,
            UpdateCoordinator coordinator)
        {
            this.directory = directory;
            this.configService = configService;
            this.gatewayService = gatewayService;
            this.dataStore = dataStore;
            Coordinator = coordinator;
        }

        public UpdateCoordinator Coordinator { get; }

        public static UpdateCoordinatorOwner Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var configService = new ConfigSnapshotService(Path.Combine(directory, "LoomX.db"));
            var gatewayService = new GatewayProcessService();
            var dataStore = new AppDataStore(configService, gatewayService);
            var coordinator = new UpdateCoordinator(dataStore, dispatch: action => action());
            return new UpdateCoordinatorOwner(directory, configService, gatewayService, dataStore, coordinator);
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            dataStore.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }

    private static string NormalizeLineEndings(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
}
