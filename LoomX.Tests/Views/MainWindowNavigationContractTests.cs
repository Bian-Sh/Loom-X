using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class MainWindowNavigationContractTests
{
    [Fact]
    public void MainWindowCreatesAndReusesAllLongLivedPageViewModels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        foreach (var field in new[]
        {
            "private readonly OverviewViewModel overviewViewModel;",
            "private readonly ProvidersViewModel providersViewModel;",
            "private readonly GatewayViewModel gatewayViewModel;",
            "private readonly ActivityViewModel activityViewModel;",
            "private readonly ConsoleViewModel consoleViewModel;",
            "private readonly SettingsViewModel settingsViewModel;"
        })
            Assert.Contains(field, source, StringComparison.Ordinal);

        Assert.Contains("overviewViewModel = new OverviewViewModel", source, StringComparison.Ordinal);
        Assert.Contains("providersViewModel = new ProvidersViewModel", source, StringComparison.Ordinal);
        Assert.Contains("gatewayViewModel = new GatewayViewModel", source, StringComparison.Ordinal);
        Assert.Contains("activityViewModel = new ActivityViewModel", source, StringComparison.Ordinal);
        Assert.Contains("consoleViewModel = new ConsoleViewModel", source, StringComparison.Ordinal);
        Assert.Contains("settingsViewModel = new SettingsViewModel", source, StringComparison.Ordinal);

        Assert.Contains("ShowView(\"nav.overview\", overviewViewModel)", source, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.providers\", providersViewModel)", source, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.gateway\", gatewayViewModel)", source, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.activity\", activityViewModel)", source, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.console\", consoleViewModel)", source, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.settings\", settingsViewModel)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowView(\"nav.overview\", new OverviewViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowView(\"nav.providers\", new ProvidersViewModel", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(() => _ = RefreshAsync())", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationUsesPersistentActiveStateAndAnimatedSharedSelection()
    {
        var windowSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "MainWindow.axaml"));
        var viewModelSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("<Button Command=\"{Binding NavigateCommand}\" Classes=\"nav-button\" Classes.active=\"{Binding IsActive}\">", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<ToggleButton Command=\"{Binding NavigateCommand}\"", windowSource, StringComparison.Ordinal);
        Assert.Contains("Classes=\"navigation-selection-indicator\"", windowSource, StringComparison.Ordinal);
        Assert.Contains("Classes=\"navigation-selection-outline\"", windowSource, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Panel.ZIndex\" Value=\"1\" />", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<DoubleTransition Property=\"Y\"", windowSource, StringComparison.Ordinal);
        Assert.Contains("Button.nav-button.active:pointerover", windowSource, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", windowSource[windowSource.IndexOf("Button.nav-button.active:pointerover", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("public double SelectedNavigationOffset", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public bool HasActiveNavigationItem", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public const double LayoutStep = 46", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("SetActive(\"nav.overview\")", viewModelSource, StringComparison.Ordinal);

        var codeBehindSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "MainWindow.axaml.cs"));
        Assert.Contains("NavigationViewModel_OnPropertyChanged", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("NavigationSelectionAnimationDurationMs = 200", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("CubicEaseOut NavigationSelectionEasing", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("var eased = NavigationSelectionEasing.Ease(progress)", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("navigationSelectionAnimationTimer", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("navigationSelectionIndicatorTransform.Y = offset", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("navigationSelectionOutlineTransform.Y = offset", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("navigationSelectionIndicator.RenderTransform = new TranslateTransform", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("navigationSelectionOutline.RenderTransform = new TranslateTransform", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(() =>", codeBehindSource, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("左侧导航选中框切换请求", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("左侧导航选中框动画开始", codeBehindSource, StringComparison.Ordinal);
        Assert.DoesNotContain("左侧导航选中框动画完成", codeBehindSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewViewDoesNotDisposeTheLongLivedPageViewModel()
    {
        var viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "Views", "OverviewView.axaml.cs");
        var viewSource = File.ReadAllText(viewPath);

        var vmPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var vmSource = File.ReadAllText(vmPath);

        // 概览页被导航切换替换时会脱离可视树；View 不拥有 ViewModel，
        // 在这里 Dispose 会永久解除 CultureChanged / ConfigurationChanged / StateChanged 订阅。
        Assert.DoesNotContain("Dispose()", viewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DetachedFromVisualTree", viewSource, StringComparison.Ordinal);
        Assert.Contains("overviewViewModel.Dispose()", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewRefreshesViaPushEventsInsteadOfManualTrigger()
    {
        var vmPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var overviewViewModel = ExtractTopLevelType(File.ReadAllText(vmPath), "public sealed class OverviewViewModel");

        Assert.Contains("dataStore.ConfigurationChanged += OnConfigurationChanged", overviewViewModel, StringComparison.Ordinal);
        Assert.Contains("gatewayService.StateChanged += OnGatewayStateChanged", overviewViewModel, StringComparison.Ordinal);
        Assert.Contains("gatewayService.TelemetryPublished += OnTelemetryPublished", overviewViewModel, StringComparison.Ordinal);
        Assert.Contains("LocaleService.CultureChanged += OnCultureChanged", overviewViewModel, StringComparison.Ordinal);
        Assert.Contains("LocaleService.CultureChanged -= OnCultureChanged", overviewViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public ICommand RefreshCommand", overviewViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceRefreshOnlyRespondsToSettingsChanges()
    {
        var vmPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(vmPath);

        Assert.Contains("if (args.Source == ConfigurationChangeSource.LocalSave && args.Kind != ConfigurationChangeKind.Settings) return;", source, StringComparison.Ordinal);
    }

    private static string ExtractTopLevelType(string source, string declaration)
    {
        var start = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到顶层类型声明 {declaration}");

        var next = source.IndexOf("\npublic ", start + declaration.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }
}
