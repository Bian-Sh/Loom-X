using System.Text;

namespace LoomX.ToolResultCompression;

/// <summary>
/// Git 输出过滤器：log / status / diff 三种形态各自折叠。
/// git 输出体量大但信息密度低，按语义折叠比按字符截断更能保住模型需要的部分。
/// </summary>
public static partial class ToolResultCompressor
{
    private const int GitLogMaxEntries = 40;
    private const int GitStatusMaxFilesPerGroup = 15;
    private const int GitDiffMaxChangeLines = 300;

    /// <summary>git log：只保留短哈希、作者与标题。</summary>
    private static bool TryGitLog(string[] lines, out string result)
    {
        result = string.Empty;
        var commitLines = lines.Count(line => line.TrimStart().StartsWith("commit ", StringComparison.Ordinal));
        if (commitLines < 2) return false;

        var builder = new StringBuilder();
        var emitted = 0;
        var skipped = 0;
        string? hash = null;
        string? author = null;
        string? title = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("commit ", StringComparison.Ordinal))
            {
                AppendGitLogEntry(builder, ref emitted, ref skipped, hash, author, title);
                hash = line["commit ".Length..].Trim();
                author = null;
                title = null;
                continue;
            }

            if (line.StartsWith("Author:", StringComparison.Ordinal))
            {
                author = line["Author:".Length..].Trim();
                continue;
            }

            // Date / Merge / 空行对模型判断提交序列没有价值，直接丢弃。
            if (line.StartsWith("Date:", StringComparison.Ordinal)
                || line.StartsWith("Merge:", StringComparison.Ordinal)
                || line.Length == 0)
            {
                continue;
            }

            title ??= line;
        }

        AppendGitLogEntry(builder, ref emitted, ref skipped, hash, author, title);
        if (skipped > 0) builder.AppendLine($"… 其余 {skipped} 条提交已省略");

        result = builder.ToString().TrimEnd();
        return result.Length > 0;
    }

    private static void AppendGitLogEntry(
        StringBuilder builder,
        ref int emitted,
        ref int skipped,
        string? hash,
        string? author,
        string? title)
    {
        if (string.IsNullOrEmpty(hash)) return;
        if (emitted >= GitLogMaxEntries)
        {
            skipped++;
            return;
        }

        var shortHash = hash.Length > 7 ? hash[..7] : hash;
        var message = SafeHead(title ?? "(无提交信息)", 100);
        builder.AppendLine($"{shortHash} {author ?? "(未知作者)"} {message}");
        emitted++;
    }

    /// <summary>git status：按状态分组，折叠同类文件列表。</summary>
    private static bool TryGitStatus(string[] lines, out string result)
    {
        result = string.Empty;
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var headers = new List<string>();
        var matched = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("On branch", StringComparison.Ordinal)
                || trimmed.StartsWith("Your branch", StringComparison.Ordinal)
                || trimmed.StartsWith("HEAD detached", StringComparison.Ordinal))
            {
                headers.Add(trimmed);
                continue;
            }

            if (line.Length < 4 || line[2] != ' ') continue;
            var status = ClassifyGitStatus(line[0], line[1]);
            if (status is null) continue;

            var path = line[3..].Trim();
            if (path.Length == 0) continue;
            if (path.Contains(" -> ", StringComparison.Ordinal))
                path = path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];

            matched++;
            if (!groups.TryGetValue(status, out var bucket))
            {
                bucket = [];
                groups[status] = bucket;
            }

            bucket.Add(path);
        }

        if (matched < 3) return false;

        var builder = new StringBuilder();
        foreach (var header in headers) builder.AppendLine(header);
        foreach (var group in groups.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"[{group.Key}] {group.Value.Count}");
            var shown = 0;
            foreach (var path in group.Value)
            {
                if (shown++ >= GitStatusMaxFilesPerGroup) break;
                builder.AppendLine($"  {path}");
            }

            if (group.Value.Count > shown)
                builder.AppendLine($"  … 其余 {group.Value.Count - shown} 项");
        }

        result = builder.ToString().TrimEnd();
        return result.Length > 0;
    }

    private static string? ClassifyGitStatus(char index, char worktree)
    {
        if (index == '?' || worktree == '?') return "untracked";
        if (index == 'U' || worktree == 'U') return "conflict";
        if (index == 'A' || worktree == 'A') return "added";
        if (index == 'D' || worktree == 'D') return "deleted";
        if (index == 'R' || worktree == 'R') return "renamed";
        if (index == 'C' || worktree == 'C') return "copied";
        if (index == 'M' || worktree == 'M') return "modified";
        if (index == 'T' || worktree == 'T') return "typechanged";
        return null;
    }

    /// <summary>git diff：去掉文件头元信息，长段上下文折叠为计数。</summary>
    private static bool TryGitDiff(string[] lines, out string result)
    {
        result = string.Empty;
        var hasDiffHeader = lines.Any(line => line.TrimStart().StartsWith("diff --git", StringComparison.Ordinal));
        var hasHunk = lines.Any(line => line.TrimStart().StartsWith("@@", StringComparison.Ordinal));
        if (!hasDiffHeader && !hasHunk) return false;

        var builder = new StringBuilder();
        var contextRun = 0;
        var changeLines = 0;
        var omittedChanges = 0;

        foreach (var raw in lines)
        {
            var trimmed = raw.TrimEnd().TrimStart();

            if (trimmed.StartsWith("diff --git", StringComparison.Ordinal))
            {
                FlushDiffContext(builder, ref contextRun);
                builder.AppendLine($"文件 {ExtractDiffPath(trimmed)}");
                continue;
            }

            if (IsDiffMetadata(trimmed)) continue;

            if (trimmed.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushDiffContext(builder, ref contextRun);
                builder.AppendLine(trimmed);
                continue;
            }

            if (trimmed.StartsWith('+') || trimmed.StartsWith('-'))
            {
                FlushDiffContext(builder, ref contextRun);
                if (changeLines >= GitDiffMaxChangeLines)
                {
                    omittedChanges++;
                    continue;
                }

                builder.AppendLine(trimmed);
                changeLines++;
                continue;
            }

            if (trimmed.Length == 0) continue;
            contextRun++;
        }

        FlushDiffContext(builder, ref contextRun);
        if (omittedChanges > 0) builder.AppendLine($"… 其余 {omittedChanges} 处变更已省略");

        result = builder.ToString().TrimEnd();
        return result.Length > 0;
    }

    /// <summary>diff 的索引/模式/重命名元信息：模型要的是改动内容，不是文件头。</summary>
    private static bool IsDiffMetadata(string line) =>
        line.StartsWith("index ", StringComparison.Ordinal)
        || line.StartsWith("--- ", StringComparison.Ordinal)
        || line.StartsWith("+++ ", StringComparison.Ordinal)
        || line.StartsWith("new file mode", StringComparison.Ordinal)
        || line.StartsWith("deleted file mode", StringComparison.Ordinal)
        || line.StartsWith("old mode", StringComparison.Ordinal)
        || line.StartsWith("new mode", StringComparison.Ordinal)
        || line.StartsWith("similarity index", StringComparison.Ordinal)
        || line.StartsWith("rename from", StringComparison.Ordinal)
        || line.StartsWith("rename to", StringComparison.Ordinal);

    private static void FlushDiffContext(StringBuilder builder, ref int contextRun)
    {
        if (contextRun <= 3) return;
        builder.AppendLine($"… {contextRun} 行上下文已省略");
        contextRun = 0;
    }

    private static string ExtractDiffPath(string diffLine)
    {
        var index = diffLine.LastIndexOf(" b/", StringComparison.Ordinal);
        if (index < 0) index = diffLine.LastIndexOf(" a/", StringComparison.Ordinal);
        if (index < 0) return diffLine;
        return diffLine[(index + 3)..].Trim();
    }
}
