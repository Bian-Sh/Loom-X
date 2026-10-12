using System.Xml.Linq;
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

    [Fact]
    public void CredentialPluginProject_DoesNotReferenceSqliteProviderOrHostImplementation()
    {
        var project = File.ReadAllText(GetRepositoryFile(
            "plugins", "LoomX.CredentialProtection", "LoomX.CredentialProtection.csproj"));
        Assert.DoesNotContain("Microsoft.Data.Sqlite", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLitePCLRaw", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LoomX.Plugin.Storage.Sqlite", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LoomX.Plugin.Abstractions.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialPluginAssembly_DependsOnlyOnPublicContractForStorage()
    {
        var references = typeof(LoomX.CredentialProtection.CredentialTokenStore).Assembly.GetReferencedAssemblies();
        Assert.Contains(references, item => item.Name == "LoomX.Plugin.Abstractions");
        Assert.DoesNotContain(references, item => item.Name is "Microsoft.Data.Sqlite" or "LoomX.Plugin.Storage.Sqlite" or "LoomX.PluginHost" or "System.Security.Cryptography.ProtectedData");
        Assert.DoesNotContain(references, item => item.Name?.StartsWith("SQLitePCLRaw.", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void CredentialPluginBuildPayload_DoesNotCarrySqliteManagedOrNativeLibraries()
    {
        // 静态 DLL 仅服务单测；运行时负载只检查 plugins/<id> 源码包。
        var mirroredPayload = Path.Combine(AppContext.BaseDirectory, "plugins", "loomx.credential-protection");
        Assert.True(File.Exists(Path.Combine(mirroredPayload, "plugin.manifest.json")));
        Assert.DoesNotContain(Directory.GetFiles(mirroredPayload, "*", SearchOption.AllDirectories), IsSqlitePayload);
    }

    [Theory]
    [InlineData("LoomX")]
    [InlineData("LoomX.PluginPlayground")]
    public void Consumers_DoNotRecursivelyCopyPluginBuildDirectories(string projectName)
    {
        var project = XDocument.Load(GetRepositoryFile(projectName, projectName + ".csproj"));
        var includes = project.Descendants().Attributes("Include").Select(attribute => attribute.Value);
        Assert.DoesNotContain(includes, value => value.Contains("OutputDir", StringComparison.Ordinal)
            && value.Contains("**", StringComparison.Ordinal));
        Assert.Contains(project.Descendants("Import"), import =>
            ((string?)import.Attribute("Project"))?.EndsWith("FirstPartySourcePayload.targets", StringComparison.Ordinal) == true);
        foreach (var target in project.Descendants("Target").Where(target =>
            ((string?)target.Attribute("Name"))?.StartsWith("CopyFirstPartyPlugins", StringComparison.Ordinal) == true))
        {
            Assert.Contains(target.Descendants("Copy"), copy =>
                (string?)copy.Attribute("SourceFiles") == "@(_CredentialPluginPayload)");
            Assert.Contains(target.Descendants("Copy"), copy =>
                (string?)copy.Attribute("SourceFiles") == "@(_CompressionPluginPayload)");
        }
    }

    private static bool IsSqlitePayload(string file)
    {
        var name = Path.GetFileName(file);
        return name.Equals("Microsoft.Data.Sqlite.dll", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SQLitePCLRaw.", StringComparison.OrdinalIgnoreCase)
            || name.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("FirstPartySourcePayload.targets", project, StringComparison.Ordinal);
        Assert.Contains("<PluginPayload Include=\"@(_", project, StringComparison.Ordinal);
        Assert.Contains("%(PackageRelativePath)", project, StringComparison.Ordinal);
        Assert.Contains("<ImplicitUsings>disable</ImplicitUsings>", project, StringComparison.Ordinal);
        Assert.Contains(@"i18n\en-US.json", project, StringComparison.Ordinal);
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
