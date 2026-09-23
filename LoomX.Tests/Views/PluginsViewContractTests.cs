using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class PluginsViewContractTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");

    [Fact]
    public void PluginPageIsRegisteredAsAFirstClassNavigationPage()
    {
        var mainViewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "MainWindowViewModel.cs"));
        var app = File.ReadAllText(Path.Combine(Root, "LoomX", "App.axaml"));
        var resources = File.ReadAllText(Path.Combine(Root, "LoomX", "Resources", "Strings.resx"));

        Assert.Contains("new(\"nav.plugins\"", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("ShowView(\"nav.plugins\", pluginsViewModel)", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type vm:PluginsViewModel}\"><views:PluginsView", app, StringComparison.Ordinal);
        Assert.Contains("name=\"nav.plugins\"", resources, StringComparison.Ordinal);
        Assert.Contains("name=\"nav.plugins.description\"", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialProtectionCardExplainsCompatibilityLifecycleAndCannotBeToggled()
    {
        var viewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "PluginsViewModel.cs"));
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));
        var resources = File.ReadAllText(Path.Combine(Root, "LoomX", "Resources", "Strings.resx"));

        Assert.Contains("CredentialProtectionPluginId = \"loomx.credential-protection\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("public bool CanToggle => !IsCredentialProtection", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanToggle}\"", view, StringComparison.Ordinal);
        Assert.Contains("历史会话中的 token 仍依赖兼容解析能力", resources, StringComparison.Ordinal);
        Assert.Contains("保留 Vault", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginPageReadsRuntimeSummariesWithoutExposingSensitivePayloads()
    {
        var viewModel = File.ReadAllText(Path.Combine(Root, "LoomX", "ViewModels", "PluginsViewModel.cs"));
        var view = File.ReadAllText(Path.Combine(Root, "LoomX", "Views", "PluginsView.axaml"));

        Assert.Contains("gatewayService.GetHostedService<PluginRuntime>()", viewModel, StringComparison.Ordinal);
        Assert.Contains("pluginRuntime.PluginInfos", viewModel, StringComparison.Ordinal);
        Assert.Contains("pluginRuntime.Diagnostics", viewModel, StringComparison.Ordinal);
        Assert.Contains("public bool HasLoadError", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!runtime.SetPluginEnabled(item.Id, enabled))", viewModel, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CapabilitySummary}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", viewModel, StringComparison.Ordinal);
    }
}
