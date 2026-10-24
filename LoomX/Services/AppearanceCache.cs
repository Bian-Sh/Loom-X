using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// 启动期外观初值快照。写入发生在设置保存成功路径（早于下一次进程启动），
/// 读取发生在显示闪屏之前——两者都不触碰配置库，满足启动单次读库约束。
/// </summary>
public sealed record AppearanceCacheSnapshot(bool Enabled, int Opacity, int Blur, string Theme)
{
    /// <summary>缓存缺失或损坏时的兜底初值：不透明 + 跟随系统主题。</summary>
    public static AppearanceCacheSnapshot OpaqueDefault { get; } = new(false, 86, 24, "system");
}

public static class AppearanceCache
{
    private static readonly JsonSerializerOptions SerializeOptions = new(JsonSerializerDefaults.Web);

    /// <summary>读取外观缓存；缺失、损坏或取值非法时返回 null 并记 Debug 日志，绝不抛出。</summary>
    public static AppearanceCacheSnapshot? TryRead(string path, ILogger? logger = null)
    {
        try
        {
            if (!File.Exists(path))
            {
                logger?.LogDebug("外观缓存缺失，使用默认不透明材质 {Path}", path);
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetBoolean(root, "enabled", out var enabled)
                || !TryGetInt32(root, "opacity", out var opacity)
                || !TryGetInt32(root, "blur", out var blur)
                || !root.TryGetProperty("theme", out var themeElement)
                || themeElement.ValueKind != JsonValueKind.String)
            {
                logger?.LogDebug("外观缓存结构损坏，使用默认不透明材质 {Path}", path);
                return null;
            }

            var theme = NormalizeTheme(themeElement.GetString());
            if (theme is null || opacity is < 0 or > 100 || blur is < 0 or > 64)
            {
                logger?.LogDebug("外观缓存取值非法，使用默认不透明材质 {Path}", path);
                return null;
            }

            return new AppearanceCacheSnapshot(enabled, opacity, blur, theme);
        }
        catch (Exception exception)
        {
            logger?.LogDebug(exception, "外观缓存读取失败，使用默认不透明材质 {Path}", path);
            return null;
        }
    }

    /// <summary>原子写入外观缓存（临时文件 + 替换）；失败记 Warning 并返回 false，绝不抛出。</summary>
    public static bool TryWrite(string path, AppearanceCacheSnapshot snapshot, ILogger? logger = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                new AppearanceCachePayload(snapshot.Enabled, snapshot.Opacity, snapshot.Blur, snapshot.Theme),
                SerializeOptions);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "外观缓存写入失败 {Path}", path);
            return false;
        }
    }

    /// <summary>把配置库中的主题值归一化为缓存合法取值；无法识别时返回 null。</summary>
    public static string? NormalizeTheme(string? theme)
    {
        var normalized = (theme ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "dark" or "light" or "system" ? normalized : null;
    }

    private static bool TryGetBoolean(JsonElement root, string name, out bool value)
    {
        if (root.TryGetProperty(name, out var element)
            && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
        {
            value = element.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt32(JsonElement root, string name, out int value)
    {
        if (root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value))
            return true;

        value = default;
        return false;
    }

    private sealed record AppearanceCachePayload(bool Enabled, int Opacity, int Blur, string Theme);
}
