using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 凭据保护设置页：规则增删改查、内置形态开关、密钥前缀，以及停用利害关系声明。
/// </summary>
public sealed class CredentialProtectionSettingsTests : IDisposable
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-credential-settings-" + Guid.NewGuid().ToString("N"));

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

    private static async Task<PluginUiCommandResult> RunAsync(
        CredentialProtectionPlugin plugin,
        string commandId,
        params (string Key, string Value)[] arguments)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            map[key] = value;

        return await plugin.ExecuteUiCommandAsync(
            PluginUiCommand.Create(CredentialSettingsUi.SettingsContributionId, commandId, map));
    }

    [Fact]
    public void SettingsPage_OffersCardAndInteractiveDetailBody()
    {
        var plugin = CreatePlugin();
        var contributions = plugin.GetUiContributions(new PluginUiContext("zh-CN"));

        Assert.Equal(2, contributions.Count);
        var card = Assert.Single(contributions, item => item.Slot == PluginUiSlot.CardBody);
        Assert.Equal(CredentialSettingsUi.OverviewContributionId, card.Id);

        var detail = Assert.Single(contributions, item => item.Slot == PluginUiSlot.DetailBody);
        Assert.Equal(CredentialSettingsUi.SettingsContributionId, detail.Id);
    }

    [Fact]
    public async Task AddRule_PersistsToPluginOwnedStore()
    {
        var plugin = CreatePlugin();

        var result = await RunAsync(plugin, CredentialSettingsUi.AddRule,
            (CredentialSettingsUi.NewValueField, "vault_path"),
            (CredentialSettingsUi.NewKindField, "名称"));

        Assert.True(result.Handled);
        Assert.Contains(plugin.RuleStore.Rules, rule => rule.Value == "vault_path");
    }

    [Fact]
    public async Task AddRule_RejectsDuplicate()
    {
        var plugin = CreatePlugin();
        await RunAsync(plugin, CredentialSettingsUi.AddRule,
            (CredentialSettingsUi.NewValueField, "vault_path"),
            (CredentialSettingsUi.NewKindField, "名称"));

        var duplicate = await RunAsync(plugin, CredentialSettingsUi.AddRule,
            (CredentialSettingsUi.NewValueField, "vault_path"),
            (CredentialSettingsUi.NewKindField, "名称"));

        Assert.True(duplicate.Handled);
        Assert.Equal(PluginUiTone.Danger, duplicate.Tone);
    }

    [Fact]
    public async Task UpdateRule_ChangesValue()
    {
        var plugin = CreatePlugin();
        await RunAsync(plugin, CredentialSettingsUi.AddRule,
            (CredentialSettingsUi.NewValueField, "vault_path"),
            (CredentialSettingsUi.NewKindField, "名称"));
        var rule = plugin.RuleStore.Rules.First(item => item.Value == "vault_path");

        var result = await RunAsync(plugin, CredentialSettingsUi.SaveEdit,
            ("rule_id", rule.Id),
            (CredentialSettingsUi.EditValueField(rule.Id), "vault_location"));

        Assert.True(result.Handled);
        Assert.Contains(plugin.RuleStore.Rules, item => item.Value == "vault_location");
        Assert.DoesNotContain(plugin.RuleStore.Rules, item => item.Value == "vault_path");
    }

    [Fact]
    public async Task DeleteRule_RemovesFromStore()
    {
        var plugin = CreatePlugin();
        await RunAsync(plugin, CredentialSettingsUi.AddRule,
            (CredentialSettingsUi.NewValueField, "vault_path"),
            (CredentialSettingsUi.NewKindField, "名称"));
        var rule = plugin.RuleStore.Rules.First(item => item.Value == "vault_path");

        var result = await RunAsync(plugin, CredentialSettingsUi.DeleteRule, ("rule_id", rule.Id));

        Assert.True(result.Handled);
        Assert.DoesNotContain(plugin.RuleStore.Rules, item => item.Id == rule.Id);
    }

    [Fact]
    public async Task ToggleBuiltin_PersistsEnabledState()
    {
        var plugin = CreatePlugin();

        var result = await RunAsync(plugin, CredentialSettingsUi.ToggleBuiltin,
            ("key", "IDCARD"),
            ("builtin_IDCARD", "false"));

        Assert.True(result.Handled);
        Assert.False(plugin.RuleStore.BuiltinRules["IDCARD"]);
        Assert.DoesNotContain(plugin.RuleStore.EnabledBuiltinRules(), item => item.Key == "IDCARD");
    }

    [Fact]
    public async Task AddAndRemovePrefix_PersistsCustomPrefix()
    {
        var plugin = CreatePlugin();

        var added = await RunAsync(plugin, CredentialSettingsUi.AddPrefix,
            (CredentialSettingsUi.NewPrefixField, "x-secret-"));
        Assert.True(added.Handled);
        Assert.Contains("x-secret-", plugin.RuleStore.SecretPrefixes);

        var removed = await RunAsync(plugin, CredentialSettingsUi.DeletePrefix, ("prefix", "x-secret-"));
        Assert.True(removed.Handled);
        Assert.DoesNotContain("x-secret-", plugin.RuleStore.SecretPrefixes);
    }

    [Fact]
    public void CustomPrefix_MasksMatchingValue()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddPrefix("x-secret-", out _);
        var engine = plugin.Engine!;

        var sanitized = engine.Sanitize("token=x-secret-abcdef123456 rest", out var changed, out var count);

        Assert.True(changed);
        Assert.True(count > 0);
        Assert.DoesNotContain("x-secret-abcdef123456", sanitized, StringComparison.Ordinal);
        Assert.Contains(CredentialEngine.PlaceholderPrefix, sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandResult_NeverEchoesRuleValue()
    {
        var plugin = CreatePlugin();

        var result = await RunAsync(plugin, CredentialSettingsUi.AddRule,
            (CredentialSettingsUi.NewValueField, "super_secret_field"),
            (CredentialSettingsUi.NewKindField, "名称"));

        Assert.DoesNotContain("super_secret_field", result.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_DeclaresDisableConsequences()
    {
        var plugin = CreatePlugin();
        var text = FlattenText(GetSettingsNode(plugin));

        // 停用利害关系必须在设置页显式声明：停用行为、保留能力、停用代价。
        Assert.Contains("停用后不再检测", text, StringComparison.Ordinal);
        Assert.Contains("停用后仍然保留", text, StringComparison.Ordinal);
        Assert.Contains("停用的代价", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_ReflectsProtectionState()
    {
        var plugin = CreatePlugin();
        plugin.OnPluginEnabledChanged(false);

        var text = FlattenText(GetSettingsNode(plugin));

        Assert.Contains("已停用", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleValue_IsShownVerbatimInList()
    {
        var plugin = CreatePlugin();
        plugin.RuleStore.AddRule(SensitiveRuleKind.Name, "vault_path", out _, out _);
        plugin.RuleStore.AddRule(
            SensitiveRuleKind.Pattern, @"(?<![a-z0-9])x-vault-[0-9]{8}(?![a-z0-9])", out _, out _);

        var text = FlattenText(GetSettingsNode(plugin));

        // 规则值原样展示：用户要能一眼确认自己写的是哪条规则，
        // 否则打码后的星号既无法辨认、也无法与规则文件对上。
        Assert.Contains("vault_path", text, StringComparison.Ordinal);
        Assert.Contains(@"(?<![a-z0-9])x-vault-[0-9]{8}(?![a-z0-9])", text, StringComparison.Ordinal);
        // 展示层不再产生掩码占位。
        Assert.DoesNotMatch(@"\*{2,}", text);
    }

    private static PluginUiNode GetSettingsNode(CredentialProtectionPlugin plugin) =>
        plugin.GetUiContributions(new PluginUiContext("zh-CN"))
            .Single(item => item.Slot == PluginUiSlot.DetailBody)
            .Root;

    private static string FlattenText(PluginUiNode node) => node switch
    {
        PluginUiTextNode text => text.Text,
        PluginUiStackNode stack => string.Join("\n", stack.Children.Select(FlattenText)),
        PluginUiGridNode grid => string.Join("\n", grid.Children.Select(item => FlattenText(item.Content))),
        PluginUiSurfaceNode surface => FlattenText(surface.Content),
        PluginUiDividerNode => string.Empty,
        _ => string.Empty,
    };
}