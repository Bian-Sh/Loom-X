using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 缓存身份诊断（spec: source-plugin-runtime「编译缓存身份变化必须可诊断」）：
/// 未命中时日志指明翻转的组成部分并输出未变化项对照，命中时记录命中事件；身份摘要只入缓存元数据与日志，不影响正确性。
/// </summary>
public sealed class SourceCacheIdentityDiagnosticsTests
{
    [Fact]
    public async Task SealedMetadata_RecordsAllIdentityComponents()
    {
        using var package = new SourcePluginTestPackage();
        var result = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(result.Success, string.Join(";", result.Diagnostics));

        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(MetadataPath(package, result.CacheKey!)))!;
        var components = metadata["Components"]!.AsObject();
        Assert.Equal(["references", "compiler", "options", "inputs"], components.Select(item => item.Key).ToArray());
        Assert.Matches("^[0-9A-F]{16}$", components["references"]!.GetValue<string>());
        Assert.Matches("^[0-9A-F]{16}$", components["compiler"]!.GetValue<string>());
        Assert.Matches("^[0-9A-F]{16}$", components["inputs"]!.GetValue<string>());
        Assert.Contains("optimize=True", components["options"]!.GetValue<string>());
    }

    [Fact]
    public async Task FirstCompile_LogsMissWithoutStoredRecord_AndHitLogsComponents()
    {
        using var package = new SourcePluginTestPackage();
        var logger = new IdentityCaptureLogger();
        var compiler = new SourcePluginCompiler(package.CacheRoot, logger);
        var first = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(first.Success, string.Join(";", first.Diagnostics));

        var missMessage = logger.Message("无既有身份记录");
        Assert.NotNull(missMessage);
        Assert.Equal("demo", logger.Field(missMessage!, "PluginId"));

        var hit = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(hit.CacheHit);
        var hitMessage = logger.Message("编译缓存命中");
        Assert.NotNull(hitMessage);
        // 命中事件携带完整身份组成，与未命中日志可直接对照。
        Assert.Equal(logger.Field(missMessage!, "Inputs"), logger.Field(hitMessage!, "Inputs"));
        Assert.Equal(logger.Field(missMessage!, "References"), logger.Field(hitMessage!, "References"));
    }

    [Fact]
    public async Task SourceChange_LogsFlippedInputsOnly_AndUnchangedRest()
    {
        using var package = new SourcePluginTestPackage();
        var logger = new IdentityCaptureLogger();
        var compiler = new SourcePluginCompiler(package.CacheRoot, logger);
        Assert.True((await compiler.CompileAsync(package.Directory, package.Manifest)).Success);
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source + "\n// 本次源码修改");
        var second = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));

        var miss = logger.Message("身份对比");
        Assert.NotNull(miss);
        Assert.Equal("inputs", logger.Field(miss!, "Flipped"));
        // 翻转项与既有记录不同，未变化项同时输出且一致，供对照。
        Assert.NotEqual(logger.Field(miss!, "Inputs"), logger.Field(miss!, "StoredInputs"));
        Assert.Equal(logger.Field(miss!, "References"), logger.Field(miss!, "StoredReferences"));
        Assert.Equal(logger.Field(miss!, "Compiler"), logger.Field(miss!, "StoredCompiler"));
        Assert.Equal(logger.Field(miss!, "Options"), logger.Field(miss!, "StoredOptions"));
    }

    [Fact]
    public async Task ContractReferenceChange_LogsFlippedReferencesOnly()
    {
        using var package = new SourcePluginTestPackage();
        var logger = new IdentityCaptureLogger();
        Assert.True((await new SourcePluginCompiler(package.CacheRoot, logger).CompileAsync(package.Directory, package.Manifest)).Success);
        var resources = TamperedContractResources();
        var compiler = new SourcePluginCompiler(package.CacheRoot, logger, () => SourcePluginReferences.ReadResources(
            name => resources.TryGetValue(name, out var data) ? new MemoryStream(data, writable: false) : null), new());
        var second = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));

        var miss = logger.Message("身份对比");
        Assert.NotNull(miss);
        Assert.Equal("references", logger.Field(miss!, "Flipped"));
        Assert.NotEqual(logger.Field(miss!, "References"), logger.Field(miss!, "StoredReferences"));
        Assert.Equal(logger.Field(miss!, "Inputs"), logger.Field(miss!, "StoredInputs"));
        Assert.Equal(logger.Field(miss!, "Compiler"), logger.Field(miss!, "StoredCompiler"));
        Assert.Equal(logger.Field(miss!, "Options"), logger.Field(miss!, "StoredOptions"));
    }

    [Fact]
    public async Task CompilerOptionsChange_LogsFlippedOptionsOnly()
    {
        using var package = new SourcePluginTestPackage();
        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source + "\npublic class Marker { }");
        var logger = new IdentityCaptureLogger();
        Assert.True((await new SourcePluginCompiler(package.CacheRoot, logger).CompileAsync(package.Directory, package.Manifest)).Success);
        var strict = new SourcePluginCompiler(package.CacheRoot, logger,
            () => SourcePluginReferences.Bundled.Value, new(WarningsAsErrors: true));
        var second = await strict.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));

        var miss = logger.Message("身份对比");
        Assert.NotNull(miss);
        Assert.Equal("options", logger.Field(miss!, "Flipped"));
        Assert.NotEqual(logger.Field(miss!, "Options"), logger.Field(miss!, "StoredOptions"));
        Assert.Equal(logger.Field(miss!, "References"), logger.Field(miss!, "StoredReferences"));
        Assert.Equal(logger.Field(miss!, "Compiler"), logger.Field(miss!, "StoredCompiler"));
        Assert.Equal(logger.Field(miss!, "Inputs"), logger.Field(miss!, "StoredInputs"));
    }

    [Fact]
    public async Task LegacySealWithoutComponents_IsReportedAsUnknownInsteadOfGuessing()
    {
        using var package = new SourcePluginTestPackage();
        var logger = new IdentityCaptureLogger();
        var compiler = new SourcePluginCompiler(package.CacheRoot, logger);
        var first = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(first.Success, string.Join(";", first.Diagnostics));
        // 模拟旧代际元数据：去掉 Components 并用根认证密钥重新签名，签名有效但身份摘要缺失。
        var metadataPath = MetadataPath(package, first.CacheKey!);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!;
        metadata.AsObject().Remove("Components");
        var raw = Encoding.UTF8.GetBytes(metadata.ToJsonString());
        await File.WriteAllBytesAsync(metadataPath, raw);
        var authenticationKey = await File.ReadAllBytesAsync(Path.Combine(package.CacheRoot, ".cache..auth-key"));
        await File.WriteAllBytesAsync(Path.Combine(package.CacheRoot, "demo", first.CacheKey!, "cache.sig"),
            HMACSHA256.HashData(authenticationKey, raw));

        package.Write("src/Plugin.cs", SourcePluginTestPackage.Source + "\n// 触发新身份");
        var second = await compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.Success, string.Join(";", second.Diagnostics));

        Assert.NotNull(logger.Message("无既有身份记录"));
        Assert.Null(logger.Message("身份对比"));
    }

    [Fact]
    public async Task TamperedIdentityComponents_DoNotAlterCacheKeyOrOutcome()
    {
        using var package = new SourcePluginTestPackage();
        var first = await package.Compiler.CompileAsync(package.Directory, package.Manifest);
        Assert.True(first.Success, string.Join(";", first.Diagnostics));
        // 篡改元数据中的 Components（同时重签）只影响诊断对比，不影响命中判定。
        var metadataPath = MetadataPath(package, first.CacheKey!);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!;
        metadata["Components"]!["references"] = "FFFFFFFFFFFFFFFF";
        var raw = Encoding.UTF8.GetBytes(metadata.ToJsonString());
        await File.WriteAllBytesAsync(metadataPath, raw);
        var authenticationKey = await File.ReadAllBytesAsync(Path.Combine(package.CacheRoot, ".cache..auth-key"));
        await File.WriteAllBytesAsync(Path.Combine(package.CacheRoot, "demo", first.CacheKey!, "cache.sig"),
            HMACSHA256.HashData(authenticationKey, raw));

        var second = await new SourcePluginCompiler(package.CacheRoot).CompileAsync(package.Directory, package.Manifest);
        Assert.True(second.CacheHit);
        Assert.Equal(first.CacheKey, second.CacheKey);
    }

    private static string MetadataPath(SourcePluginTestPackage package, string cacheKey) =>
        Path.Combine(package.CacheRoot, "demo", cacheKey, "cache.json");

    private static Dictionary<string, byte[]> TamperedContractResources()
    {
        var resources = SourcePluginCompilerTests.ReferenceResources();
        var contractName = SourcePluginReferences.Prefix + "LoomX.Plugin.Abstractions.dll";
        // PE 允许尾部附加字节：保持有效契约语义，真实改变引用内容。
        var bytes = File.ReadAllBytes(typeof(LoomX.Plugins.ILoomXPlugin).Assembly.Location).Concat(new byte[] { 0 }).ToArray();
        resources[contractName] = bytes;
        var manifestName = SourcePluginReferences.Prefix + "sha256";
        var lines = Encoding.UTF8.GetString(resources[manifestName]).Split('\n').Select(line =>
            line.StartsWith("LoomX.Plugin.Abstractions.dll|") ? "LoomX.Plugin.Abstractions.dll|" + Convert.ToHexString(SHA256.HashData(bytes)) : line);
        resources[manifestName] = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return resources;
    }
}

/// <summary>捕获结构化日志字段，用于断言身份组成部分而非渲染文本。</summary>
internal sealed class IdentityCaptureLogger : ILogger<SourcePluginCompiler>
{
    private readonly List<(string Message, Dictionary<string, object?> State)> entries = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;

    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = new Dictionary<string, object?>();
        if (state is IReadOnlyList<KeyValuePair<string, object?>> list)
            foreach (var pair in list) values[pair.Key] = pair.Value;
        entries.Add((formatter(state, exception), values));
    }

    internal string? Message(string contains) =>
        entries.Select(item => item.Message).LastOrDefault(message => message.Contains(contains, StringComparison.Ordinal));

    internal string? Field(string message, string field)
    {
        var entry = entries.LastOrDefault(item => item.Message == message);
        if (entry.State.TryGetValue(field, out var value) && value is not null) return value.ToString();
        throw new Xunit.Sdk.XunitException($"日志未携带结构化字段 {field}：{message}");
    }
}
