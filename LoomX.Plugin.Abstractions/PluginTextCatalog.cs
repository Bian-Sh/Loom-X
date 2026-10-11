using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace LoomX.Plugins;

/// <summary>
/// 插件自有文案目录。文案放在插件自己的 <c>i18n/&lt;culture&gt;.json</c>：
/// 宿主资源里不留任何插件专属文案，新增插件或新增语言都不需要改动宿主，
/// 翻译者也能直接改 JSON 而不必重新编译插件。
/// <para>
/// 本类型放在契约程序集，是为了让所有插件共用同一套查找顺序与回落规则，
/// 避免每个插件各写一份、行为还不一致。
/// </para>
/// <para>
/// 查找顺序（文件名与相对路径在所有工程里都固定为 <c>plugins/&lt;id&gt;/i18n/&lt;culture&gt;.json</c>）：
/// <list type="number">
/// <item>插件程序集所在目录下的 <c>i18n/</c>——正式运行时程序集就在插件包目录里，直接命中；</item>
/// <item>宿主目录下的 <c>plugins/&lt;pluginId&gt;/i18n/</c>——程序集被摊平到别处时
///（测试工程、验证工具）的回落路径；</item>
/// <item>嵌入程序集的默认文化（<see cref="DefaultCultureName"/>）——保证即使只拷了一个 DLL
/// 也不会显示原始键名。</item>
/// </list>
/// 不存在「按程序集名分层」这类只为绕开输出目录同名覆盖的查找分支：
/// 覆盖只在测试工程出现，正确做法是让测试输出复刻运行时布局，而不是让运行时多认一套路径。
/// </para>
/// </summary>
public sealed class PluginTextCatalog
{
    /// <summary>资源缺失时使用的文化，必须存在于每个插件的 i18n 目录。</summary>
    public const string DefaultCultureName = "en-US";

    private const string DirectoryName = "i18n";
    private const string PluginsDirectoryName = "plugins";
    private const int MaxTextLength = 4096;

    private static readonly ConcurrentDictionary<string, PluginTextCatalog> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, string> entries;

    private PluginTextCatalog(CultureInfo culture, IReadOnlyDictionary<string, string> entries)
    {
        Culture = culture;
        this.entries = entries;
    }

    /// <summary>用于数字与日期格式化的文化；解析失败时为不变文化。</summary>
    public CultureInfo Culture { get; }

    /// <summary>
    /// 取指定插件在指定文化下的文案目录。
    /// <paramref name="owner"/> 决定资源所属程序集，必须传插件自身的类型；
    /// <paramref name="pluginId"/> 是插件包目录名（与 Manifest 的 <c>id</c> 一致），
    /// 用于程序集不在插件包目录内时回落到宿主 <c>plugins/&lt;id&gt;/</c>。
    /// </summary>
    public static PluginTextCatalog For(Type owner, string pluginId, string? cultureName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var assembly = owner.Assembly;
        return Cache.GetOrAdd(
            $"{assembly.FullName}|{pluginId}|{cultureName}",
            _ => Build(assembly, pluginId, cultureName ?? string.Empty));
    }

    /// <summary>取文案；缺失时返回键本身，让问题可见而不是静默空白。</summary>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (!entries.TryGetValue(key, out var value))
        {
            return key.Length > MaxTextLength ? key[..MaxTextLength] : key;
        }

        // 资源文件是外部数据，超长或含控制字符一律视为不可用，避免撑爆请求正文或污染日志。
        if (value.Length > MaxTextLength || ContainsControlCharacters(value)) return key;
        return value;
    }

    public string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    private static bool ContainsControlCharacters(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch) && ch is not ('\n' or '\r' or '\t')) return true;
        }

        return false;
    }

    private static PluginTextCatalog Build(Assembly assembly, string pluginId, string cultureName)
    {
        var culture = ResolveCulture(cultureName);
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        // 先铺默认文化，再用目标文化覆盖：缺失键自动回落，不会出现半中文半英文的界面。
        Overlay(table, assembly, pluginId, DefaultCultureName);

        var fileCulture = ResolveFileCulture(cultureName);
        if (!string.Equals(fileCulture, DefaultCultureName, StringComparison.OrdinalIgnoreCase))
            Overlay(table, assembly, pluginId, fileCulture);

        return new PluginTextCatalog(culture, table);
    }

    private static void Overlay(
        Dictionary<string, string> table,
        Assembly assembly,
        string pluginId,
        string culture)
    {
        var loaded = LoadEntries(assembly, pluginId, culture);
        if (loaded is null) return;

        foreach (var (key, value) in loaded)
            table[key] = value;
    }

    private static IReadOnlyDictionary<string, string>? LoadEntries(
        Assembly assembly,
        string pluginId,
        string culture)
    {
        var fileName = culture + ".json";
        foreach (var directory in CandidateDirectories(assembly, pluginId))
        {
            var parsed = TryRead(Path.Combine(directory, DirectoryName, fileName));
            if (parsed is not null) return parsed;
        }

        // 默认文化随程序集嵌入：即使外部文件全丢了也有可读文案。
        return string.Equals(culture, DefaultCultureName, StringComparison.OrdinalIgnoreCase)
            ? TryParse(ReadEmbedded(assembly, culture))
            : null;
    }

    private static IReadOnlyDictionary<string, string>? TryRead(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return TryParse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            // 资源文件正在被替换或不可读：视为缺失，继续下一个候选路径。
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 候选目录：插件程序集所在目录（正式运行时即 <c>plugins/&lt;id&gt;/</c>）→
    /// 宿主输出目录下的同名插件包目录（程序集被摊平时）。
    /// 两处都用 <c>i18n/&lt;culture&gt;.json</c> 这一个相对路径，文件名不分叉。
    /// </summary>
    private static IEnumerable<string> CandidateDirectories(Assembly assembly, string pluginId)
    {
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
            yield return assemblyDirectory;

        if (IsSafeDirectoryName(pluginId))
            yield return Path.Combine(AppContext.BaseDirectory, PluginsDirectoryName, pluginId);
    }

    /// <summary>
    /// 插件 id 会参与拼路径，必须是不含分隔符与通配符的单个目录名，
    /// 否则回落路径可能指向包目录之外（<c>..</c> 之类）。
    /// </summary>
    private static bool IsSafeDirectoryName(string? pluginId) =>
        !string.IsNullOrWhiteSpace(pluginId)
        && pluginId is not ("." or "..")
        && pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string ReadEmbedded(Assembly assembly, string culture)
    {
        var expected = $"{assembly.GetName().Name}.{DirectoryName}.{culture}.json";
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!string.Equals(name, expected, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) return string.Empty;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return string.Empty;
    }

    private static IReadOnlyDictionary<string, string>? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // 资源文件损坏不应让插件不可用：回落默认文化或键名。
            return null;
        }
    }

    /// <summary>文化名来自宿主，不可信：解析失败退化为不变文化。</summary>
    private static CultureInfo ResolveCulture(string cultureName)
    {
        try
        {
            return string.IsNullOrWhiteSpace(cultureName)
                ? CultureInfo.GetCultureInfo(DefaultCultureName)
                : CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>
    /// 把请求的文化映射到已提供的资源文件：繁体归 zh-TW、简体归 zh-CN、日文归 ja-JP，其余用默认文化。
    /// 只按前两级匹配，避免为每种区域变体都维护一份文件。
    /// </summary>
    private static string ResolveFileCulture(string cultureName)
    {
        var normalized = (cultureName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("zh-tw", StringComparison.Ordinal)
            || normalized.StartsWith("zh-hk", StringComparison.Ordinal))
        {
            return "zh-TW";
        }

        if (normalized.StartsWith("zh", StringComparison.Ordinal)) return "zh-CN";
        if (normalized.StartsWith("ja", StringComparison.Ordinal)) return "ja-JP";
        return DefaultCultureName;
    }
}