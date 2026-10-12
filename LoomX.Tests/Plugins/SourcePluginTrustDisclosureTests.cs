using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using LoomX.Localization;
using LoomX.Plugins.Host;
using LoomX.Services;
using LoomX.Tests.Views;
using LoomX.ViewModels;
using LoomX.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

[Collection("Avalonia UI")]
public sealed class SourcePluginTrustDisclosureTests : IDisposable
{
    private readonly List<SourcePluginTestPackage> packages = [];
    private const string DisclosureKey = "plugins.source.trust.description";
    private const string EnabledKey = "plugins.source.trust.enabled";

    [Fact]
    public void 源码插件没有自带详情时仍能打开宿主可信边界说明()
    {
        using var gateway = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gateway);
        var item = CreateItem(viewModel);
        viewModel.Plugins.Add(item);

        Assert.True(item.HasDetailUi);
        Assert.True(item.CanOpenDetail);
        item.OpenDetailCommand.Execute(null);
        Assert.Same(item, viewModel.SelectedPlugin);
        Assert.True(viewModel.IsPluginDetailVisible);
    }

    [Theory]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", false)]
    [InlineData("zh-TW", true)]
    [InlineData("ja-JP", false)]
    [InlineData("ja-JP", true)]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    public void 源码插件卡片与详情实际可见可信边界警示(string culture, bool detail)
    {
        using var locale = new TestLocale(culture);
        using var gateway = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gateway);
        var item = CreateItem(viewModel);
        viewModel.Plugins.Add(item);
        if (detail) item.OpenDetailCommand.Execute(null);

        var view = Render(viewModel);
        var disclosure = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == ResourceLookup.Resolve(DisclosureKey));
        Assert.True(disclosure.Bounds.Width > 0);
        Assert.True(disclosure.Bounds.Height > 0);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, disclosure.TextWrapping);
    }

    [Theory]
    [InlineData("zh-CN", "可信", "同一进程", "当前用户", "操作系统沙箱", "两者均不构成")]
    [InlineData("zh-TW", "可信", "同一處理程序", "目前使用者", "作業系統沙箱", "兩者均不構成")]
    [InlineData("ja-JP", "信頼", "同一プロセス", "現在のユーザー", "OS サンドボックス", "どちらも OS サンドボックスではなく")]
    [InlineData("en-US", "trusted", "same process", "current user", "OS sandbox", "Neither is an OS sandbox")]
    public void 四语言警示明确来源权限与非沙箱且启用反馈保留完整边界(
        string culture, string trusted, string process, string user, string sandbox, string notSandbox)
    {
        var resources = new System.Resources.ResourceManager("LoomX.Resources.Strings", typeof(PluginsViewModel).Assembly);
        foreach (var key in new[] { DisclosureKey, EnabledKey })
        {
            var text = resources.GetString(key, CultureInfo.GetCultureInfo(culture));
            Assert.NotNull(text);
            Assert.Contains(trusted, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(process, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(user, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(sandbox, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AssemblyLoadContext", text, StringComparison.Ordinal);
            Assert.Contains("Bridge", text, StringComparison.Ordinal);
            Assert.Contains(notSandbox, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", true)]
    [InlineData("ja-JP", true)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    public async Task 真实启用成功时源码警告不泄漏显示文案而程序集保留原反馈(string culture, bool source)
    {
        using var locale = new TestLocale(culture);
        var package = new SourcePluginTestPackage();
        packages.Add(package);
        await using var runtime = PluginRuntime.Start(new PluginRuntimeOptions
        {
            PluginDirectory = package.Root,
            DataRootDirectory = Path.Combine(package.Root, "data"),
            SourceCompiler = package.Compiler,
            DisabledPlugins = new HashSet<string>(StringComparer.Ordinal) { "demo" },
        }, NullLoggerFactory.Instance);
        var info = Assert.Single(runtime.PluginInfos);
        Assert.False(info.Enabled);
        Assert.Equal("csharp-source", info.RuntimeKind);
        using var gateway = new GatewayProcessService();
        var toast = new ToastService();
        var notifications = new List<ToastNotification>();
        toast.Requested += (_, notification) => notifications.Add(notification);
        using var viewModel = new PluginsViewModel(gateway, toast, uiDispatcher: action => action());
        typeof(PluginsViewModel).GetField("runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, runtime);
        var item = CreateItem(viewModel, info, source);
        viewModel.Plugins.Add(item);

        item.Enabled = true;

        Assert.True(item.Enabled);
        Assert.True(Assert.Single(runtime.PluginInfos).Enabled);
        var notification = Assert.Single(notifications);
        Assert.Equal(source ? ToastLevel.Warning : ToastLevel.Success, notification.Level);
        if (source)
        {
            Assert.Equal(ResourceLookup.Resolve(EnabledKey), notification.Message);
            Assert.DoesNotContain("sk-source-display-secret", notification.Message, StringComparison.Ordinal);
        }
        else
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, ResourceLookup.Resolve("plugins.toast.enabled"), item.DisplayName), notification.Message);
        notifications.Clear();
        item.Enabled = true;
        Assert.Empty(notifications);
        item.Enabled = false;
        Assert.True(Assert.Single(notifications).Message != ResourceLookup.Resolve(EnabledKey));
    }

    [Fact]
    public void 默认程序集插件保持原有详情与生命周期规则()
    {
        using var gateway = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gateway);
        var item = CreateItem(viewModel, source: false);
        viewModel.Plugins.Add(item);
        Assert.False(item.HasDetailUi);
        Assert.False(item.CanOpenDetail);
        Assert.Equal(ResourceLookup.Resolve("plugins.generic.lifecycle"), item.LifecycleNote);
        Assert.DoesNotContain(Render(viewModel).GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == ResourceLookup.Resolve(DisclosureKey));
    }


    [Fact]
    public void 切换语言后已打开详情的宿主警示立即刷新()
    {
        using var locale = new TestLocale("zh-CN");
        using var gateway = new GatewayProcessService();
        using var viewModel = new PluginsViewModel(gateway, uiDispatcher: action => action());
        var item = CreateItem(viewModel);
        viewModel.Plugins.Add(item);
        item.OpenDetailCommand.Execute(null);
        var view = Render(viewModel);
        var changes = new List<string?>();
        item.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        LocaleService.SetCulture("en-US");

        Assert.Contains(nameof(PluginItemViewModel.SourceTrustTitle), changes);
        Assert.Contains(nameof(PluginItemViewModel.SourceTrustDescription), changes);
        Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == ResourceLookup.Resolve(DisclosureKey));
        Assert.Same(item, viewModel.SelectedPlugin);
    }

    public void Dispose()
    {
        foreach (var package in packages)
        {
            try { package.Dispose(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 只处理本测试创建的独立临时目录；collectible ALC 仍持有文件时留给系统清理。
            }
        }
    }

    private sealed class TestLocale : IDisposable
    {
        private readonly string original = LocaleService.CurrentCulture.Name;
        public TestLocale(string culture) => LocaleService.SetCulture(culture);
        public void Dispose() => LocaleService.SetCulture(original);
    }

    private static PluginItemViewModel CreateItem(PluginsViewModel viewModel, LoadedPluginInfo? info = null, bool source = true)
    {
        info ??= new LoadedPluginInfo("demo", "1", [], 0, false);
        info = info with { RuntimeKind = source ? "csharp-source" : "assembly" };
        var toggle = typeof(PluginsViewModel).GetMethod("SetPluginEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return new PluginItemViewModel(info, [], [],
            (item, enabled) => toggle.Invoke(viewModel, [item, enabled]),
            viewModel.OpenPluginDetail, ResourceLookup.Resolve,
            displayInfo: new LoomX.Plugins.PluginDisplayInfo("sk-source-display-secret", "", "", ""));
    }

    private static PluginsView Render(PluginsViewModel viewModel)
    {
        AvaloniaTestBootstrap.Ensure();
        var view = new PluginsView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Measure(new Size(1000, 900));
        window.Arrange(new Rect(0, 0, 1000, 900));
        window.UpdateLayout();
        return view;
    }
}
