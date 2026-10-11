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
    public void DesktopProject_CopiesEachFirstPartyPluginIntoItsOwnDirectory()
    {
        var source = File.ReadAllText(GetRepositoryFile("LoomX", "LoomX.csproj"));

        // 两个插件共用一个 OutputItemType 会让负载落到同一目录互相覆盖（Manifest 同名尤其致命），
        // 因此必须各自独立声明并各自拷贝。
        Assert.Contains("OutputItemType=\"FirstPartyCredentialPlugin\"", source, StringComparison.Ordinal);
        Assert.Contains("OutputItemType=\"FirstPartyCompressionPlugin\"", source, StringComparison.Ordinal);
        Assert.Contains("$(PublishDir)plugins\\loomx.tool-result-compression", source, StringComparison.Ordinal);
        Assert.Contains("$(OutputPath)plugins\\loomx.tool-result-compression", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompressionPluginPayloadIncludesManifestAndInteractiveSettings()
    {
        var project = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.ToolResultCompression", "LoomX.ToolResultCompression.csproj"));
        var manifest = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.ToolResultCompression", "plugin.manifest.json"));

        AssertPluginPayloadIsCopiedByItsOwnTarget(project);
        Assert.Contains("\"id\": \"observability\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"slot\": \"card-body\"", manifest, StringComparison.Ordinal);
        // 设置页以交互式 detail-body 贡献，未声明 interactive 的贡献不接收命令。
        Assert.Contains("\"slot\": \"detail-body\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"interactive\": true", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstPartyPluginPublishPayloadIncludesManifestAndCardContribution()
    {
        var project = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.CredentialProtection", "LoomX.CredentialProtection.csproj"));
        var manifest = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.CredentialProtection", "plugin.manifest.json"));

        AssertPluginPayloadIsCopiedByItsOwnTarget(project);
        Assert.Contains("\"id\": \"observability\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"slot\": \"card-body\"", manifest, StringComparison.Ordinal);
        // 设置页以交互式 detail-body 贡献，未声明 interactive 的贡献不接收命令。
        Assert.Contains("\"slot\": \"detail-body\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"interactive\": true", manifest, StringComparison.Ordinal);
    }

    /// <summary>
    /// 插件负载必须由插件工程自己的目标拷进它自己的输出目录。
    /// 一旦改回 Content + CopyToOutputDirectory，MSBuild 会把负载传给每个引用方，
    /// 同名的 plugin.manifest.json、i18n/&lt;culture&gt;.json 平铺后会互相覆盖。
    /// </summary>
    private static void AssertPluginPayloadIsCopiedByItsOwnTarget(string project)
    {
        Assert.Contains("Name=\"CopyPluginPayloadToOutput\"", project, StringComparison.Ordinal);
        Assert.Contains("AfterTargets=\"Build\"", project, StringComparison.Ordinal);
        Assert.Contains("PluginPayload Include=\"plugin.manifest.json\"", project, StringComparison.Ordinal);
        Assert.Contains("PluginPayload Include=\"i18n\\**\\*.json\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Content Include=\"plugin.manifest.json\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Content Include=\"i18n", project, StringComparison.Ordinal);
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
