using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class AppearanceCacheTests : IDisposable
{
    private readonly string directory;

    public AppearanceCacheTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch { }
    }

    [Fact]
    public void 缓存读写往返保持一致()
    {
        var path = Path.Combine(directory, "appearance.json");
        var snapshot = new AppearanceCacheSnapshot(true, 62, 18, "dark");

        Assert.True(AppearanceCache.TryWrite(path, snapshot));
        Assert.Equal(snapshot, AppearanceCache.TryRead(path));
    }

    [Fact]
    public void 缓存文件使用契约字段名()
    {
        var path = Path.Combine(directory, "appearance.json");

        Assert.True(AppearanceCache.TryWrite(path, new AppearanceCacheSnapshot(false, 86, 24, "system")));

        var json = File.ReadAllText(path);
        Assert.Contains("\"enabled\":false", json);
        Assert.Contains("\"opacity\":86", json);
        Assert.Contains("\"blur\":24", json);
        Assert.Contains("\"theme\":\"system\"", json);
    }

    [Fact]
    public void 缓存缺失时返回空并保持静默降级()
    {
        var path = Path.Combine(directory, "missing.json");

        Assert.Null(AppearanceCache.TryRead(path));
    }

    [Theory]
    [InlineData("{\"enabled\":true}")]
    [InlineData("{\"enabled\":\"yes\",\"opacity\":86,\"blur\":24,\"theme\":\"dark\"}")]
    [InlineData("{\"enabled\":true,\"opacity\":\"86\",\"blur\":24,\"theme\":\"dark\"}")]
    [InlineData("{\"enabled\":true,\"opacity\":86,\"blur\":24}")]
    [InlineData("not-json")]
    public void 缓存结构损坏时返回空(string content)
    {
        var path = Path.Combine(directory, "appearance.json");
        File.WriteAllText(path, content);

        Assert.Null(AppearanceCache.TryRead(path));
    }

    [Theory]
    [InlineData("{\"enabled\":true,\"opacity\":101,\"blur\":24,\"theme\":\"dark\"}")]
    [InlineData("{\"enabled\":true,\"opacity\":86,\"blur\":65,\"theme\":\"dark\"}")]
    [InlineData("{\"enabled\":true,\"opacity\":86,\"blur\":24,\"theme\":\"neon\"}")]
    public void 缓存取值非法时返回空(string content)
    {
        var path = Path.Combine(directory, "appearance.json");
        File.WriteAllText(path, content);

        Assert.Null(AppearanceCache.TryRead(path));
    }

    [Fact]
    public void 原子写入替换既有文件且不残留临时文件()
    {
        var path = Path.Combine(directory, "appearance.json");
        AppearanceCache.TryWrite(path, new AppearanceCacheSnapshot(true, 40, 10, "light"));

        Assert.True(AppearanceCache.TryWrite(path, new AppearanceCacheSnapshot(false, 90, 30, "dark")));

        Assert.Equal(new AppearanceCacheSnapshot(false, 90, 30, "dark"), AppearanceCache.TryRead(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.Single(Directory.GetFiles(directory, "*.json"));
    }

    [Fact]
    public void 主题值写入前归一化()
    {
        Assert.Equal("dark", AppearanceCache.NormalizeTheme("Dark"));
        Assert.Equal("system", AppearanceCache.NormalizeTheme(" System "));
        Assert.Null(AppearanceCache.NormalizeTheme("neon"));
        Assert.Null(AppearanceCache.NormalizeTheme(null));
    }
}
