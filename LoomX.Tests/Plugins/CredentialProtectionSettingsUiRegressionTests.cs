using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 凭据保护设置页交互的回归防线：
/// 命令参数必须按需收集（否则页面字段一多就全量超限、增删规则静默失效）、
/// 自定义规则区不得混入内置基线（否则整屏都是与用户配置无关的内置规则）。
/// </summary>
public sealed class CredentialProtectionSettingsUiRegressionTests : IDisposable
{
    private readonly CredentialTestFactory factory = new();
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-credential-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果。
        }
    }

    private CredentialProtectionPlugin CreatePlugin()
    {
        Directory.CreateDirectory(dataDirectory);
        var plugin = new CredentialProtectionPlugin();
        plugin.Initialize(factory.CreateContext(dataDirectory));
        plugin.CreateExtensions().ToArray();
        return plugin;
    }

    private static PluginUiNode Settings(CredentialProtectionPlugin plugin) =>
        plugin.GetUiContributions(new PluginUiContext("zh-CN"))
            .Single(item => item.Slot == PluginUiSlot.DetailBody)
            .Root;

    /// <summary>模拟宿主校验器的参数上限；超限即等价于 Runtime 拒绝该命令。</summary>
    private const int RuntimeArgumentLimit = 32;

    /// <summary>
    /// 整页表单的字段总数：旧实现把它全量并入每条命令，这就是「参数过多」的根因。
    /// 这里显式建模，断言任一命令实际携带的参数都远小于该总数。
    /// </summary>
    private static int CountFormFields(PluginUiNode node) =>
        Collect(node, candidate => candidate is PluginUiTextFieldNode
                or PluginUiToggleFieldNode
                or PluginUiChoiceFieldNode)
            .Count();

    [Fact]
    public void SettingsPage_HasFarMoreFieldsThanTheRuntimeArgumentLimit()
    {
        var plugin = CreatePlugin();
        for (var index = 0; index < 12; index++)
            plugin.RuleStore.AddRule(SensitiveRuleKind.Name, $"custom_field_{index}", out _, out _);

        // 前置事实：页面字段数确实超过上限，因此「全量并入」的实现必然被拒绝。
        Assert.True(CountFormFields(Settings(plugin)) > RuntimeArgumentLimit);
    }

    [Fact]
    public void EveryInteractiveCommand_StaysWithinRuntimeArgumentLimit()
    {
        var plugin = CreatePlugin();
        // 制造一个"重页面"：多组用户规则 + 内置基线，远超参数上限。
        for (var index = 0; index < 12; index++)
            plugin.RuleStore.AddRule(SensitiveRuleKind.Name, $"custom_field_{index}", out _, out _);

        var offenders = new List<string>();
        foreach (var node in Walk(Settings(plugin)))
        {
            var total = EstimateArgumentCount(node);
            if (total > RuntimeArgumentLimit) offenders.Add($"{Describe(node)} → {total}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void InteractiveCommands_CollectOnlyDeclaredFields_SoAddRuleReadsItsOwnInputs()
    {
        var node = FindButton(Settings(CreatePlugin()), CredentialSettingsUi.AddRule);

        // 新增规则必须显式读取规则值与形态两个字段，否则命令拿不到用户输入。
        Assert.NotNull(node.ReadsFields);
        Assert.Contains(CredentialSettingsUi.NewValueField, node.ReadsFields);
        Assert.Contains(CredentialSettingsUi.NewKindField, node.ReadsFields);
    }

    [Fact]
    public void DeleteRuleButton_DeclaresNoExtraFormFields()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddRule(SensitiveRuleKind.Name, "vault_path", out _, out _);
        var rule = plugin.RuleStore.Rules.First(item => !item.Builtin);

        var node = FindButton(Settings(plugin), CredentialSettingsUi.DeleteRule);

        // 删除只需要 rule_id；带上整页字段就是参数超限的根因。
        Assert.Null(node.ReadsFields);
        Assert.Equal(rule.Id, node.Arguments?["rule_id"]);
    }

    [Fact]
    public void CustomRuleSection_ListsOnlyUserRules_AndHidesBuiltinBaseline()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddRule(SensitiveRuleKind.Name, "vault_path", out _, out _);

        var customSection = FindSection(Settings(plugin), "自定义规则 · ");
        var toggles = Collect(customSection, node => node is PluginUiToggleFieldNode)
            .OfType<PluginUiToggleFieldNode>()
            .ToArray();

        // 只有 1 条用户规则；18 条内置基线不得混进这一段。
        Assert.Single(toggles);
        Assert.Equal(
            CredentialSettingsUi.EnabledField(
                plugin.RuleStore.Rules.First(item => !item.Builtin).Id),
            toggles[0].FieldId);
    }

    [Fact]
    public void CustomRuleRows_LeadWithUnlabelledToggle()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddRule(SensitiveRuleKind.Name, "vault_path", out _, out _);

        var customSection = FindSection(Settings(plugin), "自定义规则 · ");
        var stack = Assert.IsType<PluginUiStackNode>(customSection.Content);
        var row = Assert.IsType<PluginUiGridNode>(stack.Children[1]);
        var toggle = Assert.IsType<PluginUiToggleFieldNode>(row.Children[0].Content);

        // 开关在最前，且不携带「启用」标签文字。
        Assert.Equal(string.Empty, toggle.Label);
        Assert.Equal(PluginUiAlignment.Start, row.Children[0].HorizontalAlignment);
    }

    [Fact]
    public void SettingsSections_PlaceProtectionFirst_AndKeepAddSectionNextToCustomRules()
    {
        var root = Assert.IsType<PluginUiStackNode>(Settings(CreatePlugin()));
        var sectionTitles = root.Children
            .Cast<PluginUiSurfaceNode>()
            .Select(FlattenText)
            .Select(text => text.Split('\n')[0])
            .ToArray();

        Assert.Equal("主动保护状态", sectionTitles[0]);
        Assert.Equal("新增自定义规则", sectionTitles[1]);
        Assert.Equal("自定义规则", sectionTitles[2]);
    }

    [Fact]
    public void CustomRuleRows_CenterRuleValue_AndDoNotRenderKindTag()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddRule(SensitiveRuleKind.Name, "vault_path", out _, out _);

        var customSection = FindSection(Settings(plugin), "自定义规则 · ");
        var stack = Assert.IsType<PluginUiStackNode>(customSection.Content);
        var row = Assert.IsType<PluginUiGridNode>(stack.Children[1]);
        var value = Assert.IsType<PluginUiTextNode>(row.Children[1].Content);

        Assert.Equal(4, row.Columns);
        Assert.Equal(PluginUiAlignment.Center, row.Children[1].VerticalAlignment);
        Assert.Equal("vault_path", value.Text);
        Assert.DoesNotContain("名称", FlattenText(row), StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltinSection_GroupsRulesWithCounts_AndOffersBulkToggles()
    {
        var plugin = CreatePlugin();
        var text = FlattenText(Settings(plugin));

        // 四个场景分组与启用计数徽章。
        Assert.Contains("核心隐私与通用密钥（推荐默认）（7/7）", text, StringComparison.Ordinal);
        Assert.Contains("扩展凭据与敏感形态（可选）（5/5）", text, StringComparison.Ordinal);
        Assert.Contains("网络与设备标识（可选，易影响模型配置推理）（1/5）", text, StringComparison.Ordinal);
        Assert.Contains("扩展证照与特定格式（可选）（2/4）", text, StringComparison.Ordinal);

        Assert.NotNull(FindButton(Settings(plugin), CredentialSettingsUi.EnableAllBuiltins));
        Assert.NotNull(FindButton(Settings(plugin), CredentialSettingsUi.DisableAllBuiltins));
    }

    [Fact]
    public void BaselineSection_UsesReadableLabelsInsteadOfRawRegex()
    {
        var text = FlattenText(Settings(CreatePlugin()));

        // 基线区块有自己的标题，不再复用「自定义规则」。
        Assert.Contains("内置识别底座", text, StringComparison.Ordinal);

        // 由规则 id 派生的可读标签，而不是铺开原始正则。
        Assert.Contains("Openai Sk", text, StringComparison.Ordinal);
        Assert.Contains("Github Token", text, StringComparison.Ordinal);
        Assert.Contains("Http Headers", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomRuleRows_ShowRuleValueVerbatim()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddRule(SensitiveRuleKind.Name, "vault_path", out _, out _);
        plugin.RuleStore.AddRule(
            SensitiveRuleKind.Pattern, @"(?<![a-z0-9])x-vault-[0-9]{8}(?![a-z0-9])", out _, out _);

        var section = FindSection(Settings(plugin), "自定义规则 · ");
        var text = FlattenText(section);

        // 用户要能一眼确认自己写的是哪条规则，因此名称与正则都原样展示。
        Assert.Contains("vault_path", text, StringComparison.Ordinal);
        Assert.Contains(@"(?<![a-z0-9])x-vault-[0-9]{8}(?![a-z0-9])", text, StringComparison.Ordinal);

        // 展示层不再产生掩码占位：连续星号即打码残留。
        Assert.DoesNotMatch(@"\*{2,}", text);
    }

    [Fact]
    public async Task BulkToggle_UpdatesEveryBuiltinRule()
    {
        var plugin = CreatePlugin();

        await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            CredentialSettingsUi.SettingsContributionId,
            CredentialSettingsUi.DisableAllBuiltins));
        Assert.All(plugin.RuleStore.BuiltinRules.Values, Assert.False);

        await plugin.ExecuteUiCommandAsync(PluginUiCommand.Create(
            CredentialSettingsUi.SettingsContributionId,
            CredentialSettingsUi.EnableAllBuiltins));
        Assert.All(plugin.RuleStore.BuiltinRules.Values, Assert.True);
    }

    [Fact]
    public void BuiltinCards_StackTitleAndHintBesideUnlabelledToggle()
    {
        var builtinSection = FindSection(Settings(CreatePlugin()), "内置形态规则");
        var card = Collect(builtinSection, node => node is PluginUiSurfaceNode)
            .OfType<PluginUiSurfaceNode>()
            .First(surface => surface.Content is PluginUiGridNode
                && Collect(surface, n => n is PluginUiToggleFieldNode).Any());

        var grid = (PluginUiGridNode)card.Content;
        var toggle = Assert.IsType<PluginUiToggleFieldNode>(grid.Children[0].Content);
        var content = Assert.IsType<PluginUiStackNode>(grid.Children[1].Content);

        Assert.Equal(string.Empty, toggle.Label);
        Assert.Equal(2, grid.Columns);
        Assert.Equal([0d, 1d], grid.ColumnWeights);
        Assert.Equal(PluginUiOrientation.Vertical, content.Orientation);
        Assert.Equal(PluginUiTextRole.Body, Assert.IsType<PluginUiTextNode>(content.Children[0]).Role);
        Assert.Equal(PluginUiTextRole.Caption, Assert.IsType<PluginUiTextNode>(content.Children[1]).Role);
    }

    [Fact]
    public void BaselineCards_UseTheSameTitleAndHintLayoutAsBuiltinCards()
    {
        var builtinSection = FindSection(Settings(CreatePlugin()), "内置形态规则");
        var card = Collect(builtinSection, node => node is PluginUiSurfaceNode)
            .OfType<PluginUiSurfaceNode>()
            .First(surface => surface.Content is PluginUiGridNode
                && Collect(surface, n => n is PluginUiToggleFieldNode toggle
                    && toggle.FieldId.StartsWith("enabled_name.", StringComparison.Ordinal)).Any());

        var grid = Assert.IsType<PluginUiGridNode>(card.Content);
        var content = Assert.IsType<PluginUiStackNode>(grid.Children[1].Content);

        Assert.Equal(2, grid.Columns);
        Assert.Equal([0d, 1d], grid.ColumnWeights);
        Assert.Equal(PluginUiTextRole.Body, Assert.IsType<PluginUiTextNode>(content.Children[0]).Role);
        Assert.Equal(PluginUiTextRole.Caption, Assert.IsType<PluginUiTextNode>(content.Children[1]).Role);
    }

    [Fact]
    public void PrefixSection_PlacesAddFormBeforeContentSizedCenteredChips()
    {
        var plugin = CreatePlugin();
        Assert.True(plugin.RuleStore.AddPrefix("sk-live-", out _));

        var section = FindSection(Settings(plugin), "自定义密钥前缀");
        var stack = Assert.IsType<PluginUiStackNode>(section.Content);
        var addFormIndex = stack.Children
            .Select((node, index) => (node, index))
            .Single(item => item.node is PluginUiGridNode grid
                && Collect(grid, child => child is PluginUiTextFieldNode field
                    && field.FieldId == CredentialSettingsUi.NewPrefixField).Any())
            .index;
        var chipsIndex = stack.Children
            .Select((node, index) => (node, index))
            .Single(item => item.node is PluginUiGridNode grid
                && Collect(grid, child => child is PluginUiButtonNode button
                    && button.CommandId == CredentialSettingsUi.DeletePrefix).Any())
            .index;
        var addForm = Assert.IsType<PluginUiGridNode>(stack.Children[addFormIndex]);
        var chips = Assert.IsType<PluginUiGridNode>(stack.Children[chipsIndex]);
        var chip = Assert.IsType<PluginUiSurfaceNode>(chips.Children[0].Content);
        var chipContent = Assert.IsType<PluginUiGridNode>(chip.Content);
        var prefix = Assert.IsType<PluginUiTextNode>(chipContent.Children[0].Content);
        var delete = Assert.IsType<PluginUiButtonNode>(chipContent.Children[1].Content);

        Assert.True(addFormIndex < chipsIndex);
        Assert.Equal(PluginUiAlignment.End, addForm.Children[1].VerticalAlignment);
        Assert.Equal([0d, 0d, 0d], chips.ColumnWeights);
        Assert.All(chips.Children, item => Assert.Equal(PluginUiAlignment.Start, item.HorizontalAlignment));
        Assert.Equal(PluginUiAlignment.Center, chipContent.Children[0].VerticalAlignment);
        Assert.Equal("sk-live-", prefix.Text);
        Assert.Equal("删除", delete.Label);
        Assert.Equal("删除", delete.ToolTip);
        Assert.True(delete.IsIconOnly);
        Assert.Equal(
            "M1.9,2.5 L2.5,1.9 L6,5.4 L9.5,1.9 L10.1,2.5 L6.6,6 " +
            "L10.1,9.5 L9.5,10.1 L6,6.6 L2.5,10.1 L1.9,9.5 L5.4,6 Z",
            delete.IconData);
    }

    /// <summary>
    /// 等价于宿主 <c>PluginUiPresenter.CollectArguments</c>：
    /// 命令自带参数 + 触发节点自身字段 + 按钮显式声明的 ReadsFields。
    /// 关键是没有任何一项来自"整页表单全量"。
    /// </summary>
    private static int EstimateArgumentCount(PluginUiNode node)
    {
        switch (node)
        {
            case PluginUiButtonNode button:
                return (button.Arguments?.Count ?? 0) + (button.ReadsFields?.Count ?? 0);
            case PluginUiToggleFieldNode toggle:
                // 开关自身字段 + 命令自带参数。
                return (toggle.Arguments?.Count ?? 0) + 1;
            case PluginUiChoiceFieldNode choice:
                return (choice.Arguments?.Count ?? 0) + 1;
            default:
                return 0;
        }
    }

    /// <summary>设置页顶层按顺序排列的各个 Surface 区块；用标题前缀定位其中一个。</summary>
    private static PluginUiSurfaceNode FindSection(PluginUiNode node, string titlePrefix)
    {
        var surfaces = Collect(node, candidate => candidate is PluginUiSurfaceNode)
            .OfType<PluginUiSurfaceNode>()
            .ToArray();
        return surfaces.First(surface => FlattenText(surface)
            .Split('\n')
            .Any(line => line.StartsWith(titlePrefix, StringComparison.Ordinal)));
    }

    private static PluginUiButtonNode FindButton(PluginUiNode node, string commandId) =>
        Collect(node, candidate => candidate is PluginUiButtonNode button && button.CommandId == commandId)
            .Cast<PluginUiButtonNode>()
            .First();

    private static IEnumerable<PluginUiNode> Walk(PluginUiNode node)
    {
        yield return node;
        foreach (var child in Children(node))
        {
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }

    private static IEnumerable<PluginUiNode> Collect(PluginUiNode node, Func<PluginUiNode, bool> predicate) =>
        Walk(node).Where(predicate);

    private static IEnumerable<PluginUiNode> Children(PluginUiNode node) => node switch
    {
        PluginUiStackNode stack => stack.Children,
        PluginUiGridNode grid => grid.Children.Select(item => item.Content),
        PluginUiSurfaceNode surface => [surface.Content],
        _ => [],
    };

    private static string Describe(PluginUiNode node) => node switch
    {
        PluginUiButtonNode button => $"button:{button.CommandId}",
        PluginUiToggleFieldNode toggle => $"toggle:{toggle.FieldId}",
        PluginUiChoiceFieldNode choice => $"choice:{choice.FieldId}",
        _ => node.GetType().Name,
    };

    private static string FlattenText(PluginUiNode node) =>
        string.Join("\n", Walk(node).OfType<PluginUiTextNode>().Select(item => item.Text));
}
