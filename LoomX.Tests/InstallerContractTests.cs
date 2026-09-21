using Xunit;

namespace LoomX.Tests;

public sealed class InstallerContractTests
{
    [Fact]
    public void 安装器始终创建当前用户桌面快捷方式()
    {
        var source = File.ReadAllText(Path.Combine(SolutionRoot, "installer", "LoomX.iss"));
        var desktopIconLine = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains("{autodesktop}\\LoomX", StringComparison.Ordinal));

        Assert.DoesNotContain("Name: \"desktopicon\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tasks: desktopicon", desktopIconLine, StringComparison.Ordinal);
        Assert.Contains("Filename: \"{app}\\LoomX.exe\"", desktopIconLine, StringComparison.Ordinal);
    }

    private static string SolutionRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
