using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 凭据保护设置页交互的回归防线：
/// 命令参数必须按需收集（否则页面字段一多就全量超限、增删规则静默失效）、
/// 自定义规则区不得混入内置基线（否则整屏都是打码后的 ******）。
/// </summary>
public sealed class CredentialProtectionSettingsUiRegressionTests : IDisposable
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-credential-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
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
        plugin.Initialize(new PluginInitializationContext("loomx.credential-protection", dataDirectory));
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
    public void BaselineSection_UsesReadableLabelsInsteadOfRawRegexOrMasks()
    {
        var text = FlattenText(Settings(CreatePlugin()));

        // 基线区块有自己的标题，不再复用「自定义规则」。
        Assert.Contains("内置识别底座", text, StringComparison.Ordinal);

        // 由规则 id 派生的可读标签：既不是原始正则，也不是打码星号。
        Assert.Contains("Openai Sk", text, StringComparison.Ordinal);
        Assert.Contains("Github Token", text, StringComparison.Ordinal);
        Assert.Contains("Http Headers", text, StringComparison.Ordinal);

        // 整页不得出现纯星号占位。
        Assert.DoesNotContain("********", text, StringComparison.Ordinal);
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
    public void BuiltinCards_LeadWithUnlabelledToggle_AndUseWeightedColumns()
    {
        var builtinSection = FindSection(Settings(CreatePlugin()), "内置形态规则");
        var card = Collect(builtinSection, node => node is PluginUiSurfaceNode)
            .OfType<PluginUiSurfaceNode>()
            .First(surface => surface.Content is PluginUiGridNode
                && Collect(surface, n => n is PluginUiToggleFieldNode).Any());

        var grid = (PluginUiGridNode)card.Content;
        var toggle = Assert.IsType<PluginUiToggleFieldNode>(grid.Children[0].Content);

        Assert.Equal(string.Empty, toggle.Label);
        Assert.NotNull(grid.ColumnWeights);
        // 第 0 列按内容自适应（开关不被拉伸），后两列按权重瓜分剩余宽度。
        Assert.Equal(0d, grid.ColumnWeights![0]);
        Assert.All(grid.ColumnWeights.Skip(1), weight => Assert.True(weight > 0));
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