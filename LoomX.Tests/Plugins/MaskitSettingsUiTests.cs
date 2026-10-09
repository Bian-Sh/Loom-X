using LoomX.Maskit;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>maskit 设置页：声明式 UI 快照 + 命令回传驱动的规则增删改查。</summary>
public sealed class MaskitSettingsUiTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(), "loomx-tests", "maskit-ui", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
            Directory.Delete(dataDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PluginContributesLocalizedCardAndInteractiveSettings()
    {
        var plugin = CreatePlugin(out var invalidated);

        var contributions = plugin.GetUiContributions(new PluginUiContext("zh-CN"));

        var card = Assert.Single(contributions, item => item.Slot == PluginUiSlot.CardBody);
        Assert.Equal("overview", card.Id);
        Assert.Equal(PluginUiContribution.CurrentSchemaVersion, card.SchemaVersion);

        var settings = Assert.Single(contributions, item => item.Slot == PluginUiSlot.DetailBody);
        Assert.Equal("settings", settings.Id);

        var nodes = Flatten(settings.Root).ToList();
        Assert.Contains(nodes, node => node is PluginUiTextNode text && text.Text.Contains("脱敏规则设置", StringComparison.Ordinal));
        // 交互节点：新增表单 + 添加按钮
        Assert.Contains(nodes, node => node is PluginUiTextFieldNode field && field.FieldId == MaskitSettingsUi.NewCategoryField);
        Assert.Contains(nodes, node => node is PluginUiToggleFieldNode toggle && toggle.FieldId == MaskitSettingsUi.NewWholeField);
        Assert.Contains(nodes, node => node is PluginUiButtonNode button && button.CommandId == MaskitSettingsUi.AddRule);
        Assert.Contains(nodes, node => node is PluginUiChoiceFieldNode);
        // 内置规则可开关、不可删除
        Assert.Contains(nodes, node => node is PluginUiToggleFieldNode toggle
            && toggle.CommandId == MaskitSettingsUi.ToggleBuiltin
            && toggle.Arguments is not null && toggle.Arguments.ContainsKey("key"));
        Assert.DoesNotContain(nodes, node => node is PluginUiButtonNode button
            && button.CommandId == MaskitSettingsUi.DeleteRule);

        // 英文快照跟随宿主文化
        var english = plugin.GetUiContributions(new PluginUiContext("en-US"));
        var englishSettings = Assert.Single(english, item => item.Slot == PluginUiSlot.DetailBody);
        Assert.Contains(
            Flatten(englishSettings.Root).OfType<PluginUiTextNode>(),
            node => node.Text.Contains("Masking rules", StringComparison.Ordinal));

        Assert.Empty(invalidated);
    }

    [Fact]
    public async Task AddEditDeleteRuleThroughCommandRoundTrip()
    {
        var plugin = CreatePlugin(out var invalidated);

        // 增
        var added = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
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
        Assert.False(string.IsNullOrEmpty(added.Message));
        var rule = Assert.Single(plugin.RuleStore.Rules);
        Assert.NotEmpty(invalidated);

        // 列表出现该规则（值为凭据类以外的类别时按打码展示）
        var nodes = Flatten(SettingsRoot(plugin)).ToList();
        Assert.Contains(nodes, node => node is PluginUiTextNode text && text.Text == "人名");
        Assert.Contains(nodes, node => node is PluginUiToggleFieldNode toggle
            && toggle.FieldId == MaskitSettingsUi.EnabledField(rule.Id));

        // 改：进入编辑态 → 保存
        invalidated.Clear();
        await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.BeginEdit,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id }));

        var editing = Flatten(SettingsRoot(plugin)).ToList();
        Assert.Contains(editing, node => node is PluginUiTextFieldNode field
            && field.FieldId == MaskitSettingsUi.EditValueField(rule.Id)
            && field.Value == "张三");

        var saved = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.SaveEdit,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rule_id"] = rule.Id,
                [MaskitSettingsUi.EditCategoryField(rule.Id)] = "内部代号",
                [MaskitSettingsUi.EditValueField(rule.Id)] = "Project-X",
                [MaskitSettingsUi.EditWholeField(rule.Id)] = "false",
            }));
        Assert.True(saved.Handled);
        var updated = Assert.Single(plugin.RuleStore.Rules);
        Assert.Equal("内部代号", updated.Category);
        Assert.Equal("Project-X", updated.Value);
        Assert.False(updated.WholeWord);

        // 启停切换
        await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.ToggleRule,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rule_id"] = rule.Id,
                [MaskitSettingsUi.EnabledField(rule.Id)] = "false",
            }));
        Assert.False(Assert.Single(plugin.RuleStore.Rules).Enabled);

        // 删
        var deleted = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.DeleteRule,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["rule_id"] = rule.Id }));
        Assert.True(deleted.Handled);
        Assert.Empty(plugin.RuleStore.Rules);
    }

    [Fact]
    public async Task DuplicateAndInvalidAddAreReportedWithoutCreatingRules()
    {
        var plugin = CreatePlugin(out _);

        var first = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.AddRule,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MaskitSettingsUi.NewCategoryField] = "人名",
                [MaskitSettingsUi.NewValueField] = "张三",
                [MaskitSettingsUi.NewKindField] = "字面词",
            }));
        Assert.True(first.Handled);

        var duplicate = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.AddRule,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MaskitSettingsUi.NewCategoryField] = "人名",
                [MaskitSettingsUi.NewValueField] = "张三",
                [MaskitSettingsUi.NewKindField] = "字面词",
            }));
        Assert.True(duplicate.Handled);
        Assert.Equal(PluginUiTone.Danger, duplicate.Tone);
        Assert.Single(plugin.RuleStore.Rules);

        var empty = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.AddRule,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MaskitSettingsUi.NewCategoryField] = "人名",
                [MaskitSettingsUi.NewValueField] = "   ",
            }));
        Assert.Equal(PluginUiTone.Danger, empty.Tone);
        Assert.Single(plugin.RuleStore.Rules);
    }

    [Fact]
    public async Task BuiltinToggleAndPrefixCommandsUpdateStore()
    {
        var plugin = CreatePlugin(out _);

        var toggled = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.ToggleBuiltin,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = "IP_PUBLIC",
                ["builtin_IP_PUBLIC"] = "true",
            }));
        Assert.True(toggled.Handled);
        Assert.True(plugin.RuleStore.BuiltinRules["IP_PUBLIC"]);

        var prefixAdded = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.AddPrefix,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MaskitSettingsUi.NewPrefixField] = "sk-",
            }));
        Assert.True(prefixAdded.Handled);
        Assert.Equal("sk-", Assert.Single(plugin.RuleStore.SecretPrefixes));

        await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId,
            MaskitSettingsUi.DeletePrefix,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["prefix"] = "sk-" }));
        Assert.Empty(plugin.RuleStore.SecretPrefixes);
    }

    [Fact]
    public async Task UnknownCommandIsRejectedWithoutChangingRules()
    {
        var plugin = CreatePlugin(out _);
        var result = await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            MaskitSettingsUi.SettingsContributionId, "drop_everything"));

        Assert.True(result.Handled);
        Assert.Equal(PluginUiTone.Danger, result.Tone);
        Assert.Empty(plugin.RuleStore.Rules);
    }

    [Fact]
    public void SettingsTreeStaysWithinHostValidationLimits()
    {
        var plugin = CreatePlugin(out _);
        for (var index = 0; index < 40; index++)
        {
            plugin.RuleStore.AddRule(
                $"类别{index}", $"词{index}", MaskitRuleKind.Word, true, out _, out _);
        }

        var nodes = Flatten(SettingsRoot(plugin)).ToList();
        // 列表分页：只渲染前 24 条并给出总数提示，节点数必须落在宿主校验上限内
        Assert.True(nodes.Count <= 512, $"节点数 {nodes.Count} 超出宿主上限");
        Assert.Contains(
            nodes.OfType<PluginUiTextNode>(),
            node => node.Text.Contains("40", StringComparison.Ordinal));
    }

    private MaskitPlugin CreatePlugin(out List<EventArgs> invalidated)
    {
        var plugin = new MaskitPlugin();
        plugin.Initialize(new PluginInitializationContext(MaskitPlugin.PluginId, dataDirectory));
        var captured = new List<EventArgs>();
        invalidated = captured;
        plugin.UiInvalidated += (_, args) => captured.Add(args);
        return plugin;
    }

    private static PluginUiNode SettingsRoot(MaskitPlugin plugin) =>
        Assert.Single(plugin.GetUiContributions(new PluginUiContext("zh-CN")),
            item => item.Slot == PluginUiSlot.DetailBody).Root;

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
