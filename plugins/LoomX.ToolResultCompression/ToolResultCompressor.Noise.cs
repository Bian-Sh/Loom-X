using System.Text;
using System.Text.RegularExpressions;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 通用文本策略：噪音过滤、去重与截断。这一族策略与具体命令无关，对所有工具结果生效。
/// </summary>
public static partial class ToolResultCompressor
{
    /// <summary>LoomX 凭据占位符起始标记；截断时不得把它截成半截。</summary>
    private const string CredentialMarker = "{{LOOMX_CREDENTIAL";

    private static readonly Regex AnsiPattern = new(
        @"\x1B(?:\[[0-9;?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\))",
        RegexOptions.Compiled);

    /// <summary>清理 ANSI 转义、回车与多余空行，统一行尾空白。</summary>
    private static string RemoveNoise(string text)
    {
        var value = AnsiPattern.Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), string.Empty);
        var lines = value.Split('\n');
        var builder = new StringBuilder(value.Length);
        var blankRun = 0;
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                // 连续空行只保留一个：缩进结构还在，视觉噪音被去掉。
                if (++blankRun > 1) continue;
            }
            else
            {
                blankRun = 0;
            }

            if (builder.Length > 0) builder.Append('\n');
            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    /// <summary>连续重复行折叠为计数，非连续高频重复行只保留首次出现并标注总次数。</summary>
    private static string DedupeLines(string text, ToolResultCompressionOptions options)
    {
        var lines = text.Split('\n');
        var collapsed = new List<string>(lines.Length);

        var run = 1;
        for (var index = 0; index < lines.Length; index++)
        {
            var key = lines[index].Trim();
            var next = index + 1 < lines.Length ? lines[index + 1].Trim() : null;
            if (key.Length > 0 && string.Equals(key, next, StringComparison.Ordinal))
            {
                run++;
                continue;
            }

            collapsed.Add(run >= options.DedupeRepeatThreshold && key.Length > 0
                ? $"{key}   (×{run})"
                : lines[index]);
            run = 1;
        }

        return CollapseGlobalRepeats(collapsed, options);
    }

    private static string CollapseGlobalRepeats(List<string> collapsed, ToolResultCompressionOptions options)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in collapsed)
        {
            var key = line.Trim();
            if (key.Length is 0 or > 200) continue;
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var result = new List<string>(collapsed.Count);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in collapsed)
        {
            var key = line.Trim();
            if (key.Length is 0 or > 200
                || !counts.TryGetValue(key, out var count)
                || count < options.DedupeGlobalThreshold)
            {
                result.Add(line);
                continue;
            }

            if (emitted.Add(key)) result.Add($"{key}   (共 {count} 次)");
        }

        return string.Join('\n', result);
    }

    private static string TruncateLongLines(string text, ToolResultCompressionOptions options)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length <= options.MaxLineLength) continue;
            var omitted = line.Length - options.MaxLineLength;
            lines[index] = $"{SafeHead(line, options.MaxLineLength)}  …(+{omitted} 字符)";
        }

        return string.Join('\n', lines);
    }

    /// <summary>超出行数上限时保留头尾，中间用一行摘要替代（尾部通常包含结果/错误）。</summary>
    private static string TruncateLines(string text, ToolResultCompressionOptions options)
    {
        var lines = text.Split('\n');
        if (lines.Length <= options.MaxLines) return text;

        var head = Math.Min(options.HeadLines, options.MaxLines);
        var tail = Math.Min(options.TailLines, Math.Max(0, options.MaxLines - head));
        var omitted = lines.Length - head - tail;

        var result = new List<string>(head + tail + 1);
        result.AddRange(lines.Take(head));
        if (omitted > 0) result.Add($"… 已省略 {omitted} 行 …");
        if (tail > 0) result.AddRange(lines.Skip(lines.Length - tail));
        return string.Join('\n', result);
    }

    /// <summary>截断时不得破坏 LoomX 凭据占位符：未闭合的占位符整体回退。</summary>
    private static string SafeHead(string value, int max)
    {
        if (max <= 0) return string.Empty;
        if (max >= value.Length) return value;

        var head = value[..max];
        var marker = head.LastIndexOf(CredentialMarker, StringComparison.Ordinal);
        if (marker >= 0 && head.IndexOf("}}", marker, StringComparison.Ordinal) < 0)
            head = head[..marker];
        return head.TrimEnd();
    }
}
