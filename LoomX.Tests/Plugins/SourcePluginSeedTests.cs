using System.Security.Cryptography;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 预编译种子路径（spec: source-plugin-runtime「宿主常规发布后第一方插件不得重复编译」）：
/// 种子命中并复制进用户缓存重签、输入变化仍重编译、产物损坏/密钥缺失/编译身份不一致回退运行时编译。
/// 运行时加载只经用户缓存（以用户认证密钥签名），种子不构成新的受信任加载路径。
/// </summary>
public sealed class SourcePluginSeedTests
{
    private static byte[] NewSeedKey() => RandomNumberGenerator.GetBytes(32);

    private static async Task SeedAsync(SourcePluginTestPackage package, string seedRoot, byte[] seedKey)
    {
        var logger = new CompilerTestLogger();
        var seeder = new SourcePluginCompiler(seedRoot, logger);
        Assert.True(await seeder.TrySeedAsync(package.Directory, package.Manifest, seedKey, default),
            "种子生成失败：" + string.Join("；", logger.Messages));
        // 发布期工具同时写入种子密钥文件；运行期据此验证种子代际。
        Directory.CreateDirectory(seedRoot);
        await File.WriteAllBytesAsync(Path.Combine(seedRoot, SourcePluginCompiler.SeedKeyRelative), seedKey);
    }

    private static string SeedAssemblyPath(string seedRoot) =>
        Directory.EnumerateFiles(Path.Combine(seedRoot, "demo"), "LoomX.Source.*.dll", SearchOption.AllDirectories).Single();

    [Fact]
    public async Task ValidSeed_IsAdoptedIntoUserCache_AndSubsequentRunsHitNormally()
    {
        using var package = new SourcePluginTestPackage();
        var seedRoot = Path.Combine(package.Root, "seed");
        await SeedAsync(package, seedRoot, NewSeedKey());
        var logger = new IdentityCaptureLogger();
        var first = await new SourcePluginCompiler(package.CacheRoot, logger, seedRoot: seedRoot)
            .CompileAsync(package.Directory, package.Manifest);

        Assert.True(first.Success, string.Join(";", first.Diagnostics));
        Assert.True(first.CacheHit);
        Assert.Contains(logger.Messages, item => item.Contains("种子命中", StringComparison.Ordinal));
        // 产物复制进用户缓存（以用户认证密钥重签），不直接从种子目录加载。
        Assert.StartsWith(Path.GetFullPath(package.CacheRoot), Path.GetFullPath(first.AssemblyPath!), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(File.ReadAllBytes(SeedAssemblyPath(seedRoot)), File.ReadAllBytes(first.AssemblyPath!));

        // 采用后的代际由用户密钥签名：后续运行走普通缓存命中，不再接触种子。
        var second = await new SourcePluginCompiler(package.CacheRoot).CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.CacheHit);
        Assert.Equal(first.CacheKey, second.CacheKey);
        Assert.Equal(first.AssemblyPath, second.AssemblyPath);
    }

    [Fact]
    public async Task ChangedInput_RecompilesEvenWhenSeedExists()
    {
        using var package = new SourcePluginTestPackage();
        var seedRoot = Path.Combine(package.Root, "seed");
        await SeedAsync(package, seedRoot, NewSeedKey());
        var logger = new IdentityCaptureLogger();
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source + "\n// 种子之后的源码修改");

        var result = await new SourcePluginCompiler(package.CacheRoot, logger, seedRoot: seedRoot)
            .CompileAsync(package.Directory, package.Manifest);

        // 插件输入变化 → 新键 → 种子（按旧键寻址）不命中，按既有语义重新编译。
        Assert.True(result.Success, string.Join(";", result.Diagnostics));
        Assert.False(result.CacheHit);
        Assert.Contains(logger.Messages, item => item.Contains("回退运行时编译", StringComparison.Ordinal));
        Assert.True(File.Exists(result.AssemblyPath));
    }

    [Fact]
    public async Task CorruptSeedArtifact_FallsBackToRuntimeCompile_AndKeepsSeedUntouched()
    {
        using var package = new SourcePluginTestPackage();
        var seedRoot = Path.Combine(package.Root, "seed");
        await SeedAsync(package, seedRoot, NewSeedKey());
        var seedAssembly = SeedAssemblyPath(seedRoot);
        File.WriteAllText(seedAssembly, "篡改的种子产物");

        var result = await new SourcePluginCompiler(package.CacheRoot, NullLogger<SourcePluginCompiler>.Instance, seedRoot: seedRoot)
            .CompileAsync(package.Directory, package.Manifest);

        Assert.True(result.Success, string.Join(";", result.Diagnostics));
        Assert.False(result.CacheHit);
        // 确定性编译使回退产物与原种子字节一致；这里验证的是回退路径本身，而非字节差异。
        Assert.StartsWith(Path.GetFullPath(package.CacheRoot), Path.GetFullPath(result.AssemblyPath!), StringComparison.OrdinalIgnoreCase);
        // 种子代际只被隔离检查，不被删除或改写。
        Assert.Equal("篡改的种子产物", File.ReadAllText(seedAssembly));
    }

    [Fact]
    public async Task MissingSeedKeyFile_FallsBackToRuntimeCompile()
    {
        using var package = new SourcePluginTestPackage();
        var seedRoot = Path.Combine(package.Root, "seed");
        await SeedAsync(package, seedRoot, NewSeedKey());
        File.Delete(Path.Combine(seedRoot, SourcePluginCompiler.SeedKeyRelative));

        var result = await new SourcePluginCompiler(package.CacheRoot, NullLogger<SourcePluginCompiler>.Instance, seedRoot: seedRoot)
            .CompileAsync(package.Directory, package.Manifest);

        Assert.True(result.Success, string.Join(";", result.Diagnostics));
        Assert.False(result.CacheHit);
    }

    [Fact]
    public async Task CompilerIdentityMismatch_SeedIsNeverTrustedForDifferentKey()
    {
        using var package = new SourcePluginTestPackage();
        var seedRoot = Path.Combine(package.Root, "seed");
        await SeedAsync(package, seedRoot, NewSeedKey());
        var resources = SourceCacheIdentityDiagnosticsTests.TamperedContractResources();

        var result = await new SourcePluginCompiler(package.CacheRoot, NullLogger<SourcePluginCompiler>.Instance,
            () => SourcePluginReferences.ReadResources(name => resources.TryGetValue(name, out var data) ? new MemoryStream(data, writable: false) : null),
            new(), seedRoot)
            .CompileAsync(package.Directory, package.Manifest);

        // 发布期与运行期编译身份不一致（契约引用字节不同 → 键不同）→ 种子目录不命中 → 回退运行时编译。
        Assert.True(result.Success, string.Join(";", result.Diagnostics));
        Assert.False(result.CacheHit);
        Assert.NotEqual(SeedAssemblyPath(seedRoot), result.AssemblyPath);
        Assert.True(File.Exists(result.AssemblyPath));
    }

    [Fact]
    public async Task Seed_FailsSafelyForInvalidManifest()
    {
        using var package = new SourcePluginTestPackage();
        var seedRoot = Path.Combine(package.Root, "seed");
        package.SetManifest(sources: ["src/不存在.cs"]);
        var seeder = new SourcePluginCompiler(seedRoot, NullLogger<SourcePluginCompiler>.Instance);
        Assert.False(await seeder.TrySeedAsync(package.Directory, package.Manifest, NewSeedKey(), default));
        // 输入验证失败发生在任何候选创建之前，不留下半成品种子。
        Assert.False(Directory.Exists(Path.Combine(seedRoot, "demo")));
    }
}
