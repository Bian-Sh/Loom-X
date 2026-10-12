using LoomX.Plugins;

namespace LoomX.Plugin.Storage.Sqlite;

internal sealed class PluginStoragePaths
{
    internal string PluginRoot { get; }
    // 每实例内部验收探针；默认为空，不属于插件公共契约，也不进入日志。
    internal Action<string, string>? Checkpoint { get; set; }
    private readonly string dataRoot;
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³" };

    internal PluginStoragePaths(string pluginId, string dataRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId.Length > 128 || pluginId != pluginId.Trim() || pluginId.Contains("..", StringComparison.Ordinal) || !SafeSegment(pluginId))
            throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
        if (string.IsNullOrWhiteSpace(dataRootDirectory) || !Path.IsPathFullyQualified(dataRootDirectory))
            throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
        try
        {
            dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRootDirectory));
            if (string.Equals(dataRoot, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(dataRoot)!), StringComparison.OrdinalIgnoreCase) || dataRoot.StartsWith(@"\\", StringComparison.Ordinal) || dataRoot.Contains('\0'))
                throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
            var broadRoots = new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Windows, Environment.SpecialFolder.System };
            if (broadRoots.Select(Environment.GetFolderPath).Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => string.Equals(dataRoot, Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
            PluginRoot = Path.Combine(dataRoot, pluginId);
        }
        catch (Exception ex) { throw PluginSqliteErrors.Map(ex); }
    }

    internal string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 1024 || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(s => !SafeSegment(s))) throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
        var path = Path.GetFullPath(Path.Combine(PluginRoot, Path.Combine(segments)));
        if (!path.StartsWith(PluginRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
        return path;
    }

    private static bool SafeSegment(string s) => !string.IsNullOrWhiteSpace(s) && s is not "." and not ".." && !s.EndsWith('.') && !s.EndsWith(' ') && !Reserved.Contains(s.Split('.')[0].TrimEnd()) && !s.Any(c => c < 32 || c == 34 || "<>:/\\|?*".Contains(c));

    internal void CreateAndValidate(string path)
    {
        Validate(path);
        // 每段创建前后都复查，已存在的链接（含悬空链接）不得被跟随。
        var parent = Path.GetDirectoryName(path)!;
        var chain = new Stack<string>();
        for (var directory = parent; directory is not null; directory = Path.GetDirectoryName(directory)) chain.Push(directory);
        foreach (var directory in chain)
        {
            Check(directory, true);
            Directory.CreateDirectory(directory);
            Checkpoint?.Invoke("目录已创建", directory);
            Check(directory, true);
        }
        Validate(path);
    }

    internal void Validate(string path)
    {
        for (var directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory)) Check(directory, true);
        Check(path, false);
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" }) Check(path + suffix, false);
        if (File.Exists(path) && new FileInfo(path).Length > SqliteDatabase.MaximumDatabaseBytes)
            throw PluginSqliteErrors.Create(PluginSqlError.ResourceLimit);
    }

    private static void Check(string path, bool directory)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
            throw PluginSqliteErrors.Create(PluginSqlError.InvalidPath);
    }
}
