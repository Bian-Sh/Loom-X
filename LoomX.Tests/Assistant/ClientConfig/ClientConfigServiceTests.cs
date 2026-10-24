using LoomX.Assistant;
using LoomX.Assistant.ClientConfig;
using LoomX.Assistant.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant.ClientConfig;

/// <summary>
/// ClientConfigService 行为：fake home 封闭性、备份、拒绝覆写、目录创建。
/// 所有写入都发生在 Temp 下的 fake home（codextest-home-*），绝不使用真实用户主目录。
/// </summary>
public sealed class ClientConfigServiceTests : IDisposable
{
    private const string ApiKey = "sk-loomx-service-key";

    private readonly string fakeHome = Path.Combine(Path.GetTempPath(), $"codextest-home-{Guid.NewGuid():N}");
    private readonly ClientConfigService service;

    public ClientConfigServiceTests()
    {
        service = new ClientConfigService(
            new TomlDocumentService(NullLogger<TomlDocumentService>.Instance),
            NullLogger<ClientConfigService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(fakeHome))
        {
            Directory.Delete(fakeHome, recursive: true);
        }
    }

    private static ClientWriteInput Input(params string[] models) => new(
        "http://127.0.0.1:43111",
        ApiKey,
        models.Select(id => new ClientModelInfo(id, 200_000, 128_000, [], true)).ToArray());

    private static string TomlNormText(string text) =>
        text.Replace("\"", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    [Fact]
    public async Task 写入_使用FakeHome_解析路径不越界()
    {
        foreach (var target in service.Targets)
        {
            var path = service.ResolvePath(target.TargetKey, fakeHome);
            Assert.StartsWith(fakeHome, path.FullName, StringComparison.OrdinalIgnoreCase);
        }

        // zcode 在 fake home 下忽略机器上的 ZCODE_DATA_BASE_DIR/HOME 环境变量（测试封闭）。
        var zcodePath = service.ResolvePath("zcode-codex", fakeHome);
        Assert.Equal(
            Path.Combine(fakeHome, ".zcode", "v2", "provider_config.json"),
            zcodePath.FullName,
            ignoreCase: true,
            ignoreLineEndingDifferences: false,
            ignoreWhiteSpaceDifferences: false,
            ignoreAllWhiteSpace: false);
    }

    [Fact]
    public async Task 写入_codex全部落盘且目录自动创建()
    {
        var outcome = await service.WriteAsync("codex", fakeHome, Input("glm-5.3"), CancellationToken.None);

        Assert.True(outcome.Changed);
        Assert.Null(outcome.BackupPath);
        Assert.True(File.Exists(outcome.ConfigPath));
        Assert.True(File.Exists(outcome.CatalogPath));
        Assert.StartsWith(fakeHome, outcome.ConfigPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(fakeHome, outcome.CatalogPath!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TomlNormText("model_provider = \"loomx\""), TomlNormText(await File.ReadAllTextAsync(outcome.ConfigPath)), StringComparison.Ordinal);
        Assert.Equal(["glm-5.3"], outcome.ModelsWritten);
    }

    [Fact]
    public async Task 写入_覆盖时生成备份且内容为旧文件()
    {
        var first = await service.WriteAsync("codex", fakeHome, Input("glm-5.3"), CancellationToken.None);
        var original = await File.ReadAllTextAsync(first.ConfigPath);
        // 等一拍，避免同文件名时间戳碰撞（时间戳含毫秒，仍保守等待）。
        await Task.Delay(20);

        var second = await service.WriteAsync("codex", fakeHome, Input("kimi-k3"), CancellationToken.None);

        Assert.NotNull(second.BackupPath);
        Assert.NotEqual(first.ConfigPath, second.BackupPath);
        Assert.Equal(original, await File.ReadAllTextAsync(second.BackupPath!));
        Assert.Contains("kimi-k3", await File.ReadAllTextAsync(second.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 写入_非法既有配置拒绝覆写且原文件原样保留()
    {
        const string corrupt = "{not json";
        var path = service.ResolvePath("zcode-codex", fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        await File.WriteAllTextAsync(path.FullName, corrupt);

        await Assert.ThrowsAsync<ClientConfigException>(() =>
            service.WriteAsync("zcode-codex", fakeHome, Input(), CancellationToken.None));

        Assert.Equal(corrupt, await File.ReadAllTextAsync(path.FullName));
        Assert.Empty(Directory.GetFiles(path.Directory!.FullName, "*.bak-*"));
        var inspection = await service.InspectAsync("zcode-codex", fakeHome, CancellationToken.None);
        Assert.Equal("invalid", inspection.FileStatus);
    }

    [Fact]
    public async Task 检查_缺失与已托管状态()
    {
        Assert.Equal("missing", (await service.InspectAsync("codex", fakeHome)).FileStatus);

        await service.WriteAsync("codex", fakeHome, Input(), CancellationToken.None);
        var inspection = await service.InspectAsync("codex", fakeHome);
        Assert.Equal("managed", inspection.FileStatus);
        Assert.True(inspection.Managed);
    }

    [Fact]
    public async Task 未知客户端标识抛出参数异常()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.WriteAsync("not-a-client", fakeHome, Input(), CancellationToken.None));
        Assert.Null(service.Find("not-a-client"));
    }

    [Fact]
    public void 目标清单包含九个客户端()
    {
        var keys = service.Targets.Select(target => target.TargetKey).ToArray();
        Assert.Equal(
            ["claude-code", "codex", "gemini-cli", "grok", "hermes", "openclaw", "opencode", "zcode-claude", "zcode-codex"],
            keys);
        Assert.All(service.Targets, target => Assert.False(string.IsNullOrWhiteSpace(target.DisplayName)));
    }

    [Fact]
    public void ClientHome_环境变量重定向只作用于真实用户主目录()
    {
        const string variable = "LOOMX_TEST_ZCODE_BASE";
        try
        {
            Environment.SetEnvironmentVariable(variable, Path.Combine(Path.GetTempPath(), "redirected-zcode"));
            // fake home ≠ 真实主目录：原样透传（测试封闭）。
            Assert.Equal(fakeHome, ClientHome.ResolveBase(variable, fakeHome));
            // 真实主目录：遵循客户端自己的解析链。
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                Assert.Equal(Path.Combine(Path.GetTempPath(), "redirected-zcode"), ClientHome.ResolveBase(variable, profile));
                Assert.Equal(profile, ClientHome.ResolveBase("LOOMX_TEST_UNSET_VARIABLE", profile));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task 写入结果不泄漏ApiKey到Catalog()
    {
        var outcome = await service.WriteAsync("codex", fakeHome, Input(), CancellationToken.None);
        Assert.DoesNotContain(ApiKey, await File.ReadAllTextAsync(outcome.CatalogPath!), StringComparison.Ordinal);
        Assert.Contains(ApiKey, await File.ReadAllTextAsync(outcome.ConfigPath), StringComparison.Ordinal);
    }
}
