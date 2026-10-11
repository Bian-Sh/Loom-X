namespace LoomX.ToolResultCompression;

/// <summary>
/// 工具结果的 token 估算器。Router 侧拿不到目标模型的真实分词器，
/// 因此按字符构成做保守估算：拉丁字符约 4 字符/token，中日韩与全角字符约 1.5 字符/token。
/// <para>
/// 这只是量级估算，不是账单口径；UI 必须标注「估算」，不得声称与 Provider 计费一致。
/// 压缩前后用同一个口径相减，比值与节省趋势是可信的。
/// </para>
/// </summary>
public static class CompressionTokenEstimator
{
    /// <summary>拉丁字母、数字与常见符号的估算密度。</summary>
    public const double CharsPerTokenDefault = 4.0;

    /// <summary>中日韩与全角字符的估算密度：同样字符数会占用更多 token。</summary>
    public const double CharsPerTokenCjk = 1.5;

    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var cjk = 0;
        foreach (var character in text)
            if (IsWideCharacter(character))
                cjk++;

        var other = text.Length - cjk;
        var tokens = cjk / CharsPerTokenCjk + other / CharsPerTokenDefault;
        return (int)Math.Ceiling(tokens);
    }

    /// <summary>由字符数反推估算 token 数；用于只有累计字符数的历史数据。</summary>
    public static int EstimateFromChars(long chars) =>
        chars <= 0 ? 0 : (int)Math.Ceiling(chars / CharsPerTokenDefault);

    private static bool IsWideCharacter(char character) =>
        character is (>= '\u1100' and <= '\u115F')      // 韩文字母
                  or (>= '\u2E80' and <= '\u303E')      // CJK 部首、标点
                  or (>= '\u3041' and <= '\u33FF')      // 假名、CJK 兼容
                  or (>= '\u3400' and <= '\u4DBF')      // CJK 扩展 A
                  or (>= '\u4E00' and <= '\u9FFF')      // CJK 基本区
                  or (>= '\uA000' and <= '\uA4CF')      // 彝文
                  or (>= '\uAC00' and <= '\uD7A3')      // 韩文音节
                  or (>= '\uF900' and <= '\uFAFF')      // CJK 兼容 ideograph
                  or (>= '\uFE30' and <= '\uFE4F')      // CJK 兼容形式
                  or (>= '\uFF00' and <= '\uFF60')      // 全角形式
                  or (>= '\uFFE0' and <= '\uFFE6');
}
