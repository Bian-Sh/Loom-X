using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoomX.Plugins;

namespace LoomX.Plugins.Host;

/// <summary>先写候选，再认证元数据并原子发布；不删除任何既有代际。</summary>
internal sealed class SourcePluginCache(string root, string pluginId, string key)
{
    internal string Parent { get; } = Path.Combine(Path.GetFullPath(root), pluginId);
    internal string DirectoryPath => Path.Combine(Parent, key);
    internal string AssemblyName => "LoomX.Source." + key;
    internal string PluginRelative => $"plugins/{pluginId}";
    internal string AssemblyRelative => $"{PluginRelative}/{AssemblyName}.dll";
    internal string PdbRelative => $"{PluginRelative}/{AssemblyName}.pdb";
    internal string AssemblyPath => Path.Combine(DirectoryPath, AssemblyRelative);
    internal string PdbPath => Path.Combine(DirectoryPath, PdbRelative);
    private sealed record Metadata(string InputHash, Dictionary<string, string> Files, ImmutableArray<PluginDiagnostic> Diagnostics);

    internal async Task<FileStream> LockAsync(CancellationToken token)
    {
        SourcePluginPaths.NoLinks(Parent);
        Directory.CreateDirectory(Parent);
        SourcePluginPaths.NoLinks(Parent);
        return await AcquireAsync(SourcePluginPaths.Resolve(Parent, ".compile.lock"), token).ConfigureAwait(false);
    }

    private static async Task<FileStream> AcquireAsync(string path, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            SourcePluginPaths.NoLinks(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 or 11 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, token).ConfigureAwait(false);
            }
        }
    }

    internal async Task<byte[]> AuthenticationKeyAsync(CancellationToken token)
    {
        var absoluteRoot = Path.GetFullPath(root);
        SourcePluginPaths.NoLinks(absoluteRoot);
        Directory.CreateDirectory(absoluteRoot);
        SourcePluginPaths.NoLinks(absoluteRoot);
        // 现有 Manifest 已禁止包含 ".." 的 id；控制文件与所有合法插件 id 的命名空间不相交。
        await using var held = await AcquireAsync(SourcePluginPaths.Resolve(absoluteRoot, ".cache..key.lock"), token).ConfigureAwait(false);
        var path = SourcePluginPaths.Resolve(absoluteRoot, ".cache..auth-key");
        var legacyKey = Path.Combine(absoluteRoot, ".cache-auth-key");
        if (File.Exists(legacyKey))
        {
            var bytes = await SourcePluginPaths.ReadAsync(absoluteRoot, ".cache-auth-key", token).ConfigureAwait(false);
            if (bytes.Length != 32) throw new InvalidDataException();
            // 保留原密钥与旧签名；不删除、覆盖同名插件目录或已有新控制文件。
            var destination = File.Exists(path)
                ? SourcePluginPaths.Resolve(absoluteRoot, ".cache..legacy-auth-key-" + Guid.NewGuid().ToString("N")) : path;
            File.Move(legacyKey, destination);
        }
        var legacyLock = Path.Combine(absoluteRoot, ".cache-key.lock");
        if (File.Exists(legacyLock))
        {
            SourcePluginPaths.NoLinks(legacyLock);
            if (new FileInfo(legacyLock).Length != 0) throw new InvalidDataException();
            File.Move(legacyLock, SourcePluginPaths.Resolve(absoluteRoot, ".cache..legacy-key.lock-" + Guid.NewGuid().ToString("N")));
        }
        if (!File.Exists(path))
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            await file.WriteAsync(bytes, token).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }
        var keyBytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        SourcePluginPaths.NoLinks(path);
        if (keyBytes.Length != 32) throw new InvalidDataException();
        return keyBytes;
    }

    internal async Task<ImmutableArray<PluginDiagnostic>?> ReadAsync(byte[] authenticationKey, SourcePluginInputs inputs, CancellationToken token)
    {
        try
        {
            SourcePluginPaths.NoLinks(DirectoryPath);
            var raw = await SourcePluginPaths.ReadAsync(DirectoryPath, "cache.json", token).ConfigureAwait(false);
            if (raw.Length > 1024 * 1024) return null;
            var signature = await SourcePluginPaths.ReadAsync(DirectoryPath, "cache.sig", token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(authenticationKey, raw))) return null;
            var metadata = JsonSerializer.Deserialize<Metadata>(raw);
            if (metadata is null || metadata.InputHash != key || metadata.Files is null || metadata.Diagnostics.IsDefault) return null;
            var expected = ArtifactPaths(inputs).ToArray();
            if (metadata.Files.Count != expected.Length || expected.Any(path => !metadata.Files.ContainsKey(path))) return null;
            foreach (var relative in expected)
            {
                var bytes = await SourcePluginPaths.ReadAsync(DirectoryPath, relative, token).ConfigureAwait(false);
                if (Convert.ToHexString(SHA256.HashData(bytes)) != metadata.Files[relative]) return null;
            }
            var sourcePaths = inputs.Sources.Select(item => item.RelativePath).ToHashSet(StringComparer.Ordinal);
            if (metadata.Diagnostics.Any(item => item.PluginId != pluginId || item.Stage != PluginDiagnosticStage.Compile
                || item.Severity != PluginDiagnosticSeverity.Warning || !Regex.IsMatch(item.Code, "^CS[0-9]{4}$")
                || (item.RelativePath is not null && !sourcePaths.Contains(item.RelativePath)))) return null;
            return metadata.Diagnostics.Select(item => item with { Message = SourcePluginCompiler.SafeCompileMessage }).ToImmutableArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal string CreateCandidate()
    {
        var candidate = SourcePluginPaths.Resolve(Parent, ".candidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(candidate, PluginRelative, "i18n"));
        SourcePluginPaths.NoLinks(candidate);
        return candidate;
    }

    internal async Task SealAsync(string candidate, SourcePluginInputs inputs, ImmutableArray<PluginDiagnostic> diagnostics, byte[] authenticationKey, CancellationToken token)
    {
        await File.WriteAllBytesAsync(SourcePluginPaths.Resolve(candidate, PluginRelative + "/plugin.manifest.json"), inputs.Manifest, token).ConfigureAwait(false);
        foreach (var input in inputs.Localization)
            await File.WriteAllBytesAsync(SourcePluginPaths.Resolve(candidate, PluginRelative + "/" + input.RelativePath), input.Bytes, token).ConfigureAwait(false);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in ArtifactPaths(inputs))
            files.Add(relative, Convert.ToHexString(SHA256.HashData(await SourcePluginPaths.ReadAsync(candidate, relative, token).ConfigureAwait(false))));
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new Metadata(key, files, diagnostics));
        await File.WriteAllBytesAsync(SourcePluginPaths.Resolve(candidate, "cache.json"), metadata, token).ConfigureAwait(false);
        await File.WriteAllBytesAsync(SourcePluginPaths.Resolve(candidate, "cache.sig"), HMACSHA256.HashData(authenticationKey, metadata), token).ConfigureAwait(false);
    }

    internal void Publish(string candidate)
    {
        SourcePluginPaths.NoLinks(candidate);
        SourcePluginPaths.NoLinks(DirectoryPath);
        // 只隔离损坏旧缓存，不删除；候选失败时不触碰已发布缓存。
        if (Directory.Exists(DirectoryPath))
            Directory.Move(DirectoryPath, Path.Combine(Parent, ".invalid-" + key + "-" + Guid.NewGuid().ToString("N")));
        Directory.Move(candidate, DirectoryPath);
    }

    internal void CleanupCandidate(string candidate)
    {
        var absolute = Path.GetFullPath(candidate);
        if (Path.GetDirectoryName(absolute) != Parent || !Path.GetFileName(absolute).StartsWith(".candidate-", StringComparison.Ordinal))
            throw new InvalidDataException();
        if (!Directory.Exists(absolute)) return;
        SourcePluginPaths.NoLinks(absolute);
        Directory.Delete(absolute, recursive: true);
    }

    private IEnumerable<string> ArtifactPaths(SourcePluginInputs inputs)
        => new[] { AssemblyRelative, PdbRelative, PluginRelative + "/plugin.manifest.json" }
            .Concat(inputs.Localization.Select(item => PluginRelative + "/" + item.RelativePath));
}
