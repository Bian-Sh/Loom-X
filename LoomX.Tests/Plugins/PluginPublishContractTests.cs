using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginPublishContractTests
{
    [Fact]
    public void DesktopProject_CopiesFirstPartyPluginIntoPublishDirectory()
    {
        var source = File.ReadAllText(GetRepositoryFile("LoomX", "LoomX.csproj"));

        Assert.Contains("Name=\"CopyFirstPartyPluginsToPublish\"", source, StringComparison.Ordinal);
        Assert.Contains("AfterTargets=\"Publish\"", source, StringComparison.Ordinal);
        Assert.Contains("$(PublishDir)plugins\\loomx.credential-protection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstPartyPluginPublishPayloadIncludesManifestAndCardContribution()
    {
        var project = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.CredentialProtection", "LoomX.CredentialProtection.csproj"));
        var manifest = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.CredentialProtection", "plugin.manifest.json"));

        Assert.Contains("Content Include=\"plugin.manifest.json\"", project, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"observability\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"slot\": \"card-body\"", manifest, StringComparison.Ordinal);
        // 设置页以交互式 detail-body 贡献，未声明 interactive 的贡献不接收命令。
        Assert.Contains("\"slot\": \"detail-body\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"interactive\": true", manifest, StringComparison.Ordinal);
    }

    private static string GetRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
