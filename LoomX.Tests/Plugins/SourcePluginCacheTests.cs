using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Plugins;

public sealed class SourcePluginCacheTests
{
    [Theory]
    [InlineData(".cache-auth-key")]
    [InlineData(".cache-key.lock")]
    [InlineData(".CACHE-AUTH-KEY")]
    [InlineData(".CACHE-KEY.LOCK")]
    public async Task ValidControlFileNames_ArePluginIdsAndDoNotPoisonFollowingPlugins(string id)
    {
        using var package = new SourcePluginTestPackage();
        SetPluginId(package, id);
        var named = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        SetPluginId(package, "demo");
        var ordinary = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var ordinaryHit = await new SourcePluginCompiler(package.CacheRoot).CompileAsync(package.Directory, package.Manifest);
        Assert.True(named.Success, $"同名插件失败：{string.Join(';', named.Diagnostics)}；后续普通插件：{string.Join(';', ordinary.Diagnostics)}");
        Assert.True(ordinary.Success, string.Join(';', ordinary.Diagnostics));
        Assert.True(ordinaryHit.Success);
        Assert.True(ordinaryHit.CacheHit);
        Assert.Equal(ordinary.CacheKey, ordinaryHit.CacheKey);
        Assert.True(Directory.Exists(Path.Combine(package.CacheRoot, id)));
        Assert.EndsWith(Path.Combine("plugins", id), Path.GetDirectoryName(named.AssemblyPath));
        SetPluginId(package, id);
        Assert.True((await package.Compiler.CompileAsync(package.Directory, package.Manifest)).CacheHit);
    }

    [Fact]
    public async Task PreviouslyPoisonedControlDirectories_ArePreservedAndOrdinaryPluginRecovers()
    {
        using var package = new SourcePluginTestPackage();
        foreach (var id in new[] { ".cache-auth-key", ".cache-key.lock" })
        {
            Directory.CreateDirectory(Path.Combine(package.CacheRoot, id));
            File.WriteAllText(Path.Combine(package.CacheRoot, id, "旧产物.txt"), "不得删除旧产物");
        }
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(result.Success, string.Join(';', result.Diagnostics));
        Assert.True((await package.Compiler.CompileAsync(package.Directory, package.Manifest)).CacheHit);
        foreach (var id in new[] { ".cache-auth-key", ".cache-key.lock" })
            Assert.Equal("不得删除旧产物", File.ReadAllText(Path.Combine(package.CacheRoot, id, "旧产物.txt")));
    }

    [Fact]
    public async Task LegacyRootKeyAndLock_MigrateWithoutLosingExistingSignedGeneration()
    {
        using var package = new SourcePluginTestPackage();
        var original = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(original.Success);
        var originalDll = File.ReadAllBytes(original.AssemblyPath!);
        var legacyKey = Path.Combine(package.CacheRoot, ".cache-auth-key");
        var currentKey = Path.Combine(package.CacheRoot, ".cache..auth-key");
        // 由当前编译器签出的真实产物模拟旧版根布局，不重写元数据或签名。
        if (File.Exists(currentKey)) File.Move(currentKey, legacyKey);
        var key = File.ReadAllBytes(legacyKey);
        File.WriteAllBytes(Path.Combine(package.CacheRoot, ".cache-key.lock"), []);
        SetPluginId(package, ".cache-auth-key");
        var named = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        SetPluginId(package, ".cache-key.lock");
        var lockNamed = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        SetPluginId(package, "demo");
        var hit = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(named.Success, string.Join(';', named.Diagnostics));
        Assert.True(lockNamed.Success, string.Join(';', lockNamed.Diagnostics));
        Assert.True(hit.CacheHit);
        Assert.Equal(original.AssemblyPath, hit.AssemblyPath);
        Assert.Equal(originalDll, File.ReadAllBytes(hit.AssemblyPath!));
        Assert.Equal(key, File.ReadAllBytes(currentKey));
        Assert.Single(Directory.EnumerateFiles(package.CacheRoot, ".cache..legacy-key.lock-*"));
    }

    private static void SetPluginId(SourcePluginTestPackage package, string id)
    {
        package.SetManifest(id: id);
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source.Replace("\"demo\"", System.Text.Json.JsonSerializer.Serialize(id), StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnchangedInputs_HitAcrossCompilerInstances_AndMirrorFourLanguages()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var second = await new SourcePluginCompiler(package.CacheRoot).CompileAsync(package.Directory, package.Manifest);
        Assert.True(first.Success);
        Assert.True(second.CacheHit);
        Assert.Equal(first.AssemblyPath, second.AssemblyPath);
        Assert.Equal(first.CacheKey, second.CacheKey);
        var directory = Path.GetDirectoryName(first.AssemblyPath)!;
        Assert.EndsWith(Path.Combine("plugins", "demo"), directory);
        Assert.True(File.Exists(Path.Combine(directory, "plugin.manifest.json")));
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(package.Directory, "i18n", culture + ".json")),
                File.ReadAllBytes(Path.Combine(directory, "i18n", culture + ".json")));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("path")]
    [InlineData("manifest")]
    [InlineData("unknownManifestField")]
    [InlineData("localization")]
    public async Task SingleInputChange_InvalidatesCache(string input)
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        switch (input)
        {
            case "source": package.Write("src/Plugin.cs", SourcePluginTestPackage.Source + "\n// 本次源码修改"); break;
            case "path":
                package.Write("src/Renamed.cs", SourcePluginTestPackage.Source);
                package.SetManifest(sources: ["src/Renamed.cs"]); break;
            case "manifest": package.SetManifest(version: "2"); break;
            case "unknownManifestField":
                var json = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Directory, "plugin.manifest.json")))!;
                json["future_metadata"] = "影响完整清单摘要";
                package.Write("plugin.manifest.json", json.ToJsonString()); break;
            case "localization": package.Write("i18n/ja-JP.json", "{\"title\":\"新文案\"}"); break;
        }
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));
        Assert.False(second.CacheHit);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
        Assert.True(File.Exists(first.AssemblyPath));
    }

    [Fact]
    public async Task EquivalentManifestFormatting_DoesNotInvalidateIdentity()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Directory, "plugin.manifest.json")))!.AsObject();
        var reverse = new JsonObject(json.Reverse().Select(item => KeyValuePair.Create(item.Key, item.Value?.DeepClone())));
        package.Write("plugin.manifest.json", reverse.ToJsonString(new() { WriteIndented = true }));
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.CacheHit);
        Assert.Equal(first.CacheKey, second.CacheKey);
    }

    [Theory]
    [InlineData("dll")]
    [InlineData("pdb")]
    [InlineData("metadata")]
    [InlineData("signature")]
    [InlineData("manifest")]
    [InlineData("localization")]
    public async Task MissingOrTamperedArtifact_RecompilesInsteadOfTrustingDirectoryName(string artifact)
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(first.Success);
        var pluginDirectory = Path.GetDirectoryName(first.AssemblyPath)!;
        var cacheDirectory = Path.Combine(package.CacheRoot, "demo", first.CacheKey!);
        switch (artifact)
        {
            case "dll": File.WriteAllText(first.AssemblyPath!, "篡改 DLL"); break;
            case "pdb": File.Delete(first.PdbPath!); break;
            case "metadata": File.WriteAllText(Path.Combine(cacheDirectory, "cache.json"), "{}"); break;
            case "signature": File.WriteAllText(Path.Combine(cacheDirectory, "cache.sig"), new string('0', 64)); break;
            case "manifest": File.WriteAllText(Path.Combine(pluginDirectory, "plugin.manifest.json"), "{}"); break;
            case "localization": File.WriteAllText(Path.Combine(pluginDirectory, "i18n", "en-US.json"), "{}"); break;
        }
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));
        Assert.False(second.CacheHit);
        Assert.Equal(first.CacheKey, second.CacheKey);
        Assert.Contains(second.Diagnostics, item => item.Code == "LX_CACHE_INVALID" && item.Severity == PluginDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task FailedUpdate_PreservesPriorArtifactsAndCleansOnlyItsCandidate()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var digest = SHA256.HashData(File.ReadAllBytes(first.AssemblyPath!));
        package.Write("src/Plugin.cs", "语法错误");
        var failed = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(failed.Success);
        Assert.Null(failed.AssemblyPath);
        Assert.Equal(digest, SHA256.HashData(File.ReadAllBytes(first.AssemblyPath!)));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(package.CacheRoot, "demo"), ".candidate-*"));
    }

    [Fact]
    public async Task DifferentPluginIds_IsolateCaches()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        package.SetManifest(id: "another");
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
        Assert.Contains(Path.Combine("another", second.CacheKey!), second.AssemblyPath!);
    }

    [Fact]
    public async Task ConcurrentCompilers_PublishOneCompleteGeneration()
    {
        using var package = new SourcePluginTestPackage();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            new SourcePluginCompiler(package.CacheRoot).CompileAsync(package.Directory, package.Manifest)));
        Assert.All(results, item => Assert.True(item.Success, string.Join(";", item.Diagnostics)));
        Assert.Single(results.Select(item => item.CacheKey).Distinct());
        Assert.Single(results, item => !item.CacheHit);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(package.CacheRoot, "demo"), ".candidate-*"));
    }
    [Fact]
    public async Task EffectiveCompilerOptions_InvalidateCacheAndChangeRealCompilationBehavior()
    {
        using var package = new SourcePluginTestPackage();
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source + "\npublic class Warning { public string Value = null; }");
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(first.Success);
        var compiler = new SourcePluginCompiler(package.CacheRoot, NullLogger<SourcePluginCompiler>.Instance,
            () => SourcePluginReferences.Bundled.Value, new(WarningsAsErrors: true));
        var second = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(second.Success);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
        Assert.Contains(second.Diagnostics, item => item.Code == "CS8625" && item.Severity == PluginDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CompilerVersionAndReferenceIdentity_ArePartOfContentAddress()
    {
        using var package = new SourcePluginTestPackage();
        var inputs = await SourcePluginInputs.ReadAsync(package.Directory, package.Manifest, default);
        var original = inputs.CacheKey("引用版本和内容摘要1", "编译器1", "选项1");
        Assert.NotEqual(original, inputs.CacheKey("引用版本和内容摘要2", "编译器1", "选项1"));
        Assert.NotEqual(original, inputs.CacheKey("引用版本和内容摘要1", "编译器2", "选项1"));
        Assert.NotEqual(original, inputs.CacheKey("引用版本和内容摘要1", "编译器1", "选项2"));
    }

    [Fact]
    public async Task ActualContractReferenceContentChange_InvalidatesCache()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var resources = SourcePluginCompilerTests.ReferenceResources();
        var contractName = SourcePluginReferences.Prefix + "LoomX.Plugin.Abstractions.dll";
        // PE 允许尾部附加字节：保持有效契约语义，真实改变引用内容。
        var bytes = File.ReadAllBytes(typeof(ILoomXPlugin).Assembly.Location).Concat(new byte[] { 0 }).ToArray();
        Assert.NotEqual(SHA256.HashData(resources[contractName]), SHA256.HashData(bytes));
        resources[contractName] = bytes;
        var manifestName = SourcePluginReferences.Prefix + "sha256";
        var lines = Encoding.UTF8.GetString(resources[manifestName]).Split('\n').Select(line =>
            line.StartsWith("LoomX.Plugin.Abstractions.dll|") ? "LoomX.Plugin.Abstractions.dll|" + Convert.ToHexString(SHA256.HashData(bytes)) : line);
        resources[manifestName] = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        var compiler = new SourcePluginCompiler(package.CacheRoot, NullLogger<SourcePluginCompiler>.Instance,
            () => SourcePluginReferences.ReadResources(name => resources.TryGetValue(name, out var data) ? new MemoryStream(data, writable: false) : null), new());
        var second = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));
        Assert.False(second.CacheHit);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
    }

    [Fact]
    public async Task RecompileSameInputs_ProducesByteIdenticalDllAndPortablePdb()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var dll = File.ReadAllBytes(first.AssemblyPath!);
        var pdb = File.ReadAllBytes(first.PdbPath!);
        File.Delete(first.PdbPath!);
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success);
        Assert.Equal(dll, File.ReadAllBytes(second.AssemblyPath!));
        Assert.Equal(pdb, File.ReadAllBytes(second.PdbPath!));
    }

    [Fact]
    public async Task ForgedArtifactHashesWithoutSignature_AreRejected()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        var cache = Path.Combine(package.CacheRoot, "demo", first.CacheKey!);
        var metadataPath = Path.Combine(cache, "cache.json");
        File.WriteAllText(first.AssemblyPath!, "篡改正文");
        var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
        var relative = Path.GetRelativePath(cache, first.AssemblyPath!).Replace('\\', '/');
        metadata["Files"]![relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.AssemblyPath!)));
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success);
        Assert.False(second.CacheHit);
        Assert.Contains(second.Diagnostics, item => item.Code == "LX_CACHE_INVALID");
    }

    [Fact]
    public async Task LocalizationGeneration_UsesExistingCatalogAndEmbeddedEnglishFallback()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        package.Write("i18n/en-US.json", "{\"title\":\"Updated English\"}");
        var second = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
        static string ReadTitle(string path)
        {
            var context = new AssemblyLoadContext("文案代际测试", isCollectible: true);
            try
            {
                using var file = File.OpenRead(path);
                var assembly = context.LoadFromStream(file);
                // 从流加载无 Location，仅保留 DLL，验证既有 Catalog 的嵌入 en-US 回落。
                return PluginTextCatalog.For(assembly.GetType("Demo.Plugin")!, "demo", "fr-FR").Get("title");
            }
            finally { context.Unload(); }
        }
        Assert.Equal("演示", ReadTitle(first.AssemblyPath!));
        Assert.Equal("Updated English", ReadTitle(second.AssemblyPath!));
    }
    [Fact]
    public void CompilerIdentity_CoversBothRoslynComponents()
    {
        foreach (var assembly in new[] { typeof(Microsoft.CodeAnalysis.Compilation).Assembly, typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly })
        {
            Assert.Contains(assembly.FullName!, SourcePluginCompiler.CompilerIdentity);
            Assert.Contains(assembly.ManifestModule.ModuleVersionId.ToString(), SourcePluginCompiler.CompilerIdentity);
        }
    }}
