using System.Globalization;

namespace LoomX.CredentialProtection;

/// <summary>凭据保护设置页的多语言文案。凭据值本身绝不出现在这里。</summary>
internal sealed record CredentialSettingsText(
    CultureInfo Culture,
    string AddSectionTitle,
    string AddSectionHint,
    string FieldValue,
    string FieldKind,
    string KindName,
    string KindPattern,
    string ButtonAdd,
    string ProtectionTitle,
    string StateActive,
    string StateInactive,
    string ProtectionBody,
    string ProtectionKeepsTitle,
    string ProtectionKeepsBody,
    string ProtectionRiskTitle,
    string ProtectionRiskBody,
    string RulesSectionTitle,
    string EmptyRulesHint,
    string MoreRulesHint,
    string TagName,
    string TagPattern,
    string EnableToggleLabel,
    string ButtonEdit,
    string ButtonDelete,
    string ButtonSave,
    string ButtonCancel,
    string BuiltinSectionTitle,
    string BuiltinSectionHint,
    string PrefixSectionTitle,
    string FieldPrefix,
    string ButtonAddPrefix,
    string EmptyPrefixHint,
    string RuleAdded,
    string RuleUpdated,
    string RuleDeleted,
    string RuleEnabled,
    string RuleDisabled,
    string RuleNotFound,
    string BuiltinEnabled,
    string BuiltinDisabled,
    string BuiltinNotFound,
    string PrefixAdded,
    string PrefixDeleted,
    string PrefixNotFound,
    string ErrorInvalidPattern,
    string ErrorInvalidValue,
    string ErrorDuplicateRule,
    string ErrorTooManyRules,
    string ErrorInvalidPrefix,
    string ErrorDuplicatePrefix,
    string ErrorTooManyPrefixes,
    string ErrorUnknown,
    Func<string, string> BuiltinNameResolver)
{
    public string BuiltinName(string key) => BuiltinNameResolver(key);

    public static CredentialSettingsText Resolve(string cultureName)
    {
        var normalized = cultureName.Trim().ToLowerInvariant();
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }

        if (normalized.StartsWith("zh-tw", StringComparison.Ordinal)
            || normalized.StartsWith("zh-hk", StringComparison.Ordinal))
        {
            return new(
                culture,
                "新增自定義規則", "規則值可為敏感欄位名稱或值形態正則；命中後一律替換為本地可恢復的引用。",
                "規則值", "形態", "名稱", "正則", "新增規則",
                "主動保護狀態", "保護中", "已停用",
                "停用後不再偵測與替換新送出的明文內容。",
                "停用後仍然保留", "歷史會話中已簽發的憑據引用仍會被識別，並依舊注入完整性指令、於回應中還原。",
                "停用的代價", "新送出的請求可能包含未經處理的憑據明文，直接發送給 Provider。",
                "自定義規則", "尚無自定義規則。",
                "僅顯示前 {0} 條，共 {1} 條。",
                "名稱", "正則", "啟用", "編輯", "刪除", "儲存", "取消",
                "內建形態規則", "內建形態規則只可開關，不可刪除；公網 IP、IPv6、中國香港身分證等預設關閉以避免過度脫敏。",
                "自定義密鑰前綴", "密鑰前綴", "新增前綴", "尚無自定義前綴。",
                "規則已新增。", "規則已更新。", "規則已刪除。", "規則已啟用。", "規則已停用。", "規則不存在。",
                "內建規則已開啟。", "內建規則已關閉。", "內建規則不存在。",
                "前綴已新增。", "前綴已刪除。", "前綴不存在。",
                "正則格式無效。", "規則值無效。", "規則已存在。", "規則數量已達上限。",
                "前綴無效。", "前綴已存在。", "前綴數量已達上限。", "操作未完成。",
                BuiltinNamesZhTw);
        }

        if (normalized.StartsWith("ja", StringComparison.Ordinal))
        {
            return new(
                culture,
                "カスタムルールを追加", "ルール値は機密フィールド名または値形態の正規表現です。命中した値は必ずローカルで復元可能な参照に置き換わります。",
                "ルール値", "形態", "名前", "正規表現", "ルールを追加",
                "能動保護の状態", "保護中", "停止中",
                "停止後は、新しく送信する平文の検出と置換を行いません。",
                "停止後も維持されるもの", "過去のセッションで発行された資格情報参照は引き続き識別され、完全性指示の注入と応答での復元が行われます。",
                "停止の代償", "新しく送信するリクエストに未処理の資格情報平文が含まれ、Provider へ直接送信される可能性があります。",
                "カスタムルール", "カスタムルールはまだありません。",
                "先頭 {0} 件のみ表示、全 {1} 件。",
                "名前", "正規表現", "有効", "編集", "削除", "保存", "キャンセル",
                "組み込み形態ルール", "組み込み形態ルールは有効化のみ可能で削除はできません。公開 IP、IPv6、中国香港身分証などは過剰なマスキングを避けるため既定で無効です。",
                "カスタムシークレット接頭辞", "シークレット接頭辞", "接頭辞を追加", "カスタム接頭辞はまだありません。",
                "ルールを追加しました。", "ルールを更新しました。", "ルールを削除しました。", "ルールを有効にしました。", "ルールを無効にしました。", "ルールが見つかりません。",
                "組み込みルールを有効にしました。", "組み込みルールを無効にしました。", "組み込みルールが見つかりません。",
                "接頭辞を追加しました。", "接頭辞を削除しました。", "接頭辞が見つかりません。",
                "正規表現の形式が不正です。", "ルール値が不正です。", "ルールは既に存在します。", "ルール数が上限に達しています。",
                "接頭辞が不正です。", "接頭辞は既に存在します。", "接頭辞数が上限に達しています。", "操作を完了できませんでした。",
                BuiltinNamesJa);
        }

        if (normalized.StartsWith("zh", StringComparison.Ordinal))
        {
            return new(
                culture,
                "新增自定义规则", "规则值可为敏感字段名称或值形态正则；命中后一律替换为本地可恢复的引用。",
                "规则值", "形态", "名称", "正则", "新增规则",
                "主动保护状态", "保护中", "已停用",
                "停用后不再检测与替换新送出的明文内容。",
                "停用后仍然保留", "历史会话中已签发的凭据引用仍会被识别，并依旧注入完整性指令、在响应中还原。",
                "停用的代价", "新送出的请求可能包含未经处理的凭据明文，直接发送给 Provider。",
                "自定义规则", "尚无自定义规则。",
                "仅显示前 {0} 条，共 {1} 条。",
                "名称", "正则", "启用", "编辑", "删除", "保存", "取消",
                "内置形态规则", "内置形态规则只可开关，不可删除；公网 IP、IPv6、中国香港身份证等默认关闭以避免过度脱敏。",
                "自定义密钥前缀", "密钥前缀", "新增前缀", "尚无自定义前缀。",
                "规则已新增。", "规则已更新。", "规则已删除。", "规则已启用。", "规则已停用。", "规则不存在。",
                "内置规则已开启。", "内置规则已关闭。", "内置规则不存在。",
                "前缀已新增。", "前缀已删除。", "前缀不存在。",
                "正则格式无效。", "规则值无效。", "规则已存在。", "规则数量已达上限。",
                "前缀无效。", "前缀已存在。", "前缀数量已达上限。", "操作未完成。",
                BuiltinNamesZh);
        }

        return new(
            culture,
            "Add custom rule", "A rule value is either a sensitive field name or a value-shape regex. Matches are always replaced with a locally restorable reference.",
            "Rule value", "Shape", "Name", "Regex", "Add rule",
            "Active protection", "Protecting", "Disabled",
            "When disabled, newly sent plaintext is no longer detected or replaced.",
            "Still available after disabling", "Credential references issued in past sessions are still recognised, still receive the integrity instruction, and are still restored in responses.",
            "Cost of disabling", "Newly sent requests may contain unprocessed credential plaintext sent straight to the Provider.",
            "Custom rules", "No custom rules yet.",
            "Showing the first {0} of {1}.",
            "Name", "Regex", "Enable", "Edit", "Delete", "Save", "Cancel",
            "Built-in shape rules", "Built-in shape rules can only be toggled, not deleted. Public IP, IPv6, and Hong Kong China ID are off by default to avoid over-masking.",
            "Custom secret prefixes", "Secret prefix", "Add prefix", "No custom prefixes yet.",
            "Rule added.", "Rule updated.", "Rule deleted.", "Rule enabled.", "Rule disabled.", "Rule not found.",
            "Built-in rule enabled.", "Built-in rule disabled.", "Built-in rule not found.",
            "Prefix added.", "Prefix deleted.", "Prefix not found.",
            "Invalid regex pattern.", "Invalid rule value.", "The rule already exists.", "Rule limit reached.",
            "Invalid prefix.", "The prefix already exists.", "Prefix limit reached.", "The operation did not complete.",
            BuiltinNamesEn);
    }

    private static string BuiltinNamesZh(string key) => key switch
    {
        "PRIVATE_KEY" => "私钥",
        "CONNSTR" => "数据库连接串",
        "JWT" => "JWT 令牌",
        "IDCARD" => "身份证号",
        "HKID" => "中国香港身份证",
        "EMAIL" => "电子邮箱",
        "PHONE" => "手机号",
        "LANDLINE" => "固定电话",
        "PLATE" => "车牌号",
        "IP_PRIVATE" => "私有 IP",
        "IP_INTERNAL" => "本机地址",
        "IPV6_PRIVATE" => "私有 IPv6",
        "IP_PUBLIC" => "公网 IP",
        "CARD" => "银行卡号",
        "IBAN" => "国际银行账号",
        "USCC" => "统一社会信用代码",
        "MAC" => "MAC 地址",
        "API_KEY" => "API Key",
        "ACCESS_KEY" => "云访问密钥",
        "TOKEN" => "访问令牌",
        "SECRET" => "客户端密钥",
        _ => key,
    };

    private static string BuiltinNamesZhTw(string key) => key switch
    {
        "PRIVATE_KEY" => "私鑰",
        "CONNSTR" => "資料庫連線字串",
        "JWT" => "JWT 權杖",
        "IDCARD" => "身分證號",
        "HKID" => "中國香港身分證",
        "EMAIL" => "電子郵件",
        "PHONE" => "手機號",
        "LANDLINE" => "固定電話",
        "PLATE" => "車牌號",
        "IP_PRIVATE" => "私有 IP",
        "IP_INTERNAL" => "本機位址",
        "IPV6_PRIVATE" => "私有 IPv6",
        "IP_PUBLIC" => "公開 IP",
        "CARD" => "銀行卡號",
        "IBAN" => "國際銀行帳號",
        "USCC" => "統一社會信用代碼",
        "MAC" => "MAC 位址",
        "API_KEY" => "API Key",
        "ACCESS_KEY" => "雲端存取金鑰",
        "TOKEN" => "存取權杖",
        "SECRET" => "用戶端金鑰",
        _ => key,
    };

    private static string BuiltinNamesJa(string key) => key switch
    {
        "PRIVATE_KEY" => "秘密鍵",
        "CONNSTR" => "DB 接続文字列",
        "JWT" => "JWT トークン",
        "IDCARD" => "身分証番号",
        "HKID" => "中国香港身分証",
        "EMAIL" => "メールアドレス",
        "PHONE" => "携帯電話番号",
        "LANDLINE" => "固定電話",
        "PLATE" => "ナンバープレート",
        "IP_PRIVATE" => "プライベート IP",
        "IP_INTERNAL" => "ローカルアドレス",
        "IPV6_PRIVATE" => "プライベート IPv6",
        "IP_PUBLIC" => "公開 IP",
        "CARD" => "カード番号",
        "IBAN" => "IBAN",
        "USCC" => "統一社会信用コード",
        "MAC" => "MAC アドレス",
        "API_KEY" => "API キー",
        "ACCESS_KEY" => "クラウドアクセスキー",
        "TOKEN" => "アクセストークン",
        "SECRET" => "クライアントシークレット",
        _ => key,
    };

    private static string BuiltinNamesEn(string key) => key switch
    {
        "PRIVATE_KEY" => "Private key",
        "CONNSTR" => "Database connection string",
        "JWT" => "JWT",
        "IDCARD" => "National ID",
        "HKID" => "Hong Kong China ID",
        "EMAIL" => "Email address",
        "PHONE" => "Mobile number",
        "LANDLINE" => "Landline",
        "PLATE" => "License plate",
        "IP_PRIVATE" => "Private IP",
        "IP_INTERNAL" => "Local address",
        "IPV6_PRIVATE" => "Private IPv6",
        "IP_PUBLIC" => "Public IP",
        "CARD" => "Bank card number",
        "IBAN" => "IBAN",
        "USCC" => "Unified social credit code",
        "MAC" => "MAC address",
        "API_KEY" => "API key",
        "ACCESS_KEY" => "Cloud access key",
        "TOKEN" => "Access token",
        "SECRET" => "Client secret",
        _ => key,
    };
}