using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LoomX.Plugins;

namespace LoomX.Plugins.Host;

internal sealed record SourcePluginInput(string RelativePath, byte[] Bytes);
internal sealed record SourcePluginInputs(byte[] Manifest, ImmutableArray<SourcePluginInput> Sources, ImmutableArray<SourcePluginInput> Localization)
{
    internal static readonly string[] Cultures = ["zh-CN", "zh-TW", "ja-JP", "en-US"];

    internal static async Task<SourcePluginInputs> ReadAsync(string directory, PluginManifest manifest, CancellationToken token)
    {
        if (manifest.Runtime is not { Kind: "csharp-source", Sources.Count: > 0 } runtime || string.IsNullOrWhiteSpace(runtime.EntryType))
            throw Error(manifest.Id, PluginDiagnosticStage.Manifest, "LX_MANIFEST_INVALID", "清单缺少有效的源码加载入口。");
        string id;
        try
        {
            id = SourcePluginPaths.Relative(manifest.Id);
            if (id.Contains('/')) throw new InvalidDataException();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            throw Error("", PluginDiagnosticStage.Manifest, "LX_MANIFEST_INVALID", "插件标识不是安全的目录名称。");
        }
        var relativePaths = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in runtime.Sources.ToArray())
        {
            try
            {
                var relative = SourcePluginPaths.Relative(source);
                if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !unique.Add(relative)) throw new InvalidDataException();
                SourcePluginPaths.Resolve(directory, relative);
                relativePaths.Add(relative);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException)
            {
                throw Error(id, PluginDiagnosticStage.SourceRead, "LX_PATH_INVALID", "源码路径不在允许的插件包边界内。");
            }
        }
        byte[] canonical;
        try
        {
            var bytes = await SourcePluginPaths.ReadAsync(directory, ManifestParser.ManifestFileName, token).ConfigureAwait(false);
            var json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            var errors = new List<string>();
            var parsed = ManifestParser.Parse(json, errors);
            if (parsed is null || errors.Count != 0 || JsonSerializer.Serialize(parsed) != JsonSerializer.Serialize(manifest))
                throw new InvalidDataException();
            using var document = JsonDocument.Parse(json, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) Canonicalize(document.RootElement, writer);
            canonical = stream.ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            throw Error(id, PluginDiagnosticStage.Manifest, "LX_MANIFEST_INVALID", "插件清单不可读取、无效或与调用快照不一致。", ManifestParser.ManifestFileName);
        }
        var sources = ImmutableArray.CreateBuilder<SourcePluginInput>();
        foreach (var relative in relativePaths)
        {
            try
            {
                var bytes = await SourcePluginPaths.ReadAsync(directory, relative, token).ConfigureAwait(false);
                _ = new UTF8Encoding(false, true).GetString(bytes);
                sources.Add(new(relative, bytes));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                throw Error(id, PluginDiagnosticStage.SourceRead, "LX_SOURCE_READ_FAILED", "无法读取声明的 UTF-8 源码文件。", relative);
            }
        }
        var localization = ImmutableArray.CreateBuilder<SourcePluginInput>();
        foreach (var culture in Cultures)
        {
            var relative = $"i18n/{culture}.json";
            try
            {
                var bytes = await SourcePluginPaths.ReadAsync(directory, relative, token).ConfigureAwait(false);
                var json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                if (JsonSerializer.Deserialize<Dictionary<string, string>>(json) is not { } texts || texts.Any(item => item.Value is null))
                    throw new InvalidDataException();
                localization.Add(new(relative, bytes));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
            {
                throw Error(id, PluginDiagnosticStage.SourceRead, "LX_LOCALIZATION_INVALID", "本地化负载缺失或不是有效的文本字典。", relative);
            }
        }
        return new(canonical, sources.ToImmutable(), localization.ToImmutable());
    }

    internal string CacheKey(string referencesDigest, string compilerIdentity, string optionsIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("LoomX.SourceCache/v1");
            writer.Write(referencesDigest);
            writer.Write(compilerIdentity);
            writer.Write(optionsIdentity);
            WriteInputBody(writer);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    /// <summary>
    /// 插件自身输入（Manifest + 源码 + 本地化）的独立身份摘要，仅供缓存身份诊断对比；
    /// 域分隔前缀保证它与缓存键、其他摘要不可互换，正确性仍完全由 <see cref="CacheKey"/> 决定。
    /// </summary>
    internal string InputIdentity()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("LoomX.SourceInputs/v1");
            WriteInputBody(writer);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private void WriteInputBody(BinaryWriter writer)
    {
        writer.Write(Manifest.Length); writer.Write(Manifest);
        foreach (var input in Sources.Concat(Localization))
        {
            writer.Write(input.RelativePath);
            writer.Write(input.Bytes.Length); writer.Write(input.Bytes);
        }
    }

    private static void Canonicalize(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            var properties = element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
            if (properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) throw new InvalidDataException();
            foreach (var property in properties) { writer.WritePropertyName(property.Name); Canonicalize(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) Canonicalize(item, writer);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    private static SourcePluginInputException Error(string id, PluginDiagnosticStage stage, string code, string message, string? relative = null)
        => new(new(id, stage, PluginDiagnosticSeverity.Error, code, message, relative));
}

internal sealed class SourcePluginInputException(PluginDiagnostic diagnostic) : Exception
{
    internal PluginDiagnostic Diagnostic { get; } = diagnostic;
}
