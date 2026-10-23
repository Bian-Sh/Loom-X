using System.Security.Cryptography;
using LoomX.Plugins.Host;

// 发布期预编译种子工具（openspec: stabilize-plugin-compile-cache tasks 3.1）。
// 用与运行期完全相同的编译输入（内嵌引用集、随宿主发布的 Roslyn、固定编译选项）离线编译第一方源码插件，
// 产物与完整性元数据写入 <发布目录>\plugin-cache-seed，并用种子密钥签名；
// 运行期按缓存键寻址、复用既有认证验证后复制进用户缓存，编译身份不一致或产物损坏时回退运行时编译。
// 本工具只影响种子可用性，任何失败都不应阻塞发布本身（发布目标以非致命方式处理非零退出码除外）。
if (args.Length < 1)
{
    Console.Error.WriteLine("用法：dotnet LoomX.PluginSeedTool.dll <发布目录>");
    return 2;
}

var publishDirectory = NormalizePublishDirectory(args[0]);
var pluginsDirectory = Path.Combine(publishDirectory, "plugins");
var seedRoot = Path.Combine(publishDirectory, "plugin-cache-seed");
if (!Directory.Exists(pluginsDirectory))
{
    Console.Error.WriteLine($"发布目录缺少 plugins 目录：{pluginsDirectory}");
    return 2;
}

// MSBuild Exec 传递 $(PublishDir) 时会带尾部反斜杠（转义引号后的残留）；发布目录不会是卷根，这里保守归一化。
static string NormalizePublishDirectory(string value)
{
    var normalized = value.Trim().Trim('"').TrimEnd('\\', '/');
    return normalized.Length == 2 && normalized[1] == ':' ? normalized + Path.DirectorySeparatorChar : normalized;
}

Directory.CreateDirectory(seedRoot);
var seedKeyPath = Path.Combine(seedRoot, SourcePluginCompiler.SeedKeyRelative);
var seedKey = File.Exists(seedKeyPath) ? File.ReadAllBytes(seedKeyPath) : RandomNumberGenerator.GetBytes(32);
if (seedKey.Length != 32)
{
    Console.Error.WriteLine("种子密钥长度非法，已拒绝复用。");
    return 2;
}
await File.WriteAllBytesAsync(seedKeyPath, seedKey);

var seeder = new SourcePluginCompiler(seedRoot);
var seeded = 0;
var skipped = 0;
var failed = 0;
foreach (var directory in Directory.EnumerateDirectories(pluginsDirectory).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
{
    var manifestPath = Path.Combine(directory, ManifestParser.ManifestFileName);
    if (!File.Exists(manifestPath))
    {
        skipped++;
        continue;
    }
    var errors = new List<string>();
    var manifest = ManifestParser.Parse(File.ReadAllText(manifestPath), errors);
    if (manifest is null || manifest.Runtime?.Kind != "csharp-source")
    {
        skipped++;
        continue;
    }
    if (await seeder.TrySeedAsync(directory, manifest, seedKey, default))
    {
        seeded++;
        Console.WriteLine($"种子已生成：{manifest.Id}");
    }
    else
    {
        failed++;
        Console.Error.WriteLine($"种子生成失败：{manifest.Id}（运行期将回退运行时编译）");
    }
}

Console.WriteLine($"预编译种子完成：成功 {seeded}，跳过 {skipped}，失败 {failed}");
return failed == 0 ? 0 : 1;
