using Xunit;

namespace OllamaHub.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void 运行时数据路径固定在当前用户本地应用数据目录()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OllamaHub");

        Assert.Equal(expectedRoot, AppDataPaths.RootDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "OllamaHub.db"), AppDataPaths.DatabasePath);
        Assert.Equal(Path.Combine(expectedRoot, "logs"), AppDataPaths.LogDirectory);
    }
}
