using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text.Json;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Security.Cryptography;

namespace LoomX.Tests.Plugins;

public sealed class SourcePluginCompilerTests
{
    [Fact]
    public async Task Compile_EmitsLoadablePluginAndPortablePdb_OnlyExplicitSources()
    {
        using var package = new SourcePluginTestPackage();
        package.Write("未声明.cs", "这不是 C#，不能被隐式编译");
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(result.Success, string.Join(";", result.Diagnostics));
        Assert.False(result.CacheHit);
        Assert.True(File.Exists(result.AssemblyPath));
        using var pdb = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(result.PdbPath!));
        Assert.NotEmpty(pdb.GetMetadataReader().Documents);
        var context = new AssemblyLoadContext("源码编译测试", isCollectible: true);
        try
        {
            using var assemblyStream = File.OpenRead(result.AssemblyPath!);
            var assembly = context.LoadFromStream(assemblyStream);
            var plugin = Assert.IsAssignableFrom<ILoomXPlugin>(Activator.CreateInstance(assembly.GetType("Demo.Plugin")!));
            Assert.Equal(package.Manifest.Id, plugin.Id);
            Assert.Empty(plugin.CreateExtensions());
            Assert.Contains(assembly.GetName().Name + ".i18n.en-US.json", assembly.GetManifestResourceNames());
        }
        finally { context.Unload(); }
    }
    [Theory]
    [InlineData("../外部.cs")]
    [InlineData("/外部.cs")]
    [InlineData("C:外部.cs")]
    [InlineData("C:/外部.cs")]
    [InlineData("\\\\server/share/外部.cs")]
    [InlineData("\\\\?/C:/外部.cs")]
    [InlineData("src/../Plugin.cs")]
    [InlineData("src/CON.cs")]
    public async Task UnsafeSource_IsRejectedWithoutEchoingExternalPath(string path)
    {
        using var package = new SourcePluginTestPackage();
        var manifest = package.Manifest with { Runtime = package.Manifest.Runtime! with { Sources = [path] } };
        var result = await package.Compiler.CompileAsync(package.Directory, manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "LX_PATH_INVALID");
        Assert.All(result.Diagnostics, item => Assert.Null(item.RelativePath));
        Assert.DoesNotContain(path, string.Join(";", result.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task MissingSource_ReturnsSafeSourceReadDiagnostic()
    {
        using var package = new SourcePluginTestPackage();
        package.SetManifest(sources: ["src/不存在.cs"]);
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Stage == PluginDiagnosticStage.SourceRead && item.RelativePath == "src/不存在.cs");
    }

    [Fact]
    public async Task JunctionSource_IsRejectedBeforeReadingOutsidePackage()
    {
        using var package = new SourcePluginTestPackage();
        var outside = Path.Combine(package.Root, "外部");
        System.IO.Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "Secret.cs"), "外部安全标记，不得读取");
        var junction = Path.Combine(package.Directory, "link");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction); start.ArgumentList.Add(outside);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        package.SetManifest(sources: ["link/Secret.cs"]);
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "LX_PATH_INVALID");
        Assert.DoesNotContain("外部安全标记", string.Join(";", result.Diagnostics));
    }

    [Fact]
    public async Task ThirdPartyReference_IsNotAvailableEvenIfHostHasLoadedIt()
    {
        using var package = new SourcePluginTestPackage();
        var thirdParty = typeof(Xunit.Assert).Assembly;
        Assert.Equal("xunit.assert", thirdParty.GetName().Name);
        Assert.Same(thirdParty, System.Reflection.Assembly.Load(thirdParty.GetName()));
        Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), assembly => ReferenceEquals(assembly, thirdParty));
        var dllPath = Path.Combine(package.Directory, "xunit.assert.dll");
        File.Copy(thirdParty.Location, dllPath);
        Assert.Equal(thirdParty.GetName().FullName, System.Reflection.AssemblyName.GetAssemblyName(dllPath).FullName);
        package.Write("依赖.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.3\" /></ItemGroup></Project>");
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source.Replace("using System.Collections.Generic;", "using System.Collections.Generic;\nusing Xunit;")
            + "\npublic class Extra { public void Check() => Assert.True(true); }");
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "CS0246" && item.Stage == PluginDiagnosticStage.Compile);
        Assert.DoesNotContain(result.Diagnostics, item => item.Message.Contains(thirdParty.Location, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyntaxDiagnostics_UsePhysicalRelativeLineColumnAndNeverEchoSource()
    {
        using var package = new SourcePluginTestPackage();
        package.Write("src/Plugin.cs", "#line 900 \"C:/外部/凭据.txt\"\nclass Broken { void M() { var 凭据正文SECRET = ; } }");
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Code == "CS1525"));
        Assert.Equal("src/Plugin.cs", diagnostic.RelativePath);
        Assert.Equal(2, diagnostic.Line);
        Assert.True(diagnostic.Column > 0);
        Assert.DoesNotContain("SECRET", string.Join(";", result.Diagnostics));
        Assert.DoesNotContain("C:/", string.Join(";", result.Diagnostics));
    }

    [Fact]
    public async Task MissingLocalization_IsSafeFailure()
    {
        using var package = new SourcePluginTestPackage();
        File.Delete(Path.Combine(package.Directory, "i18n/en-US.json"));
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.RelativePath == "i18n/en-US.json");
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var package = new SourcePluginTestPackage();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => package.Compiler.CompileAsync(package.Directory, package.Manifest, cancellation.Token));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("tampered")]
    [InlineData("header")]
    [InlineData("omittedContract")]
    [InlineData("duplicate")]
    public async Task InvalidBundledReferences_ReturnSafeFailureWithoutSdkFallback(string kind)
    {
        using var package = new SourcePluginTestPackage();
        var resources = ReferenceResources();
        var manifestName = SourcePluginReferences.Prefix + "sha256";
        var text = Encoding.UTF8.GetString(resources[manifestName]);
        switch (kind)
        {
            case "missing": resources.Remove(SourcePluginReferences.Prefix + "System.Runtime.dll"); break;
            case "tampered": resources[SourcePluginReferences.Prefix + "System.Runtime.dll"] = [0, 1, 2]; break;
            case "header": resources[manifestName] = Encoding.UTF8.GetBytes(text.Replace("net10.0|10.0.9", "net99.0|99.0.0")); break;
            case "omittedContract": resources[manifestName] = Encoding.UTF8.GetBytes(string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("LoomX.Plugin.Abstractions.dll|")))); break;
            case "duplicate": resources[manifestName] = Encoding.UTF8.GetBytes(text + text.Split('\n').First(line => line.StartsWith("System.Runtime.dll|")) + "\n"); break;
        }
        var compiler = new SourcePluginCompiler(package.CacheRoot, NullLogger<SourcePluginCompiler>.Instance,
            () => SourcePluginReferences.ReadResources(name => resources.TryGetValue(name, out var bytes) ? new MemoryStream(bytes, writable: false) : null), new());
        var result = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "LX_REFERENCES_INVALID" && item.Stage == PluginDiagnosticStage.Compile);
        Assert.Null(result.AssemblyPath);
    }

    [Fact]
    public async Task LogsAndDiagnostics_DoNotLeakDirectivePayloadOrSourceLiterals()
    {
        using var package = new SourcePluginTestPackage();
        const string secret = "sk-源码中敏感凭据正文标记";
        package.Write("src/Plugin.cs", "#error " + secret);
        var logger = new CompilerTestLogger();
        var result = await new SourcePluginCompiler(package.CacheRoot, logger).CompileAsync(package.Directory, package.Manifest);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Code == "CS1029");
        Assert.DoesNotContain(secret, string.Join(";", result.Diagnostics));
        Assert.DoesNotContain(secret, string.Join(";", logger.Messages));
        Assert.Contains(logger.Messages, item => item.Contains("编译开始"));
        Assert.Contains(logger.Messages, item => item.Contains("编译失败"));
    }

    internal static Dictionary<string, byte[]> ReferenceResources()
    {
        var assembly = typeof(SourcePluginCompiler).Assembly;
        return assembly.GetManifestResourceNames().Where(name => name.StartsWith(SourcePluginReferences.Prefix, StringComparison.Ordinal)).ToDictionary(name => name, name =>
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }
}

internal sealed class CompilerTestLogger : ILogger<SourcePluginCompiler>
{
    internal List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception) + exception?.ToString());
}

internal sealed class SourcePluginTestPackage : IDisposable
{
    internal const string Source = """
        using System.Collections.Generic;
        using LoomX.Plugins;
        namespace Demo;
        public sealed class Plugin : ILoomXPlugin
        {
            public string Id => "demo";
            public void Initialize(PluginInitializationContext context) { }
            public IEnumerable<IPipelineExtension> CreateExtensions() => System.Array.Empty<IPipelineExtension>();
        }
        """;
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "LoomX-源码编译测试-" + Guid.NewGuid().ToString("N"));
    internal string Directory => Path.Combine(Root, "package");
    internal string CacheRoot => Path.Combine(Root, "cache");
    internal PluginManifest Manifest { get; private set; } = null!;
    internal SourcePluginCompiler Compiler { get; }
    internal SourcePluginTestPackage()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write("src/Plugin.cs", Source);
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            Write($"i18n/{culture}.json", "{\"title\":\"演示\"}");
        SetManifest();
        Compiler = new SourcePluginCompiler(CacheRoot);
    }
    internal void SetManifest(string id = "demo", string version = "1", string[]? sources = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            id, version, runtime = new { kind = "csharp-source", entry_type = "Demo.Plugin", sources = sources ?? ["src/Plugin.cs"] },
            capabilities = Array.Empty<string>(), extensions = Array.Empty<string>(),
        });
        Write("plugin.manifest.json", json);
        var errors = new List<string>();
        Manifest = ManifestParser.Parse(json, errors)!;
        Assert.NotNull(Manifest);
        Assert.Empty(errors);
    }
    internal void Write(string relative, string text)
    {
        var path = Path.Combine(Directory, relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
    }
    public void Dispose()
    {
        // 删除前验证绝对路径，只清理本夹具明确创建的独立临时根。
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(Root);
        Assert.StartsWith(temp, root, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("LoomX-源码编译测试-", Path.GetFileName(root), StringComparison.Ordinal);
        var junction = Path.Combine(root, "package", "link");
        if (System.IO.Directory.Exists(junction) && (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0)
            System.IO.Directory.Delete(junction, recursive: false);
        if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
    }
}