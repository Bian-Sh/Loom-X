using Xunit;

namespace LoomX.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void 运行时数据路径固定在当前用户本地应用数据目录()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LoomX");

        Assert.Equal(expectedRoot, AppDataPaths.RootDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "LoomX.db"), AppDataPaths.DatabasePath);
        Assert.Equal(Path.Combine(expectedRoot, "LoomX.Activity.db"), AppDataPaths.ActivityDatabasePath);
        Assert.Equal(Path.Combine(expectedRoot, "logs"), AppDataPaths.LogDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "LoomX.db.init.lock"), AppDataPaths.ConfigurationInitializationLockPath);

        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "AppDataPaths.cs"));
        Assert.DoesNotContain("LegacyRootDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyDatabasePath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyActivityDatabasePath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataMigrationLockPath", source, StringComparison.Ordinal);
    }
}
