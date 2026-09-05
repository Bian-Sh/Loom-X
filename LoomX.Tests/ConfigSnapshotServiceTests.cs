using LoomX.Services;
using Xunit;

namespace LoomX.Tests;

public sealed class ConfigSnapshotServiceTests
{
    [Fact]
    public void FileStateReadCanOpenWhileDatabaseWriterHandleIsActive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loomx-config-state-{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(path, "database");
            using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var reader = ConfigSnapshotService.OpenFileForState(path);

            Assert.Equal("database", new StreamReader(reader).ReadToEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
