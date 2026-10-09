using LoomX.Maskit;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class MaskitRulesTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(), "loomx-tests", "maskit-rules", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
            Directory.Delete(dataDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddUpdateDeleteRoundTrip()
    {
        var store = new MaskitRuleStore(dataDirectory);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        Assert.True(store.AddRule("人名", "张三", MaskitRuleKind.Word, true, out var rule, out _));
        Assert.NotNull(rule);
        Assert.Equal("人名", rule!.Category);
        Assert.Equal("张三", rule.Value);

        // 查：列表与检索
        Assert.Single(store.Rules);
        Assert.Equal(rule.Id, store.Rules[0].Id);

        // 改
        Assert.True(store.UpdateRule(rule.Id, "内部代号", "Project-X", false, out _));
        var updated = Assert.Single(store.Rules);
        Assert.Equal("内部代号", updated.Category);
        Assert.Equal("Project-X", updated.Value);
        Assert.False(updated.WholeWord);

        // 删
        Assert.True(store.RemoveRule(rule.Id));
        Assert.Empty(store.Rules);
        Assert.True(changed >= 3);
    }

    [Fact]
    public void RejectsDuplicateInvalidAndOversizedRules()
    {
        var store = new MaskitRuleStore(dataDirectory);
        Assert.True(store.AddRule("人名", "张三", MaskitRuleKind.Word, true, out _, out _));

        Assert.False(store.AddRule("人名", "张三", MaskitRuleKind.Word, true, out _, out var duplicate));
        Assert.Equal("DUPLICATE_RULE", duplicate);

        Assert.False(store.AddRule("  ", "张三", MaskitRuleKind.Word, true, out _, out var emptyCategory));
        Assert.Equal("INVALID_CATEGORY", emptyCategory);

        Assert.False(store.AddRule("人名", " ", MaskitRuleKind.Word, true, out _, out var emptyValue));
        Assert.Equal("INVALID_VALUE", emptyValue);

        Assert.False(store.AddRule("正则", "[unclosed", MaskitRuleKind.Pattern, true, out _, out var badPattern));
        Assert.Equal("INVALID_PATTERN", badPattern);

        Assert.Single(store.Rules);
    }

    [Fact]
    public void BuiltinRulesToggleAndPersistAcrossStores()
    {
        var store = new MaskitRuleStore(dataDirectory);
        Assert.True(store.BuiltinRules["PHONE"]);
        Assert.False(store.BuiltinRules["IP_PUBLIC"]);

        Assert.True(store.SetBuiltinEnabled("IP_PUBLIC", true));
        Assert.True(store.BuiltinRules["IP_PUBLIC"]);
        // 相同状态不重复触发变更
        Assert.False(store.SetBuiltinEnabled("IP_PUBLIC", true));

        var reloaded = new MaskitRuleStore(dataDirectory);
        Assert.True(reloaded.BuiltinRules["IP_PUBLIC"]);
        Assert.False(reloaded.SetBuiltinEnabled("UNKNOWN_KEY", true));
    }

    [Fact]
    public void SecretPrefixesRoundTrip()
    {
        var store = new MaskitRuleStore(dataDirectory);
        Assert.True(store.AddPrefix("sk-", out _));
        Assert.False(store.AddPrefix("sk-", out var duplicate));
        Assert.Equal("DUPLICATE_PREFIX", duplicate);
        Assert.False(store.AddPrefix("has space", out var invalid));
        Assert.Equal("INVALID_PREFIX", invalid);

        Assert.Equal("sk-", Assert.Single(store.SecretPrefixes));
        Assert.True(store.RemovePrefix("sk-"));
        Assert.Empty(store.SecretPrefixes);
    }

    [Fact]
    public void EngineMasksWordAndRestoresOnlyLocalPlaceholders()
    {
        var store = new MaskitRuleStore(dataDirectory);
        store.AddRule("人名", "张三", MaskitRuleKind.Word, true, out _, out _);
        store.SetBuiltinEnabled("PHONE", true);
        var engine = new MaskitEngine(store);

        var masked = engine.Sanitize("联系人张三 13800138000", out var replacements);
        Assert.Equal(2, replacements);
        Assert.DoesNotContain("张三", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("13800138000", masked, StringComparison.Ordinal);
        Assert.Contains("<MASKIT:", masked, StringComparison.Ordinal);

        // 响应侧还原
        var restored = engine.Restore(masked, out var restoredCount);
        Assert.Equal(2, restoredCount);
        Assert.Equal("联系人张三 13800138000", restored);

        // 未知占位符保持原样（fail closed，不猜测）
        var unknown = engine.Restore("<MASKIT:人名:99999>", out var unknownCount);
        Assert.Equal(0, unknownCount);
        Assert.Equal("<MASKIT:人名:99999>", unknown);
    }

    [Fact]
    public void DisabledRulesDoNotMaskAndWholeWordControlsBoundaries()
    {
        var store = new MaskitRuleStore(dataDirectory);
        store.AddRule("代号", "abc", MaskitRuleKind.Word, wholeWord: false, out var rule, out _);
        var engine = new MaskitEngine(store);

        Assert.Contains("<MASKIT:", engine.Sanitize("xxabcxx", out var loose), StringComparison.Ordinal);
        Assert.Equal(1, loose);

        Assert.True(store.UpdateRule(rule!.Id, "代号", "abc", wholeWord: true, out _));
        Assert.Equal("xxabcxx", engine.Sanitize("xxabcxx", out var strict));
        Assert.Equal(0, strict);

        Assert.True(store.SetRuleEnabled(rule.Id, false));
        Assert.Equal("abc", engine.Sanitize("abc", out var disabled));
        Assert.Equal(0, disabled);
    }

    [Fact]
    public void SecretPrefixRuleMasksPrefixedSecrets()
    {
        var store = new MaskitRuleStore(dataDirectory);
        store.AddPrefix("sk-", out _);
        var engine = new MaskitEngine(store);

        var masked = engine.Sanitize("key=sk-abcdef123456", out var replacements);
        Assert.Equal(1, replacements);
        Assert.DoesNotContain("sk-abcdef123456", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialCategoryIsMaskedInListView()
    {
        // 凭据类标签对齐 maskit 红线：列表展示恒打码，不得回显原文
        Assert.DoesNotContain("sk-live", MaskitSettingsUi.MaskWord("sk-live-abcdef", "API_KEY"), StringComparison.Ordinal);
        Assert.Contains("A", MaskitSettingsUi.MaskWord("Alice", "NAME"), StringComparison.Ordinal);
    }
}
