using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// Hermes Agent 适配器：把 LoomX 写进 ~/.hermes/config.yaml。
/// 移植自 ai-switch HermesAdapter。要点：
/// - 第三方端点挂在 custom_providers 序列（按 name 键控），model.provider/default 选中；
/// - api_key 必须内联写：Hermes 否则从 .env 或「按主机名派生的环境变量」找凭据，
///   回环端点匹配不到任何主机，手写条目必然报缺 Key；
/// - config.yaml 带大量注释且官方鼓励手编，因此只对 owned 的两个 section 做文本级
///   替换，其余内容（含注释）逐字节保留，替换后重新解析验证。
/// </summary>
public sealed class HermesConfigAdapter : IClientConfigAdapter
{
    public const string ProviderName = "loomx";
    private const string ApiMode = "chat_completions";
    private const string ProvidersSection = "custom_providers";
    private const string ModelSection = "model";

    private static readonly ISerializer SectionSerializer = new SerializerBuilder()
        .DisableAliases()
        .WithQuotingNecessaryStrings()
        .Build();

    private static readonly HashSet<string> OwnedProviderFields = new(StringComparer.Ordinal)
    { "name", "base_url", "api_key", "api_mode", "model", "models" };

    private static readonly HashSet<string> OwnedModelFields = new(StringComparer.Ordinal)
    { "provider", "base_url", "api_mode", "default" };

    public ClientTargetDescriptor Descriptor { get; } =
        new("hermes", "Hermes Agent", RequiresModelList: true, RestartRequired: false, Platform: "hermes");

    /// <summary>
    /// HERMES_HOME 仅在为绝对路径且 homeRoot 就是真实用户主目录时生效
    /// （匹配客户端自身的解析链）；fake home（测试）原样透传，保证封闭。
    /// </summary>
    public FileInfo ResolvePath(string homeRoot)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var overrideValue = Environment.GetEnvironmentVariable("HERMES_HOME")?.Trim();
        if (!string.IsNullOrEmpty(profile)
            && ClientHome.SamePath(homeRoot, profile)
            && !string.IsNullOrEmpty(overrideValue)
            && Path.IsPathRooted(overrideValue))
        {
            return new FileInfo(Path.Combine(overrideValue, "config.yaml"));
        }

        return new FileInfo(Path.Combine(homeRoot, ".hermes", "config.yaml"));
    }

    public Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken)
    {
        var raw = existing ?? string.Empty;
        var root = ParseRoot(raw, path.FullName);

        // 用户或更新版 Hermes 加在条目上的未知字段（key_env、request_timeout_seconds…）保留。
        var preserved = new Dictionary<string, object?>();
        foreach (var provider in ProvidersSequence(root))
        {
            if (provider is YamlMappingNode mapping
                && ScalarOf(Child(mapping, "name")) is { } name
                && name.Trim() == ProviderName)
            {
                foreach (var pair in mapping.Children)
                {
                    var key = ScalarOf(pair.Key);
                    if (key is null || OwnedProviderFields.Contains(key))
                    {
                        continue;
                    }

                    preserved[key] = NodeToObject(pair.Value);
                }
            }
        }

        var entry = new Dictionary<string, object?>
        {
            ["name"] = ProviderName,
            ["base_url"] = ClientConfigContent.BaseUrlWithV1(input.BaseUrl),
            ["api_key"] = input.ApiKey,
            ["api_mode"] = ApiMode,
        };
        if (input.Models.Count > 0)
        {
            // 单数 model 是运行时与 /model 选择器读取的字段；复数 models 只带元数据。
            entry["model"] = input.Models[0].Id;
            var modelEntries = new Dictionary<string, object?>();
            foreach (var model in input.Models)
            {
                modelEntries[model.Id] = new Dictionary<string, object?> { ["context_length"] = model.ContextWindow };
            }

            entry["models"] = modelEntries;
        }

        foreach (var pair in preserved)
        {
            entry.TryAdd(pair.Key, pair.Value);
        }

        // 外部 provider 是用户的，原地替换/追加自己的条目，其余逐项保留。
        var providerEntries = new List<object>();
        var replaced = false;
        foreach (var provider in ProvidersSequence(root))
        {
            if (provider is YamlMappingNode mapping
                && ScalarOf(Child(mapping, "name")) is { } existingName
                && existingName.Trim() == ProviderName)
            {
                providerEntries.Add(entry);
                replaced = true;
                continue;
            }

            if (NodeToObject(provider) is { } foreign)
            {
                providerEntries.Add(foreign);
            }
        }

        if (!replaced)
        {
            providerEntries.Add(entry);
        }

        var modelSection = new Dictionary<string, object?>();
        if (Child(root, ModelSection) is YamlMappingNode existingMapping)
        {
            foreach (var pair in existingMapping.Children)
            {
                var key = ScalarOf(pair.Key);
                if (key is not null && !OwnedModelFields.Contains(key))
                {
                    modelSection[key] = NodeToObject(pair.Value);
                }
            }
        }

        modelSection["provider"] = ProviderName;
        modelSection["base_url"] = ClientConfigContent.BaseUrlWithV1(input.BaseUrl);
        modelSection["api_mode"] = ApiMode;
        if (input.Models.Count > 0)
        {
            modelSection["default"] = input.Models[0].Id;
        }

        var rendered = ReplaceSection(raw, ProvidersSection, providerEntries);
        rendered = ReplaceSection(rendered, ModelSection, modelSection);

        // 文本级替换后必须重新解析验证结果仍然表达预期，才能落盘。
        var reparsed = ParseRoot(rendered, path.FullName);
        var saysLoomx = Child(reparsed, ModelSection) is YamlMappingNode modelNode
            && ScalarOf(Child(modelNode, "provider")) == ProviderName
            && ProvidersSequence(reparsed).Any(provider => provider is YamlMappingNode mapping
                && ScalarOf(Child(mapping, "name")) is { } name
                && name.Trim() == ProviderName);
        if (!saysLoomx)
        {
            throw new ClientConfigException(ClientConfigException.GeneratedInvalidCode, $"Hermes 配置段落替换后验证失败，拒绝写入：{path.FullName}");
        }

        return Task.FromResult(new ClientAdapterWriteResult(rendered, null, true));
    }

    public ClientConfigInspection Inspect(FileInfo path, string? existing)
    {
        if (existing is null)
        {
            return new ClientConfigInspection("missing", Managed: false);
        }

        try
        {
            var root = ParseRoot(existing, path.FullName);
            var managed = ProvidersSequence(root).Any(provider => provider is YamlMappingNode mapping
                && ScalarOf(Child(mapping, "name")) is { } name
                && name.Trim() == ProviderName);
            return new ClientConfigInspection(managed ? "managed" : "unmanaged", managed);
        }
        catch (ClientConfigException)
        {
            return new ClientConfigInspection("invalid", Managed: false);
        }
    }

    private static YamlNode? Child(YamlNode? parent, string key)
    {
        if (parent is not YamlMappingNode mapping)
        {
            return null;
        }

        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    }

    private static YamlMappingNode? ParseRoot(string raw, string path)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(raw);
            stream.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"既有 YAML 配置解析失败，拒绝覆写：{path}（{exception.Message}）");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is not null)
            {
                throw new ClientConfigException(ClientConfigException.ExistingInvalidCode, $"YAML 根节点必须是映射，拒绝覆写：{path}");
            }

            return null;
        }

        return root;
    }

    private static string? ScalarOf(YamlNode? node) =>
        node is YamlScalarNode scalar ? scalar.Value : null;

    private static IEnumerable<YamlNode> ProvidersSequence(YamlMappingNode? root)
    {
        if (Child(root, ProvidersSection) is not YamlSequenceNode sequence)
        {
            return [];
        }

        return sequence.Children;
    }

    /// <summary>YamlNode → 可被 SectionSerializer 序列化的 CLR 对象图。数字与布尔标量保留类型，避免重写时退化成带引号字符串。</summary>
    private static object? NodeToObject(YamlNode? node) => node switch
    {
        YamlScalarNode scalar => ScalarToObject(scalar.Value),
        YamlSequenceNode sequence => sequence.Children.Select(NodeToObject).ToList(),
        YamlMappingNode mapping => mapping.Children.ToDictionary(
            pair => ScalarOf(pair.Key) ?? pair.Key.ToString(),
            pair => NodeToObject(pair.Value)),
        _ => null,
    };

    private static object? ScalarToObject(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return value switch
        {
            "true" => true,
            "false" => false,
            _ => value,
        };
    }

    private static bool IsTopLevelKeyLine(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        var first = line[0];
        if (first is ' ' or '\t' or '#' or '-')
        {
            return false;
        }

        var colon = line.IndexOf(':');
        return colon >= 0
            && (colon + 1 == line.Length || line[colon + 1] is ' ' or '\t' or '\r');
    }

    /// <summary>一个顶层 section 的字节范围：从它的键行到下一个顶层键行或文本结尾。</summary>
    private static (int Start, int End)? SectionRange(string raw, string key)
    {
        var target = $"{key}:";
        int? start = null;
        var offset = 0;
        foreach (var line in raw.Split('\n'))
        {
            if (start is null)
            {
                if (IsTopLevelKeyLine(line) && line.StartsWith(target, StringComparison.Ordinal)
                    && (line.Length == target.Length || char.IsWhiteSpace(line[target.Length])))
                {
                    start = offset;
                }
            }
            else if (IsTopLevelKeyLine(line))
            {
                return (start.Value, offset);
            }

            offset += line.Length + 1;
        }

        return start is { } from ? (from, raw.Length) : null;
    }

    /// <summary>替换一个顶层 section；缺失时追加。section 之外的一切（含注释）逐字节保留。</summary>
    private static string ReplaceSection(string raw, string key, object value)
    {
        // 统一换行：YamlDotNet 在 Windows 上可能直接输出 CRLF；先归一到 LF，
        // 再按文件自身风格转换，避免改写后出现 \r\r\n 混合行尾。
        var section = new Dictionary<string, object?> { [key] = value };
        var serialized = SectionSerializer.Serialize(section).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!serialized.EndsWith('\n'))
        {
            serialized += "\n";
        }

        if (raw.Contains("\r\n", StringComparison.Ordinal))
        {
            serialized = serialized.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        var range = SectionRange(raw, key);
        if (range is not { } found)
        {
            var appended = raw.Length > 0 && !raw.EndsWith('\n') ? $"{raw}\n" : raw;
            return appended + serialized;
        }

        return string.Concat(raw[..found.Start], serialized, raw[found.End..]);
    }
}
