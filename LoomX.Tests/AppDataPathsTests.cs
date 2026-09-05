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
        Assert.Equal(Path.Combine(expectedRoot, "LoomX.data-migration.lock"), AppDataPaths.DataMigrationLockPath);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OllamaHub"),
            AppDataPaths.LegacyRootDirectory);
        Assert.Equal(Path.Combine(AppDataPaths.LegacyRootDirectory, "OllamaHub.db"), AppDataPaths.LegacyDatabasePath);
        Assert.Equal(Path.Combine(AppDataPaths.LegacyRootDirectory, "Activity.db"), AppDataPaths.LegacyActivityDatabasePath);
    }
}
