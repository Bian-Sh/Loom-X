using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace LoomX.Plugins.Host;

/// <summary>发布时内嵌的固定引用与摘要；不搜索 SDK、TPA 或插件目录。</summary>
internal sealed class SourcePluginReferences
{
    internal const string Prefix = "LoomX.SourceReferences.";
    internal static readonly Lazy<SourcePluginReferences> Bundled = new(Load);
    internal ImmutableArray<MetadataReference> Metadata { get; }
    internal string Digest { get; }

    private SourcePluginReferences(ImmutableArray<MetadataReference> metadata, string digest)
        => (Metadata, Digest) = (metadata, digest);

    private static SourcePluginReferences Load()
    {
        var assembly = typeof(SourcePluginCompiler).Assembly;
        return ReadResources(assembly.GetManifestResourceStream);
    }

    internal static SourcePluginReferences ReadResources(Func<string, Stream?> resourceReader)
    {
        using var manifest = resourceReader(Prefix + "sha256")
            ?? throw new InvalidDataException();
        using var reader = new StreamReader(manifest, Encoding.UTF8);
        var text = reader.ReadToEnd();
        var lines = text.Split('\n').Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
        // 固定 10.0.9 的 167 个框架引用，加上唯一的 Plugin API 引用。
        if (lines.Length != 169 || lines[0] != "net10.0|10.0.9") throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var metadata = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('|');
            if (parts.Length != 2 || parts[0] != Path.GetFileName(parts[0]) || !parts[0].EndsWith(".dll", StringComparison.Ordinal)
                || parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit) || !names.Add(parts[0])) throw new InvalidDataException();
            using var resource = resourceReader(Prefix + parts[0])
                ?? throw new InvalidDataException();
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            var bytes = memory.ToArray();
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), parts[1], StringComparison.Ordinal))
                throw new InvalidDataException();
            metadata.Add(MetadataReference.CreateFromImage(ImmutableArray.Create(bytes), filePath: parts[0]));
        }
        if (!names.Contains("System.Runtime.dll") || !names.Contains("netstandard.dll") || !names.Contains("LoomX.Plugin.Abstractions.dll"))
            throw new InvalidDataException();
        return new(metadata.ToImmutable(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }
}
