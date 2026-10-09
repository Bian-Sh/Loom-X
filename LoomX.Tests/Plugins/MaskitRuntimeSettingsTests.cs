using LoomX.Maskit;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 端到端验证 maskit 设置页是「动态加载」的：宿主从输出 plugins 目录发现插件、
/// 按 Manifest 声明取快照，命令回传也走 Runtime，不内置任何 maskit 页面。
/// </summary>
public sealed class MaskitRuntimeSettingsTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-maskit-runtime-" + Guid.NewGuid().ToString("N"));

    private string PluginDirectory => Path.Combine(rootDirectory, "plugins");

    private string DataDirectory => Path.Combine(rootDirectory, "data");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // collectible ALC 可能仍等待 GC，临时目录交由系统后续清理。
        }
    }

    [Fact]
    public async Task RuntimeLoadsMaskitAndAppliesSettingsCommands()
    {
        StageMaskitPlugin();
        var runtime = StartRuntime();

        var info = Assert.Single(runtime.PluginInfos);
        Assert.Equal(MaskitPlugin.PluginId, info.Id);
        Assert.True(info.HasCardUi);
        Assert.True(info.HasDetailUi);
        Assert.Contains("settings", info.InteractiveUiContributions ?? []);

        var card = Assert.Single(runtime.GetUiContributions(
            MaskitPlugin.PluginId, PluginUiSlot.CardBody, "zh-CN"));
        Assert.Equal("overview", card.Id);

        var settings = Assert.Single(runtime.GetUiContributions(
            MaskitPlugin.PluginId, PluginUiSlot.DetailBody, "zh-CN"));
        Assert.Equal("settings", settings.Id);

        var added = await runtime.ExecuteUiCommandAsync(
            MaskitPlugin.PluginId,
            PluginUiCommand.Create(
                MaskitSettingsUi.SettingsContributionId,
                MaskitSettingsUi.AddRule,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MaskitSettingsUi.NewCategoryField] = "人名",
                    [MaskitSettingsUi.NewValueField] = "张三",
                    [MaskitSettingsUi.NewKindField] = "字面词",
                    [MaskitSettingsUi.NewWholeField] = "true",
                }));
        Assert.True(added.Handled);

        // 命令生效后，宿主重新拉取的快照里应当出现这条规则
        var refreshed = Assert.Single(runtime.GetUiContributions(
            MaskitPlugin.PluginId, PluginUiSlot.DetailBody, "zh-CN"));
        var nodes = Flatten(refreshed.Root).ToList();
        Assert.Contains(nodes, node => node is PluginUiTextNode text && text.Text == "人名");
        Assert.Contains(nodes, node => node is PluginUiButtonNode button
            && button.CommandId == MaskitSettingsUi.DeleteRule);

        // 未声明 interactive 的 overview 不接受命令
        var rejected = await runtime.ExecuteUiCommandAsync(
            MaskitPlugin.PluginId,
            PluginUiCommand.Create("overview", MaskitSettingsUi.AddRule));
        Assert.False(rejected.Handled);

        // 未知插件 / 空命令标识都不影响其它插件
        Assert.False((await runtime.ExecuteUiCommandAsync(
            "loomx.none", PluginUiCommand.Create("settings", MaskitSettingsUi.AddRule))).Handled);

        Assert.Empty(runtime.Diagnostics);
        Assert.NotNull(runtime.GetPipeline("request"));
        Assert.NotNull(runtime.GetPipeline("response"));
    }

    [Fact]
    public async Task RequestPipelineMasksPayloadUsingConfiguredRules()
    {
        StageMaskitPlugin();
        var runtime = StartRuntime();

        await runtime.ExecuteUiCommandAsync(
            MaskitPlugin.PluginId,
            PluginUiCommand.Create(
                MaskitSettingsUi.SettingsContributionId,
                MaskitSettingsUi.AddRule,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MaskitSettingsUi.NewCategoryField] = "人名",
                    [MaskitSettingsUi.NewValueField] = "张三",
                    [MaskitSettingsUi.NewKindField] = "字面词",
                    [MaskitSettingsUi.NewWholeField] = "true",
                }));

        var result = await runtime.GetPipeline("request")!.ExecuteAsync("联系人是张三");

        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        Assert.DoesNotContain("张三", result.Payload, StringComparison.Ordinal);
        Assert.Contains("<MASKIT:", result.Payload, StringComparison.Ordinal);
    }

    private void StageMaskitPlugin()
    {
        var directory = Path.Combine(PluginDirectory, MaskitPlugin.PluginId);
        Directory.CreateDirectory(directory);

        var sourceAssembly = typeof(MaskitPlugin).Assembly.Location;
        File.Copy(sourceAssembly, Path.Combine(directory, Path.GetFileName(sourceAssembly)), overwrite: true);
        var depsSource = Path.ChangeExtension(sourceAssembly, ".deps.json");
        if (File.Exists(depsSource))
            File.Copy(depsSource, Path.Combine(directory, Path.GetFileName(depsSource)), overwrite: true);

        // 使用插件仓库中的真实 Manifest，连同 interactive 声明一起验证解析链路
        var manifestSource = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "plugins", "LoomX.Maskit", "plugin.manifest.json");
        Assert.True(File.Exists(manifestSource), $"找不到 maskit Manifest：{manifestSource}");
        File.Copy(manifestSource, Path.Combine(directory, ManifestParser.ManifestFileName), overwrite: true);
    }

    private PluginRuntime StartRuntime() => PluginRuntime.Start(
        new PluginRuntimeOptions
        {
            PluginDirectory = PluginDirectory,
            DataRootDirectory = DataDirectory,
        },
        NullLoggerFactory.Instance);

    private static IEnumerable<PluginUiNode> Flatten(PluginUiNode node)
    {
        yield return node;
        switch (node)
        {
            case PluginUiStackNode stack:
                foreach (var child in stack.Children)
                foreach (var flattened in Flatten(child))
                    yield return flattened;
                break;
            case PluginUiGridNode grid:
                foreach (var item in grid.Children)
                foreach (var flattened in Flatten(item.Content))
                    yield return flattened;
                break;
            case PluginUiSurfaceNode surface:
                foreach (var flattened in Flatten(surface.Content))
                    yield return flattened;
                break;
        }
    }
}
