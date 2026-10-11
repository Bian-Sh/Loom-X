using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 凭据保护的文案已从 C# 字面量迁到插件自有 <c>i18n/*.json</c>。
/// 这里守住两件事：四语言都完整，且关键文案（含停用语义与内建规则名）没有在迁移中丢失。
/// </summary>
public sealed class CredentialProtectionLocalizationTests
{
    private static readonly string[] Cultures = ["zh-CN", "zh-TW", "ja-JP", "en-US"];

    [Fact]
    public void SettingsText_ResolvesPerCultureWithoutMissingKeys()
    {
        foreach (var culture in Cultures)
        {
            var text = CredentialSettingsText.Resolve(culture);

            foreach (var value in Flatten(text))
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{culture} has an empty field");
                Assert.DoesNotContain(".hint", value, StringComparison.Ordinal);
                Assert.DoesNotContain("TextCatalog", value, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DisableSemantics_AreFullyTranslatedInEveryCulture()
    {
        // 停用语义是安全相关的用户提示，四种语言都必须给出「历史引用仍会还原」这一条。
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zh-CN"] = "历史会话中已签发的凭据引用仍会被识别",
            ["zh-TW"] = "歷史會話中已簽發的憑據引用仍會被識別",
            ["ja-JP"] = "過去のセッションで発行された資格情報参照は引き続き識別され",
            ["en-US"] = "Credential references issued in past sessions are still recognised",
        };

        foreach (var (culture, fragment) in expected)
        {
            var catalog = PluginTextCatalog.For(
                typeof(CredentialSettingsText),
                CredentialProtectionPlugin.PluginId,
                culture);
            var resolved = CredentialSettingsText.Resolve(culture);

            Assert.Contains(fragment, resolved.ProtectionKeepsBody, StringComparison.Ordinal);
            Assert.Equal(
                fragment,
                catalog.Get("protectionKeepsBody")[..fragment.Length],
                StringComparer.Ordinal);
        }
    }

    [Fact]
    public void BuiltinRuleNames_AreTranslatedAndHintsFallBackToEmpty()
    {
        Assert.Equal("私钥", CredentialSettingsText.Resolve("zh-CN").BuiltinName("PRIVATE_KEY"));
        Assert.Equal("秘密鍵", CredentialSettingsText.Resolve("ja-JP").BuiltinName("PRIVATE_KEY"));
        Assert.Equal("Private key", CredentialSettingsText.Resolve("en-US").BuiltinName("PRIVATE_KEY"));
        // 未登记的规则保留原始标识，不显示空白也不泄漏键名形态。
        Assert.Equal("NOT_A_RULE", CredentialSettingsText.Resolve("zh-CN").BuiltinName("NOT_A_RULE"));
        // 资源里 hint 为空串时不应显示成 ".hint"。
        Assert.Equal(string.Empty, CredentialSettingsText.Resolve("en-US").BuiltinHint("NOT_A_RULE"));
        Assert.NotEqual(string.Empty, CredentialSettingsText.Resolve("zh-CN").BuiltinHint("PRIVATE_KEY"));
    }

    [Fact]
    public void DisplayInfo_IsProvidedByThePluginItself()
    {
        var plugin = new CredentialProtectionPlugin();

        var zh = plugin.GetDisplayInfo(new PluginUiContext("zh-CN"));
        var en = plugin.GetDisplayInfo(new PluginUiContext("en-US"));

        Assert.NotNull(zh);
        Assert.Equal("凭据保护", zh!.DisplayName);
        Assert.Equal("Credential Protection", en!.DisplayName);
        Assert.Equal("系统插件", zh.Category);
        Assert.Contains("停用", zh.LifecycleNote, StringComparison.Ordinal);
        Assert.NotEqual(zh.DisplayName, en.DisplayName);
    }

    [Fact]
    public void CardOverview_RendersPluginOwnTextPerCulture()
    {
        var plugin = new CredentialProtectionPlugin();
        plugin.Initialize(new PluginInitializationContext(
            CredentialProtectionPlugin.PluginId,
            Path.Combine(Path.GetTempPath(), "loomx-cp-i18n")));

        var zh = string.Join('\n', plugin.GetUiContributions(new PluginUiContext("zh-CN"))
            .Where(item => item.Slot == PluginUiSlot.CardBody)
            .SelectMany(item => Flatten(item.Root)));
        var en = string.Join('\n', plugin.GetUiContributions(new PluginUiContext("en-US"))
            .Where(item => item.Slot == PluginUiSlot.CardBody)
            .SelectMany(item => Flatten(item.Root)));

        Assert.Contains("已脱敏请求", zh, StringComparison.Ordinal);
        Assert.Contains("已还原回复", zh, StringComparison.Ordinal);
        Assert.Contains("Sanitized requests", en, StringComparison.Ordinal);
        Assert.Contains("Restored responses", en, StringComparison.Ordinal);
    }

    [Fact]
    public void UiTextFiles_ShareTheSameKeySetAcrossCultures()
    {
        var directory = Path.Combine(RepositoryRoot(), "plugins", "LoomX.CredentialProtection", "i18n");
        Assert.True(Directory.Exists(directory), directory);

        var reference = ReadKeys(Path.Combine(directory, "en-US.json"));
        Assert.NotEmpty(reference);

        foreach (var culture in Cultures)
        {
            var keys = ReadKeys(Path.Combine(directory, culture + ".json"));
            Assert.Equal(reference.Count, keys.Count);
            Assert.Empty(reference.Except(keys, StringComparer.Ordinal));
            Assert.Empty(keys.Except(reference, StringComparer.Ordinal));
        }
    }

    private static HashSet<string> ReadKeys(string path)
    {
        Assert.True(File.Exists(path), path);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>向上找解决方案根目录，避免依赖固定的 bin 层数。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static IEnumerable<string> Flatten(CredentialSettingsText text)
    {
        yield return text.AddSectionTitle;
        yield return text.AddSectionHint;
        yield return text.FieldValue;
        yield return text.FieldKind;
        yield return text.KindName;
        yield return text.KindPattern;
        yield return text.ButtonAdd;
        yield return text.ProtectionTitle;
        yield return text.StateActive;
        yield return text.StateInactive;
        yield return text.ProtectionBody;
        yield return text.ProtectionKeepsTitle;
        yield return text.ProtectionKeepsBody;
        yield return text.ProtectionRiskTitle;
        yield return text.ProtectionRiskBody;
        yield return text.RulesSectionTitle;
        yield return text.EmptyRulesHint;
        yield return text.MoreRulesHint;
        yield return text.TagName;
        yield return text.TagPattern;
        yield return text.BaselineNameLabel;
        yield return text.BaselinePatternLabel;
        yield return text.EnableToggleLabel;
        yield return text.ButtonEdit;
        yield return text.ButtonDelete;
        yield return text.ButtonSave;
        yield return text.ButtonCancel;
        yield return text.BuiltinSectionTitle;
        yield return text.BuiltinSectionHint;
        yield return text.BaselineSectionTitle;
        yield return text.BaselineSectionHint;
        yield return text.GroupCoreTitle;
        yield return text.GroupCoreHint;
        yield return text.GroupCredentialsTitle;
        yield return text.GroupCredentialsHint;
        yield return text.GroupNetworkTitle;
        yield return text.GroupNetworkHint;
        yield return text.GroupEntitiesTitle;
        yield return text.GroupEntitiesHint;
        yield return text.ButtonAllOn;
        yield return text.ButtonAllOff;
        yield return text.RuleCountFormat;
        yield return text.PrefixSectionTitle;
        yield return text.PrefixSectionHint;
        yield return text.FieldPrefix;
        yield return text.ButtonAddPrefix;
        yield return text.EmptyPrefixHint;
        yield return text.RuleAdded;
        yield return text.RuleUpdated;
        yield return text.RuleDeleted;
        yield return text.RuleEnabled;
        yield return text.RuleDisabled;
        yield return text.RuleNotFound;
        yield return text.BuiltinEnabled;
        yield return text.BuiltinDisabled;
        yield return text.BuiltinNotFound;
        yield return text.AllBuiltinsEnabled;
        yield return text.AllBuiltinsDisabled;
        yield return text.PrefixAdded;
        yield return text.PrefixDeleted;
        yield return text.PrefixNotFound;
        yield return text.ErrorInvalidPattern;
        yield return text.ErrorInvalidValue;
        yield return text.ErrorDuplicateRule;
        yield return text.ErrorTooManyRules;
        yield return text.ErrorInvalidPrefix;
        yield return text.ErrorDuplicatePrefix;
        yield return text.ErrorTooManyPrefixes;
        yield return text.ErrorUnknown;
    }

    private static IEnumerable<string> Flatten(PluginUiNode node)
    {
        switch (node)
        {
            case PluginUiTextNode text:
                yield return text.Text;
                break;
            case PluginUiStackNode stack:
                foreach (var child in stack.Children.SelectMany(Flatten)) yield return child;
                break;
            case PluginUiGridNode grid:
                foreach (var child in grid.Children.Select(item => item.Content).SelectMany(Flatten))
                    yield return child;
                break;
            case PluginUiSurfaceNode surface:
                foreach (var child in Flatten(surface.Content)) yield return child;
                break;
        }
    }
}
