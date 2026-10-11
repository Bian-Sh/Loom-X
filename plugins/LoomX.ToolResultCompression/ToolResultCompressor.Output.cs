using System.Text;
using System.Text.RegularExpressions;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 命令输出专用过滤器：测试输出与目录列表。与 Git 过滤器同属「先识别形态、再按语义折叠」一族，
/// 命中后直接产出摘要，跳过通用截断。
/// </summary>
public static partial class ToolResultCompressor
{
    private const int TestTracebackContextLines = 4;
    private const int DirectoryListingMaxEntries = 60;

    private static readonly Regex LongFormListingPattern = new(
        @"^[d\-l][rwx-]{9}",
        RegexOptions.Compiled);

    /// <summary>
    /// 按形态挑选一个专用过滤器。命中即返回，且过滤器之间是互斥的：
    /// 一段输出只可能属于一种命令形态，叠加过滤会破坏语义。
    /// </summary>
    private static bool TryApplyCommandFilter(
        string text,
        ToolResultCompressionOptions options,
        out string filtered,
        out string strategy)
    {
        filtered = text;
        strategy = string.Empty;
        var lines = text.Split('\n');

        if (TryGitLog(lines, out var log))
        {
            filtered = log;
            strategy = StrategyGitLog;
            return true;
        }

        if (TryGitDiff(lines, out var diff))
        {
            filtered = diff;
            strategy = StrategyGitDiff;
            return true;
        }

        if (TryGitStatus(lines, out var status))
        {
            filtered = status;
            strategy = StrategyGitStatus;
            return true;
        }

        if (TryTestOutput(lines, out var tests))
        {
            filtered = tests;
            strategy = StrategyTestOutput;
            return true;
        }

        if (TryDirectoryListing(lines, out var listing))
        {
            filtered = listing;
            strategy = StrategyDirectoryListing;
            return true;
        }

        return false;
    }

    /// <summary>测试输出：通过的用例折叠为计数，只保留失败与精简 traceback。</summary>
    private static bool TryTestOutput(string[] lines, out string result)
    {
        result = string.Empty;
        var passed = 0;
        var failures = new List<string>();
        var matchCount = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();

            if (IsPassLine(trimmed))
            {
                passed++;
                matchCount++;
                continue;
            }

            if (!IsFailureLine(trimmed)) continue;
            matchCount++;
            failures.Add(trimmed);
            for (var offset = 1; offset <= TestTracebackContextLines && index + offset < lines.Length; offset++)
            {
                var contextTrimmed = lines[index + offset].Trim();
                if (contextTrimmed.Length == 0) continue;
                if (!IsTracebackLine(contextTrimmed)) continue;
                failures.Add($"  {contextTrimmed}");
            }
        }

        if (matchCount < 3 || (passed == 0 && failures.Count == 0)) return false;

        var builder = new StringBuilder();
        if (passed > 0) builder.AppendLine($"通过 {passed} 项（已折叠）");
        if (failures.Count == 0)
        {
            builder.AppendLine("失败 0 项");
        }
        else
        {
            builder.AppendLine($"失败 {failures.Count} 处：");
            foreach (var failure in failures) builder.AppendLine(failure);
        }

        result = builder.ToString().TrimEnd();
        return result.Length > 0;
    }

    private static bool IsPassLine(string line) =>
        line.StartsWith("ok ", StringComparison.Ordinal)
        || line.StartsWith("--- PASS", StringComparison.Ordinal)
        || line.StartsWith("PASS", StringComparison.Ordinal)
        || line.StartsWith("✓", StringComparison.Ordinal)
        || line.StartsWith("✔", StringComparison.Ordinal)
        || line.Contains(" passed", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureLine(string line) =>
        line.StartsWith("--- FAIL", StringComparison.Ordinal)
        || line.StartsWith("FAIL", StringComparison.Ordinal)
        || line.StartsWith("✗", StringComparison.Ordinal)
        || line.StartsWith("✘", StringComparison.Ordinal)
        || line.StartsWith("FAILED", StringComparison.Ordinal)
        || line.Contains(" failed", StringComparison.OrdinalIgnoreCase)
        || line.Contains("AssertionError", StringComparison.Ordinal)
        || line.Contains("panic:", StringComparison.Ordinal);

    private static bool IsTracebackLine(string line) =>
        line.StartsWith("File \"", StringComparison.Ordinal)
        || line.StartsWith("at ", StringComparison.Ordinal)
        || line.StartsWith("E ", StringComparison.Ordinal)
        || line.StartsWith("assert", StringComparison.Ordinal)
        || line.StartsWith("Error:", StringComparison.Ordinal)
        || line.StartsWith("error:", StringComparison.Ordinal)
        || line.Contains("AssertionError", StringComparison.Ordinal);

    /// <summary>长格式目录列表：只保留名称，折叠为紧凑列表与计数。</summary>
    private static bool TryDirectoryListing(string[] lines, out string result)
    {
        result = string.Empty;
        var entries = new List<string>();
        var candidateLines = 0;
        var startsWithTotal = lines.Length > 0 && lines[0].TrimStart().StartsWith("total ", StringComparison.Ordinal);

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("total ", StringComparison.Ordinal)) continue;
            if (!LongFormListingPattern.IsMatch(trimmed)) continue;

            candidateLines++;
            var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;

            var name = fields[^1];
            if (name is "." or "..") continue;
            entries.Add(trimmed[0] == 'd' ? $"{name}/" : name);
        }

        if (!startsWithTotal && (candidateLines == 0 || candidateLines < lines.Length / 2)) return false;
        if (entries.Count == 0) return false;

        var shown = entries.Take(DirectoryListingMaxEntries).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"目录 {shown.Length}/{entries.Count} 项：");
        builder.AppendLine(string.Join(' ', shown));
        if (entries.Count > shown.Length)
            builder.AppendLine($"… 其余 {entries.Count - shown.Length} 项已省略");

        result = builder.ToString().TrimEnd();
        return result.Length > 0;
    }
}
