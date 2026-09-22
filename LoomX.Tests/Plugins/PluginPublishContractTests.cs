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

    private static string GetRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
