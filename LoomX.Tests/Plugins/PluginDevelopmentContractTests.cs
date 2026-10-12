using System.Text.Json;
using System.Text.RegularExpressions;
using LoomX.Assistant;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginDevelopmentContractTests
{
    [Fact]
    public async Task 工具注册具有六个稳定名称()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var registry = new ToolRegistry();
        PluginDevelopmentTools.RegisterAll(registry, fixture.Service);
        Assert.Equal(ExpectedNames, registry.All.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void 内置技能目录可发现插件开发知识包()
    {
        var store = new SkillStore(Path.Combine(FindWorkspace(), "LoomX", "Skills"));
        var document = store.Load("plugins", "development-v1");
        Assert.NotNull(document);
        Assert.Contains(store.List(), skill => skill.Category == "plugins" && skill.Name == "development-v1");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindWorkspace(), "LoomX", "Skills", "plugins", "development-v1", "manifest.json")));
        Assert.Equal(PluginDevelopmentService.PluginApiMajor, manifest.RootElement.GetProperty("plugin_api_major").GetInt32());
    }

    [Fact]
    public async Task 仅按Skill公开示例可以创建编译加载并测试SQLite与Pipeline()
    {
        await using var fixture = new PluginDevelopmentFixture();
        var store = new SkillStore(Path.Combine(FindWorkspace(), "LoomX", "Skills"));
        var text = store.Load("plugins", "development-v1")!.Content;
        Assert.True((await fixture.Service.CreateAsync("example.echo", "Example.Plugin")).Success);
        fixture.Write("example.echo", "plugin.manifest.json", Example(text, "example-manifest", "json"));
        fixture.Write("example.echo", "src/Plugin.cs", Example(text, "example-source", "csharp"));
        fixture.Write("example.echo", "tests/PluginTests.cs", Example(text, "example-tests", "csharp"));
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            fixture.Write("example.echo", "i18n/" + culture + ".json", """{"card_title":"可执行示例"}""");
        Assert.True((await fixture.Service.ValidateAsync("example.echo")).Success);
        var compilation = await fixture.Service.CompileAsync("example.echo");
        Assert.True(compilation.Success, JsonSerializer.Serialize(compilation));
        var test = await fixture.Service.TestAsync("example.echo", true,
            [new("development.request", "合成输入", "合成输入-已处理", LoomX.Plugins.PipelineOutcome.Modified)]);
        Assert.True(test.Success, JsonSerializer.Serialize(test));
        Assert.Equal(2, test.TestsPassed);
        Assert.Empty(fixture.Runtime.PluginInfos);
    }

    private static string Example(string text, string marker, string language)
    {
        var match = Regex.Match(text, "<!-- " + marker + " -->\\s*```" + language + "\\s*\\r?\\n(?<code>[\\s\\S]*?)```", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Skill 示例块缺失。");
        return match.Groups["code"].Value;
    }

    internal static string FindWorkspace()
    {
        for (var path = AppContext.BaseDirectory; path is not null; path = Path.GetDirectoryName(path))
            if (File.Exists(Path.Combine(path, "LoomX.slnx"))) return path;
        throw new InvalidOperationException("无法找到测试工作树。");
    }

    private static readonly string[] ExpectedNames = [
        "loomx.plugin.compile", "loomx.plugin.create", "loomx.plugin.diagnostics",
        "loomx.plugin.reload", "loomx.plugin.test", "loomx.plugin.validate",
    ];
}
