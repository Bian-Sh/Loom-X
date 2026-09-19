using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class SkillStoreTests : IDisposable
{
    private readonly string rootDirectory = Path.Combine(Path.GetTempPath(), $"loomx-skillstore-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true); } catch (IOException) { }
    }

    private string CreateSkill(string category, string name, string manifest, string content)
    {
        var directory = Path.Combine(rootDirectory, category, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), content);
        return directory;
    }

    [Fact]
    public void List_ReturnsManifestsAcrossCategories()
    {
        CreateSkill("providers", "generic-openai-compatible", """{"name":"generic-openai-compatible","description":"通用 OpenAI 兼容","when_to_use":"接入 OpenAI 兼容服务"}""", "# A");
        CreateSkill("relays", "new-api", """{"name":"new-api","description":"New-API 中转站"}""", "# B");

        var manifests = new SkillStore(rootDirectory).List();

        Assert.Equal(2, manifests.Count);
        Assert.Contains(manifests, item => item.Category == "providers" && item.Name == "generic-openai-compatible" && item.WhenToUse == "接入 OpenAI 兼容服务");
        Assert.Contains(manifests, item => item.Category == "relays" && item.Name == "new-api");
    }

    [Fact]
    public void List_SkipsInvalidManifests()
    {
        CreateSkill("providers", "broken", """{"description":"缺 name"}""", "# C");
        CreateSkill("providers", "not-json", "这不是 JSON", "# D");

        Assert.Empty(new SkillStore(rootDirectory).List());
    }

    [Fact]
    public void List_MissingRootDirectory_ReturnsEmpty()
    {
        Assert.Empty(new SkillStore(Path.Combine(rootDirectory, "不存在")).List());
    }

    [Fact]
    public void Load_ReturnsContent()
    {
        CreateSkill("clients", "codex", """{"name":"codex","description":"Codex 配置"}""", "# Codex Skill 正文");

        var document = new SkillStore(rootDirectory).Load("clients", "codex");

        Assert.NotNull(document);
        Assert.Equal("# Codex Skill 正文", document.Content);
        Assert.Equal("codex", document.Manifest.Name);
    }

    [Fact]
    public void Load_UnknownSkill_ReturnsNull()
    {
        Assert.Null(new SkillStore(rootDirectory).Load("clients", "codex"));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    public void InstallDirectory_ClientSkill_DeclaresResearchChannelsAndChallengeHandoff(string skillName)
    {
        var document = SkillStore.ForInstallDirectory().Load("clients", skillName);

        Assert.NotNull(document);
        var content = document.Content;
        var nativeIndex = content.IndexOf("优先使用模型原生或已有的官方资料能力", StringComparison.Ordinal);
        var browserIndex = content.IndexOf("其次使用 Browser Bridge", StringComparison.Ordinal);
        var askUserIndex = content.IndexOf("没有可用资料通道时", StringComparison.Ordinal);

        Assert.True(nativeIndex >= 0 && nativeIndex < browserIndex && browserIndex < askUserIndex);
        Assert.Contains("browser.open", content);
        Assert.Contains("browser.read", content);
        Assert.Contains("browser.wait", content);
        Assert.Contains("登录", content);
        Assert.Contains("CAPTCHA", content);
        Assert.Contains("Cloudflare", content);
        Assert.Contains("JS challenge", content);
        Assert.Contains("立即暂停并交还用户", content);
        Assert.Contains("禁止绕过网站安全机制", content);
        AssertNoSearchSecretConfiguration(content);
    }

    private static void AssertNoSearchSecretConfiguration(string content)
    {
        foreach (var forbidden in new[]
        {
            "SEARCH_API_KEY",
            "SERPAPI_API_KEY",
            "TAVILY_API_KEY",
            "BRAVE_SEARCH_API_KEY",
            "BING_SEARCH_API_KEY",
            "GOOGLE_SEARCH_API_KEY",
        })
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
