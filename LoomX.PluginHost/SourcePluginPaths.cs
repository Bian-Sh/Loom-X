using System.Text.RegularExpressions;

namespace LoomX.Plugins.Host;

/// <summary>源码与缓存的本地路径边界；保守拒绝所有 reparse point，不把可信插件当作沙箱。</summary>
internal static class SourcePluginPaths
{
    internal static string Relative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl)
            || value.IndexOfAny([':', '<', '>', '"', '|', '?', '*']) >= 0)
            throw new InvalidDataException();
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/')) throw new InvalidDataException();
        foreach (var part in normalized.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:[.]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException();
        }
        return normalized;
    }

    internal static string Resolve(string root, string relative)
    {
        var absoluteRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(absoluteRoot, Relative(relative)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(absoluteRoot) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException();
        NoLinks(path);
        return path;
    }

    internal static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static async Task<byte[]> ReadAsync(string root, string relative, CancellationToken token)
    {
        var path = Resolve(root, relative);
        var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        NoLinks(path);
        return bytes;
    }
}
