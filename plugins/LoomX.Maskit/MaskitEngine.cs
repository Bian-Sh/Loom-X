using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace LoomX.Maskit;

/// <summary>
/// 脱敏/还原引擎。占位符形如 &lt;MASKIT:类别:序号&gt;，映射表只存在于进程内存：
/// 对齐 maskit「凭据原文不落库」红线，重启后旧占位符保持占位、不得猜测还原。
/// </summary>
public sealed class MaskitEngine
{
    private const int MaxTrackedTokens = 5000;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex PlaceholderRegex =
        new(@"<MASKIT:([^:<>]+):(\d+)>", RegexOptions.Compiled);

    private readonly MaskitRuleStore store;
    private readonly ConcurrentDictionary<string, string> tokens = new(StringComparer.Ordinal);
    private int placeholderSequence;

    public MaskitEngine(MaskitRuleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>按「自定义正则 → 内置形态 → 密钥前缀 → 自定义词」顺序脱敏，返回替换项数。</summary>
    public string Sanitize(string payload, out int replacements)
    {
        var counter = new StrongBox<int>(0);
        if (string.IsNullOrEmpty(payload))
        {
            replacements = 0;
            return payload;
        }

        var result = payload;
        try
        {
            foreach (var rule in store.EnabledRules().Where(item => item.Kind == MaskitRuleKind.Pattern))
                result = Regex.Replace(
                    result,
                    rule.Value,
                    match => Tokenize(rule.Category, match.Value, counter),
                    RegexOptions.None,
                    MatchTimeout);

            foreach (var builtin in store.EnabledBuiltinRules())
                result = Regex.Replace(
                    result,
                    builtin.Pattern,
                    match => Tokenize(builtin.Key, match.Value, counter),
                    RegexOptions.None,
                    MatchTimeout);

            foreach (var prefix in store.SecretPrefixes)
                result = Regex.Replace(
                    result,
                    Regex.Escape(prefix) + @"[A-Za-z0-9._-]{6,}",
                    match => Tokenize("SECRET_PREFIX", match.Value, counter),
                    RegexOptions.None,
                    MatchTimeout);

            // 自定义词按长度降序替换，避免短词先命中并吞掉长词。
            var words = store.EnabledRules()
                .Where(item => item.Kind == MaskitRuleKind.Word)
                .OrderByDescending(item => item.Value.Length)
                .ToArray();
            foreach (var rule in words)
            {
                var pattern = rule.WholeWord
                    ? $@"(?<![A-Za-z0-9]){Regex.Escape(rule.Value)}(?![A-Za-z0-9])"
                    : Regex.Escape(rule.Value);
                result = Regex.Replace(
                    result,
                    pattern,
                    match => Tokenize(rule.Category, match.Value, counter),
                    RegexOptions.None,
                    MatchTimeout);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // 正则超时：保留已完成的替换结果，交由上层按失败策略处理。
        }
        catch (ArgumentException)
        {
            // 非法正则（例如用户在 UI 里写入的模式）：忽略该轮替换，不抛出。
        }

        replacements = counter.Value;
        return result;
    }

    /// <summary>只还原本进程签发的占位符；未知或已失效的占位符保持原样。</summary>
    public string Restore(string payload, out int restored)
    {
        var counter = new StrongBox<int>(0);
        if (string.IsNullOrEmpty(payload))
        {
            restored = 0;
            return payload;
        }

        var result = PlaceholderRegex.Replace(payload, match =>
        {
            if (!tokens.TryGetValue(match.Value, out var original)) return match.Value;
            Interlocked.Increment(ref counter.Value);
            return original;
        });

        restored = counter.Value;
        return result;
    }

    private string Tokenize(string category, string value, StrongBox<int> counter)
    {
        var placeholder = $"<MASKIT:{SanitizeCategory(category)}:{Interlocked.Increment(ref placeholderSequence)}>";
        if (tokens.Count >= MaxTrackedTokens)
        {
            // 达到上限后丢弃历史映射：宁可让旧占位符无法还原，也不无界增长内存与敏感驻留。
            tokens.Clear();
        }
        tokens[placeholder] = value;
        Interlocked.Increment(ref counter.Value);
        return placeholder;
    }

    private static string SanitizeCategory(string category)
    {
        var chars = (category ?? string.Empty)
            .Where(item => !char.IsControl(item) && item is not ('<' or '>' or ':'))
            .Take(32)
            .ToArray();
        var normalized = new string(chars).Trim();
        return normalized.Length == 0 ? "SENSITIVE" : normalized;
    }
}
