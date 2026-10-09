namespace LoomX.Maskit;

/// <summary>自定义规则形态：按字面词匹配，或按正则匹配。</summary>
public enum MaskitRuleKind
{
    Word,
    Pattern,
}

/// <summary>
/// 一条脱敏规则。Category 是占位符里的标签（对齐 maskit 的 sensitive 分类口径），
/// Value 是字面词或正则；内置基线只提供形态识别，自定义规则由用户在设置页增删改查。
/// </summary>
public sealed record MaskitRule(
    string Id,
    string Category,
    string Value,
    MaskitRuleKind Kind = MaskitRuleKind.Word,
    bool WholeWord = true,
    bool Enabled = true);

/// <summary>
/// 内置形态规则：只可开关、不可删除。EnabledByDefault 与 maskit 引擎默认口径一致，
/// 公网 IP、内网 IP、IPv6、中国香港身份证、统一社会信用代码、MAC 默认关闭以避免过度脱敏。
/// </summary>
public sealed record MaskitBuiltinRule(string Key, string Pattern, bool EnabledByDefault);

/// <summary>内置形态规则基线表。</summary>
internal static class MaskitBuiltinRules
{
    /// <summary>
    /// 顺序即脱敏顺序：先窄后宽，避免短类别抢先吞掉长类别（身份证先于银行卡、私有 IP 先于公网 IP）。
    /// </summary>
    public static IReadOnlyList<MaskitBuiltinRule> Defaults =>
    [
        new("PRIVATE_KEY", @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", true),
        new("CONNSTR", @"(?<![A-Za-z0-9])(?:mongodb(?:\+srv)?|postgres(?:ql)?|mysql|redis|rediss|amqps?|mssql|sqlserver)://[^\s""'<>]+", true),
        new("JWT", @"(?<![A-Za-z0-9])eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}(?![A-Za-z0-9])", true),
        new("IDCARD", @"(?<!\d)\d{17}[\dXx](?!\d)", true),
        new("HKID", @"(?<![A-Z0-9])[A-Z]{1,2}\d{6}\([0-9A]\)", false),
        new("EMAIL", @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", true),
        new("PHONE", @"(?<!\d)1[3-9]\d{9}(?!\d)", true),
        new("LANDLINE", @"(?<!\d)0\d{2,3}-?\d{7,8}(?!\d)", true),
        new("PLATE", @"(?<![A-Z0-9])[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤青藏川宁琼][A-Z][A-Z0-9]{5}(?![A-Z0-9])", true),
        new("IP_PRIVATE", @"(?<!\d)(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})(?!\d)", true),
        new("IP_INTERNAL", @"(?<![\w.])(?:localhost|127\.0\.0\.1|0\.0\.0\.0|::1)(?![\w.])", false),
        new("IPV6_PRIVATE", @"(?<![\w:])(?:fc00|fd[0-9a-f]{2}|fe80)(?::[0-9a-fA-F]{0,4}){2,7}", false),
        new("IP_PUBLIC", @"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?!\d)", false),
        new("CARD", @"(?<!\d)\d{16,19}(?!\d)", true),
        new("IBAN", @"(?<![A-Z0-9])[A-Z]{2}\d{2}[A-Z0-9]{11,30}(?![A-Z0-9])", true),
        new("USCC", @"(?<![A-Z0-9])[0-9A-HJ-NPQRTUWXY]{2}\d{6}[0-9A-HJ-NPQRTUWXY]{10}(?![A-Z0-9])", false),
        new("MAC", @"(?<![A-Fa-f0-9:])(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}(?![A-Fa-f0-9:])", false),
        new("API_KEY", @"(?<![A-Za-z0-9])(?:api[_-]?key|x-api-key)[""'\s:=]+[A-Za-z0-9._-]{8,}", true),
        new("ACCESS_KEY", @"(?<![\w])(?:AKIA|ASIA|LTAI|AKID)[A-Z0-9]{8,24}(?![\w])", true),
        new("TOKEN", @"(?<![A-Za-z0-9])(?:access[_-]?token|token)[""'\s:=]+[A-Za-z0-9._-]{8,}", true),
        new("SECRET", @"(?<![A-Za-z0-9])(?:client[_-]?secret|app[_-]?secret|secret)[""'\s:=]+[A-Za-z0-9._-]{8,}", true),
    ];

    /// <summary>凭据类标签：对齐 maskit 红线，这类命中值绝不落到规则文件或统计里。</summary>
    public static readonly HashSet<string> CredentialKeys =
    [
        "API_KEY", "TOKEN", "SECRET", "ACCESS_KEY", "JWT", "CONNSTR", "PRIVATE_KEY",
    ];
}
