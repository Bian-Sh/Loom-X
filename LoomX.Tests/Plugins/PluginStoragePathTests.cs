using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class PluginStoragePathTests : SqliteBridgeFixture
{
    [Fact]
    public async Task 根目录包含各插件目录且自定义路径不会产生第二个库()
    {
        await using var scope = CreateStorage();
        Assert.False(Directory.Exists(Root));
        await using (var db = await scope.Storage.OpenSqliteAsync("cache/models.db")) await db.ExecuteAsync(Sql("CREATE TABLE data(x)"));
        await using (var db = await scope.Storage.OpenSqliteAsync("cache\\models.db")) Assert.Equal(1, (await db.QuerySingleAsync(Sql("SELECT count(*) FROM sqlite_master WHERE name='data'")))![0].Integer);
        Assert.Single(Directory.GetFiles(Root, "*.db", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(Root, "example.plugin", "cache", "models.db")));
        await using var other = CreateStorage("other.plugin");
        await using var independent = await other.Storage.OpenSqliteAsync("cache/models.db");
        Assert.Equal(0, (await independent.QuerySingleAsync(Sql("SELECT count(*) FROM sqlite_master WHERE name='data'")))![0].Integer);
    }

    [Theory]
    [InlineData("")][InlineData(" ")][InlineData("../other/data.db")][InlineData("cache/../data.db")]
    [InlineData("/tmp/data.db")][InlineData("C:/outside.db")][InlineData("C:relative.db")]
    [InlineData("\\\\server\\share\\data.db")][InlineData("\\\\?\\C:\\data.db")][InlineData("\\\\.\\pipe\\data")]
    [InlineData("file:outside.db?mode=rw")][InlineData(":memory:")][InlineData("cache/")]
    [InlineData("cache/./data.db")][InlineData("data.db:stream")][InlineData("data.db.")][InlineData("CON.db")]
    public async Task 危险路径不访问文件(string path)
    {
        await using var scope = CreateStorage();
        await Error(PluginSqlError.InvalidPath, () => scope.Storage.OpenSqliteAsync(path).AsTask());
        Assert.False(Directory.Exists(Root));
    }

    [Theory]
    [InlineData("../other")][InlineData("bad/id")][InlineData("C:plugin")][InlineData("")][InlineData("CON")]
    public void 绑定身份不得是路径(string id)
    {
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => CreateStorage(id));
        Assert.IsType<PluginSqlException>(ex.InnerException);
        Assert.Equal(PluginSqlError.InvalidPath, ((PluginSqlException)ex.InnerException!).Error);
    }

    [Fact]
    public void 不允许把磁盘根解释为插件数据根()
    {
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => CreateStorage(root: Path.GetPathRoot(Root)));
        Assert.Equal(PluginSqlError.InvalidPath, Assert.IsType<PluginSqlException>(ex.InnerException).Error);
    }

    [Theory]
    [InlineData("NUL .db")][InlineData("COM¹.db")][InlineData("LPT².db")]
    public async Task Windows保留设备别名在文件系统访问前拒绝(string path)
    {
        await using var scope = CreateStorage();
        var paths = scope.Storage.GetType().GetField("paths", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope.Storage)!;
        var resolve = paths.GetType().GetMethod("Resolve", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => resolve.Invoke(paths, [path]));
        Assert.Equal(PluginSqlError.InvalidPath, Assert.IsType<PluginSqlException>(ex.InnerException).Error);
        Assert.False(Directory.Exists(Root));
    }

    [Theory]
    [InlineData("example_plugin")][InlineData("示例插件")][InlineData("Example.Plugin")]
    public async Task 安全绑定身份不强制改名或创建额外目录(string id)
    {
        await using var scope = CreateStorage(id);
        await using var db = await scope.Storage.OpenSqliteAsync("identity.db");
        Assert.True(File.Exists(Path.Combine(Root, id, "identity.db")));
    }

    [Fact]
    public void 整个AppData不是插件数据根()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => CreateStorage(root: appData));
        Assert.Equal(PluginSqlError.InvalidPath, Assert.IsType<PluginSqlException>(ex.InnerException).Error);
    }

    [Fact]
    public async Task Windows大小写别名仍复用同一文件而不静默新建库()
    {
        await using var scope = CreateStorage("Example.Plugin");
        await using (var first = await scope.Storage.OpenSqliteAsync("Cache/Models.db")) await first.ExecuteAsync(Sql("CREATE TABLE marker(x)"));
        await using var alias = CreateStorage("example.plugin");
        await using var second = await alias.Storage.OpenSqliteAsync("cache/models.db");
        Assert.Equal(1, (await second.QuerySingleAsync(Sql("SELECT count(*) FROM sqlite_master WHERE name='marker'")))![0].Integer);
        Assert.Single(Directory.GetFiles(Root, "*.db", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("root")][InlineData("parent")][InlineData("file")][InlineData("journal")]
    public async Task Junction和文件符号链接均不能绕过路径边界(string position)
    {
        Directory.CreateDirectory(Root);
        var outside = Path.Combine(Root, "outside"); Directory.CreateDirectory(outside);
        var plugin = Path.Combine(Root, "example.plugin");
        if (position == "root") Directory.CreateSymbolicLink(plugin, outside);
        else
        {
            Directory.CreateDirectory(plugin);
            if (position == "parent") Directory.CreateSymbolicLink(Path.Combine(plugin, "cache"), outside);
            else
            {
                var file = Path.Combine(outside, "external.db"); await File.WriteAllBytesAsync(file, []);
                File.CreateSymbolicLink(Path.Combine(plugin, position == "journal" ? "data.db-journal" : "data.db"), file);
            }
        }
        await using var scope = CreateStorage();
        await Error(PluginSqlError.InvalidPath, () => scope.Storage.OpenSqliteAsync(position == "parent" ? "cache/data.db" : "data.db").AsTask());
        Assert.DoesNotContain(Directory.GetFiles(outside), f => new FileInfo(f).Length > 0);
    }

    [Fact]
    public async Task 打开之后出现侧车链接也会在下一操作拒绝()
    {
        await using var scope = CreateStorage();
        await using var db = await scope.Storage.OpenSqliteAsync("cache/data.db");
        var external = Path.Combine(Root, "external.db"); await File.WriteAllBytesAsync(external, []);
        File.CreateSymbolicLink(Path.Combine(Root, "example.plugin", "cache", "data.db-journal"), external);
        await Error(PluginSqlError.InvalidPath, () => db.QuerySingleAsync(Sql("SELECT 1")).AsTask());
        Assert.Equal(0, new FileInfo(external).Length);
    }

    [Theory]
    [InlineData("目录已创建")][InlineData("连接已打开")]
    public async Task 首审M2_受控置换发生在创建或打开与后置检查之间仍拒绝(string stage)
    {
        await using var scope = CreateStorage();
        Directory.CreateDirectory(Root);
        var outside = Path.Combine(Root, "outside"); Directory.CreateDirectory(outside);
        var external = Path.Combine(outside, "untouched.db"); await File.WriteAllBytesAsync(external, []);
        var parent = Path.Combine(Root, "example.plugin", "cache"); var database = Path.Combine(parent, "data.db");
        var paths = scope.Storage.GetType().GetField("paths", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope.Storage)!;
        var checkpoint = paths.GetType().GetProperty("Checkpoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.True(checkpoint is not null, "缺少每实例受控路径检查点，无法验证创建/打开与后置检查之间的置换。");
        var replacements = 0;
        checkpoint!.SetValue(paths, (Action<string, string>)((current, path) =>
        {
            if (current != stage || path != (stage == "目录已创建" ? parent : database)) return;
            replacements++;
            if (stage == "目录已创建")
            {
                Assert.StartsWith(Root + Path.DirectorySeparatorChar, path);
                Directory.Move(path, path + "-original"); Directory.CreateSymbolicLink(path, outside);
            }
            else File.CreateSymbolicLink(path + "-journal", external);
        }));
        await Error(PluginSqlError.InvalidPath, () => scope.Storage.OpenSqliteAsync("cache/data.db").AsTask());
        Assert.Equal(1, replacements);
        Assert.Equal(0, new FileInfo(external).Length);
        Assert.Single(Directory.GetFiles(outside));
        if (stage == "连接已打开")
        {
            using var exclusive = new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    [Fact]
    public async Task Windows真实Junction父目录不能逃逸()
    {
        Directory.CreateDirectory(Path.Combine(Root, "example.plugin"));
        var outside = Path.Combine(Root, "outside"); Directory.CreateDirectory(outside);
        var junction = Path.Combine(Root, "example.plugin", "junction");
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/J"); info.ArgumentList.Add(junction); info.ArgumentList.Add(outside);
        using var process = System.Diagnostics.Process.Start(info)!;
        await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        try
        {
            await using var scope = CreateStorage();
            await Error(PluginSqlError.InvalidPath, () => scope.Storage.OpenSqliteAsync("junction/data.db").AsTask());
            Assert.Empty(Directory.GetFiles(outside));
        }
        finally { Directory.Delete(junction); }
    }
}
