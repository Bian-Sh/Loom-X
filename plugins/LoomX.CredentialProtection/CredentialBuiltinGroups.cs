namespace LoomX.CredentialProtection;

/// <summary>
/// 内置形态规则的分组归属。分组只影响设置页的呈现顺序与标题，
/// 不参与脱敏语义：真正的脱敏顺序仍由 <see cref="CredentialBuiltinRules.Defaults"/> 的声明顺序决定。
/// </summary>
public enum CredentialBuiltinGroup
{
    /// <summary>核心隐私与通用密钥。</summary>
    Core,

    /// <summary>扩展凭据与敏感形态。</summary>
    Credentials,

    /// <summary>网络与设备标识。</summary>
    Network,

    /// <summary>扩展证照与特定格式。</summary>
    Entities,
}

/// <summary>内置形态规则的分组元数据（顺序即设置页展示顺序）。</summary>
internal static class CredentialBuiltinGroups
{
    public static readonly IReadOnlyList<CredentialBuiltinGroup> Order =
    [
        CredentialBuiltinGroup.Core,
        CredentialBuiltinGroup.Credentials,
        CredentialBuiltinGroup.Network,
        CredentialBuiltinGroup.Entities,
    ];

    public static CredentialBuiltinGroup GroupOf(string key) => key switch
    {
        "PHONE" or "EMAIL" or "IDCARD" or "LANDLINE" or "CARD" or "API_KEY" or "CONNSTR"
            => CredentialBuiltinGroup.Core,
        "ACCESS_KEY" or "PRIVATE_KEY" or "SECRET" or "TOKEN" or "JWT"
            => CredentialBuiltinGroup.Credentials,
        "IP_PRIVATE" or "IP_INTERNAL" or "IP_PUBLIC" or "IPV6_PRIVATE" or "MAC"
            => CredentialBuiltinGroup.Network,
        _ => CredentialBuiltinGroup.Entities,
    };
}