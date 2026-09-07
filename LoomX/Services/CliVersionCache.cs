using System.Text.Json;
using System.Text.Json.Serialization;
using LoomX.Services;

namespace LoomX.Services;

/// <summary>
/// CLI 版本缓存。JSON 文件存于 %LocalAppData%/LoomX/cli-versions.json，24h TTL。
/// 用户覆盖（Grok 手改）持久化在缓存条目中，不会被后续 fetch 覆盖。
/// </summary>
public sealed class CliVersionCache
{
    public const string DefaultPath = "cli-versions.json";
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly string _path;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    public CliVersionCache(string? path = null, TimeSpan? ttl = null, Func<DateTimeOffset>? now = null)
    {
        _path = path ?? System.IO.Path.Combine(AppDataPaths.RootDirectory, DefaultPath);
        _ttl = ttl ?? DefaultTtl;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal string Path => _path;
    internal TimeSpan Ttl => _ttl;

    /// <summary>
    /// 读取缓存中指定 CLI 的当前条目。不存在或过期返回 null。
    /// </summary>
    public CliVersionInfo? Get(CliIdentityType type)
    {
        lock (_gate)
        {
            var entry = LoadEntry(type);
            if (entry is null || string.IsNullOrWhiteSpace(entry.Version)) return null;
            return entry;
        }
    }

    /// <summary>
    /// 判断指定 CLI 的缓存条目是否过期或缺失。
    /// </summary>
    public bool IsStale(CliIdentityType type)
    {
        var entry = Get(type);
        if (entry is null) return true;
        if (entry.Source is CliVersionSource.UserOverridden) return false; // 用户覆盖不受 TTL 影响
        if (entry.FetchedAt is null) return true;
        return (_now() - entry.FetchedAt.Value) > _ttl;
    }

    /// <summary>
    /// 写入缓存条目。用户覆盖标记 (UserOverridden) 不受 TTL 影响。
    /// </summary>
    public void Set(CliIdentityType type, string version, CliVersionSource source, DateTimeOffset? fetchedAt = null)
    {
        lock (_gate)
        {
            var entries = LoadAll();
            entries[type] = new CachedEntry
            {
                Version = version,
                FetchedAt = fetchedAt ?? _now(),
                Source = source,
            };
            SaveAll(entries);
        }
    }

    /// <summary>
    /// 设置用户覆盖版本（Grok 手改），Source 固定 UserOverridden。
    /// </summary>
    public void SetUserOverride(CliIdentityType type, string version)
        => Set(type, version, CliVersionSource.UserOverridden, fetchedAt: _now());

    /// <summary>
    /// 判断指定 CLI 是否存在用户覆盖。
    /// </summary>
    public bool HasUserOverride(CliIdentityType type)
    {
        var entry = Get(type);
        return entry is not null && entry.Source == CliVersionSource.UserOverridden;
    }

    /// <summary>
    /// 清除用户覆盖（可选：允许回归默认值）。
    /// </summary>
    public void ClearUserOverride(CliIdentityType type)
    {
        lock (_gate)
        {
            var entries = LoadAll();
            if (entries.TryGetValue(type, out var entry) && entry.Source == CliVersionSource.UserOverridden)
            {
                entries.Remove(type);
                SaveAll(entries);
            }
        }
    }

    private CliVersionInfo? LoadEntry(CliIdentityType type)
    {
        var entry = LoadAll().TryGetValue(type, out var e) ? e : null;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Version)) return null;
        return new CliVersionInfo(
            type,
            entry.Version,
            entry.FetchedAt,
            entry.Source);
    }

    private Dictionary<CliIdentityType, CachedEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<CliIdentityType, CachedEntry>();
            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text)) return new Dictionary<CliIdentityType, CachedEntry>();
            return JsonSerializer.Deserialize<CachedFilePayload>(text, Options)?.Entries ?? new();
        }
        catch
        {
            // 缓存损坏或不可读，静默降级为空缓存。
            return new Dictionary<CliIdentityType, CachedEntry>();
        }
    }

    private void SaveAll(Dictionary<CliIdentityType, CachedEntry> entries)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var payload = new CachedFilePayload(entries);
            var json = JsonSerializer.Serialize(payload, Options);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 缓存写入失败静默降级：下次读取时仍会返回上次成功的缓存或默认值。
        }
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private sealed class CachedFilePayload
    {
        [JsonPropertyName("entries")]
        public Dictionary<CliIdentityType, CachedEntry> Entries { get; }
        public CachedFilePayload(Dictionary<CliIdentityType, CachedEntry> entries) => Entries = entries;
    }

    private sealed class CachedEntry
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
        [JsonPropertyName("fetchedAt")]
        public DateTimeOffset? FetchedAt { get; set; }
        [JsonPropertyName("source")]
        public CliVersionSource Source { get; set; } = CliVersionSource.Default;
    }
}
