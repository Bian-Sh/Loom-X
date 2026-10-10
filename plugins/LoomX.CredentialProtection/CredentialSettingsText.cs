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
    string BaselineNameLabel,
    string BaselinePatternLabel,
    string EnableToggleLabel,
    string ButtonEdit,
    string ButtonDelete,
    string ButtonSave,
    string ButtonCancel,
    string BuiltinSectionTitle,
    string BuiltinSectionHint,
    string BaselineSectionTitle,
    string BaselineSectionHint,
    string GroupCoreTitle,
    string GroupCoreHint,
    string GroupCredentialsTitle,
    string GroupCredentialsHint,
    string GroupNetworkTitle,
    string GroupNetworkHint,
    string GroupEntitiesTitle,
    string GroupEntitiesHint,
    string ButtonAllOn,
    string ButtonAllOff,
    string RuleCountFormat,
    string PrefixSectionTitle,
    string PrefixSectionHint,
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
    string AllBuiltinsEnabled,
    string AllBuiltinsDisabled,
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
    Func<string, string> BuiltinNameResolver,
    Func<string, string> BuiltinHintResolver)
{
    public string BuiltinName(string key) => BuiltinNameResolver(key);

    /// <summary>规则卡片的第二行说明；缺省时只展示名称，不留空行。</summary>
    public string BuiltinHint(string key)
    {
        var hint = BuiltinHintResolver(key);
        return string.IsNullOrWhiteSpace(hint) ? string.Empty : hint;
    }

    public string GroupTitle(CredentialBuiltinGroup group) => group switch
    {
        CredentialBuiltinGroup.Core => GroupCoreTitle,
        CredentialBuiltinGroup.Credentials => GroupCredentialsTitle,
        CredentialBuiltinGroup.Network => GroupNetworkTitle,
        _ => GroupEntitiesTitle,
    };

    public string GroupHint(CredentialBuiltinGroup group) => group switch
    {
        CredentialBuiltinGroup.Core => GroupCoreHint,
        CredentialBuiltinGroup.Credentials => GroupCredentialsHint,
        CredentialBuiltinGroup.Network => GroupNetworkHint,
        _ => GroupEntitiesHint,
    };

    /// <summary>分组标题右侧的启用计数徽章，形如 <c>3/5</c>。</summary>
    public string RuleCount(int enabled, int total) =>
        string.Format(Culture, RuleCountFormat, enabled, total);

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
                "名稱", "正則", "字段名", "值形態", "啟用", "編輯", "刪除", "儲存", "取消",
                "內建形態規則", "內建形態規則只可開關，不可刪除；按場景分組後可快速取捨。公網 IP、IPv6、中國香港身分證等預設關閉以避免過度脫敏。",
                "內建識別底座", "引擎的字段名與憑據形態識別底座，與形態規則同樣只可開關；值形態恆打碼，字段名展示原值以便辨識。",
                "核心隱私與通用密鑰（建議預設）", "手機號、郵箱、證件與連線串等最常外洩的形態。",
                "擴展憑據與敏感形態（可選）", "雲端訪問密鑰、客戶端密鑰與令牌等賦值型憑據。",
                "網路與裝置標識（可選，易影響模型配置推理）", "IP、MAC 等位址形態；過度脫敏會讓模型看不懂網路配置。",
                "擴展證照與特定格式（可選）", "車牌、統一社會信用代碼、國際銀行帳號等。",
                "全部開啟", "全部關閉", "{0}/{1}",
                "自定義密鑰前綴", "配置 API Key、Token 等憑據前綴（如 sk-、ghp_、hf_）。命中後寫入 API_KEY 規則會攔截。逗號與下劃線（_）自動等價兼容，@ 可放在任意位置。前綴之後最長 8 位英文字符會被替換。",
                "密鑰前綴", "新增前綴", "尚無自定義前綴。",
                "規則已新增。", "規則已更新。", "規則已刪除。", "規則已啟用。", "規則已停用。", "規則不存在。",
                "內建規則已開啟。", "內建規則已關閉。", "內建規則不存在。",
                "內建規則已全部開啟。", "內建規則已全部關閉。",
                "前綴已新增。", "前綴已刪除。", "前綴不存在。",
                "正則格式無效。", "規則值無效。", "規則已存在。", "規則數量已達上限。",
                "前綴無效。", "前綴已存在。", "前綴數量已達上限。", "操作未完成。",
                BuiltinNamesZhTw, BuiltinHintsZhTw);
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
                "名前", "正規表現", "フィールド名", "値形態", "有効", "編集", "削除", "保存", "キャンセル",
                "組み込み形態ルール", "組み込み形態ルールは有効化のみで削除はできません。シナリオ別にグループ化しました。公開 IP、IPv6、中国香港身分証などは過剰なマスキングを避けるため既定で無効です。",
                "組み込み識別基盤", "エンジンのフィールド名と資格情報形態の識別基盤。形態ルールと同じく有効化のみ。値形態は常にマスクされます。",
                "中核プライバシーと一般シークレット（推奨）", "電話番号・メール・身分証・接続文字列など、漏られよりい形態が最も多い。",
                "追加資格情報と機密形態（任意）", "クラウドアクセスキー、クライアントシークレット、トークンなどの代入型資格情報。",
                "ネットワークとデバイス識別子（任意・モデル推論に影響）", "IP・MAC などのアドレス形態。過剰なマスキングはネットワーク設定の理解を損ないます。",
                "追加証票と特定形式（任意）", "ナンバープレート、統一社会信用コード、IBAN など。",
                "すべて有効", "すべて無効", "{0}/{1}",
                "カスタムシークレット接頭辞", "API Key・Token などの資格情報接頭辞（sk-、ghp_、hf_ など）を設定します。API_KEY ルールに書き込まれた場合もキャプチャされます。読点とアンダースコア（_）は自動的に等価、@ は任意位置に配置可。接頭辞の後の最長 8 文字が置換されます。",
                "シークレット接頭辞", "接頭辞を追加", "カスタム接頭辞はまだありません。",
                "ルールを追加しました。", "ルールを更新しました。", "ルールを削除しました。", "ルールを有効にしました。", "ルールを無効にしました。", "ルールが見つかりません。",
                "組み込みルールを有効にしました。", "組み込みルールを無効にしました。", "組み込みルールが見つかりません。",
                "組み込みルールをすべて有効にしました。", "組み込みルールをすべて無効にしました。",
                "接頭辞を追加しました。", "接頭辞を削除しました。", "接頭辞が見つかりません。",
                "正規表現の形式が不正です。", "ルール値が不正です。", "ルールは既に存在します。", "ルール数が上限に達しています。",
                "接頭辞が不正です。", "接頭辞は既に存在します。", "接頭辞数が上限に達しています。", "操作を完了できませんでした。",
                BuiltinNamesJa, BuiltinHintsJa);
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
                "名称", "正则", "字段名", "值形态", "启用", "编辑", "删除", "保存", "取消",
                "内置形态规则", "内置形态规则只可开关，不可删除；已按场景分组便于取舍。公网 IP、IPv6、中国香港身份证等默认关闭以避免过度脱敏。",
                "内置识别底座", "引擎的字段名与凭据形态识别底座，与形态规则同样只可开关；值形态恒打码，字段名展示原值以便辨认。",
                "核心隐私与通用密钥（推荐默认）", "手机号、邮箱、证件与连接串等最常外泄的形态。",
                "扩展凭据与敏感形态（可选）", "云访问密钥、客户端密钥与访问令牌等赋值型凭据。",
                "网络与设备标识（可选，易影响模型配置推理）", "IP、MAC 等地址形态；过度脱敏会让模型看不懂网络配置。",
                "扩展证照与特定格式（可选）", "车牌、统一社会信用代码、国际银行账号等。",
                "全部开启", "全部关闭", "{0}/{1}",
                "自定义密钥前缀", "配置 API Key、Token 等凭据前缀（如 sk-、ghp_、hf_）。命中后写入 API_KEY 规则会拦截。逗号与下划线（_）自动等价兼容，@ 可放在任意位置。前缀之后最长 8 位英文字符会被替换。",
                "密钥前缀", "新增前缀", "尚无自定义前缀。",
                "规则已新增。", "规则已更新。", "规则已删除。", "规则已启用。", "规则已停用。", "规则不存在。",
                "内置规则已开启。", "内置规则已关闭。", "内置规则不存在。",
                "内置规则已全部开启。", "内置规则已全部关闭。",
                "前缀已新增。", "前缀已删除。", "前缀不存在。",
                "正则格式无效。", "规则值无效。", "规则已存在。", "规则数量已达上限。",
                "前缀无效。", "前缀已存在。", "前缀数量已达上限。", "操作未完成。",
                BuiltinNamesZh, BuiltinHintsZh);
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
            "Name", "Regex", "Field name", "Value shape", "Enable", "Edit", "Delete", "Save", "Cancel",
            "Built-in shape rules", "Built-in shape rules can only be toggled, not deleted, and are grouped by scenario for quick decisions. Public IP, IPv6, and Hong Kong China ID are off by default to avoid over-masking.",
            "Built-in detection baseline", "The engine's field-name and secret-shape detection baseline. Toggled like shape rules; value shapes are always masked, field names are shown as-is.",
            "Core privacy and common secrets (recommended)", "Phone numbers, emails, IDs and connection strings — the shapes that leak most often.",
            "Extended credentials and secret shapes (optional)", "Cloud access keys, client secrets and tokens assigned as key=value.",
            "Network and device identifiers (optional, may affect model config reasoning)", "IP and MAC address shapes; over-masking hides network configuration from the model.",
            "Extended IDs and specific formats (optional)", "License plates, unified social credit codes, IBAN and similar.",
            "All on", "All off", "{0}/{1}",
            "Custom secret prefixes", "Configure prefixes for API keys and tokens such as sk-, ghp_, hf_. Values written with these prefixes are captured by the API_KEY rule. Commas and underscores (_) are equivalent; @ may sit anywhere. Up to 8 characters after the prefix are replaced.",
            "Secret prefix", "Add prefix", "No custom prefixes yet.",
            "Rule added.", "Rule updated.", "Rule deleted.", "Rule enabled.", "Rule disabled.", "Rule not found.",
            "Built-in rule enabled.", "Built-in rule disabled.", "Built-in rule not found.",
            "All built-in rules enabled.", "All built-in rules disabled.",
            "Prefix added.", "Prefix deleted.", "Prefix not found.",
            "Invalid regex pattern.", "Invalid rule value.", "The rule already exists.", "Rule limit reached.",
            "Invalid prefix.", "The prefix already exists.", "Prefix limit reached.", "The operation did not complete.",
            BuiltinNamesEn, BuiltinHintsEn);
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

    private static string BuiltinHintsZh(string key) => key switch
    {
        "PRIVATE_KEY" => "PEM 私钥（整块替换）",
        "CONNSTR" => "连接串（scheme://user:pass@host）",
        "JWT" => "eyJ… 三段式令牌",
        "IDCARD" => "18 位身份证号",
        "HKID" => "中国香港身份证（默认关防误伤）",
        "EMAIL" => "邮箱地址",
        "PHONE" => "手机号（含 +86、空格分隔）",
        "LANDLINE" => "座机（区号 + 7-8 位号码）",
        "PLATE" => "车牌（普通 5 位 / 新能源 6 位）",
        "IP_PRIVATE" => "内网 IP：192.168.x / 10.x / 172.16-31.x",
        "IP_INTERNAL" => "localhost / 127.0.0.1 / ::1（默认关防误伤）",
        "IPV6_PRIVATE" => "fe80:: 链路本地 / fc00:: ULA（默认关）",
        "IP_PUBLIC" => "公网 IPv4（默认关，易与四段版本号互斥）",
        "CARD" => "16-19 位银行卡号",
        "IBAN" => "国际银行账号（mod-97）",
        "USCC" => "统一社会信用代码（默认关防误伤）",
        "MAC" => "xx:xx:xx:xx:xx:xx（默认关防误伤）",
        "API_KEY" => "api_key= / x-api-key: 赋值形态",
        "ACCESS_KEY" => "AKIA / ASIA / LTAI 云访问密钥",
        "TOKEN" => "token= / access_token= 赋值形态",
        "SECRET" => "secret= / client_secret= 赋值形态",
        _ => string.Empty,
    };

    private static string BuiltinHintsZhTw(string key) => key switch
    {
        "PRIVATE_KEY" => "PEM 私鑰（整塊替換）",
        "CONNSTR" => "連線字串（scheme://user:pass@host）",
        "JWT" => "eyJ… 三段式權杖",
        "IDCARD" => "18 位身分證號",
        "HKID" => "中國香港身分證（預設關防誤傷）",
        "EMAIL" => "電子郵件地址",
        "PHONE" => "手機號（含 +86、空白分隔）",
        "LANDLINE" => "座機（區號 + 7-8 位號碼）",
        "PLATE" => "車牌（普通 5 位 / 新能源 6 位）",
        "IP_PRIVATE" => "內網 IP：192.168.x / 10.x / 172.16-31.x",
        "IP_INTERNAL" => "localhost / 127.0.0.1 / ::1（預設關防誤傷）",
        "IPV6_PRIVATE" => "fe80:: 鏈路本地 / fc00:: ULA（預設關）",
        "IP_PUBLIC" => "公開 IPv4（預設關，易與四段版本號互斥）",
        "CARD" => "16-19 位銀行卡號",
        "IBAN" => "國際銀行帳號（mod-97）",
        "USCC" => "統一社會信用代碼（預設關防誤傷）",
        "MAC" => "xx:xx:xx:xx:xx:xx（預設關防誤傷）",
        "API_KEY" => "api_key= / x-api-key: 指派形態",
        "ACCESS_KEY" => "AKIA / ASIA / LTAI 雲端存取密鑰",
        "TOKEN" => "token= / access_token= 指派形態",
        "SECRET" => "secret= / client_secret= 指派形態",
        _ => string.Empty,
    };

    private static string BuiltinHintsJa(string key) => key switch
    {
        "PRIVATE_KEY" => "PEM 秘密鍵（全体を置換）",
        "CONNSTR" => "接続文字列（scheme://user:pass@host）",
        "JWT" => "eyJ… の 3 段トークン",
        "IDCARD" => "18 桁の身分証番号",
        "HKID" => "中国香港身分証（既定で無効）",
        "EMAIL" => "メールアドレス",
        "PHONE" => "携帯電話番号（+86・空白区切り対応）",
        "LANDLINE" => "固定電話（市外局番 + 7-8 桁）",
        "PLATE" => "ナンバープレート（5 桁 / 新能源 6 桁）",
        "IP_PRIVATE" => "プライベート IP：192.168.x / 10.x / 172.16-31.x",
        "IP_INTERNAL" => "localhost / 127.0.0.1 / ::1（既定で無効）",
        "IPV6_PRIVATE" => "fe80:: リンクローカル / fc00:: ULA（既定で無効）",
        "IP_PUBLIC" => "公開 IPv4（既定で無効、4 桁バージョン番号と互相斥）",
        "CARD" => "16-19 桁のカード番号",
        "IBAN" => "IBAN（mod-97）",
        "USCC" => "統一社会信用コード（既定で無効）",
        "MAC" => "xx:xx:xx:xx:xx:xx（既定で無効）",
        "API_KEY" => "api_key= / x-api-key: の代入形",
        "ACCESS_KEY" => "AKIA / ASIA / LTAI クラウドアクセスキー",
        "TOKEN" => "token= / access_token= の代入形",
        "SECRET" => "secret= / client_secret= の代入形",
        _ => string.Empty,
    };

    private static string BuiltinHintsEn(string key) => key switch
    {
        "PRIVATE_KEY" => "PEM private key (whole block replaced)",
        "CONNSTR" => "Connection string (scheme://user:pass@host)",
        "JWT" => "Three-segment eyJ… token",
        "IDCARD" => "18-digit national ID",
        "HKID" => "Hong Kong China ID (off by default)",
        "EMAIL" => "Email address",
        "PHONE" => "Mobile number (+86 and spaced forms included)",
        "LANDLINE" => "Landline (area code + 7-8 digits)",
        "PLATE" => "License plate (5 digits / 6 for new energy)",
        "IP_PRIVATE" => "Private IP: 192.168.x / 10.x / 172.16-31.x",
        "IP_INTERNAL" => "localhost / 127.0.0.1 / ::1 (off by default)",
        "IPV6_PRIVATE" => "fe80:: link-local / fc00:: ULA (off by default)",
        "IP_PUBLIC" => "Public IPv4 (off by default, overlaps dotted versions)",
        "CARD" => "16-19 digit bank card number",
        "IBAN" => "IBAN (mod-97)",
        "USCC" => "Unified social credit code (off by default)",
        "MAC" => "xx:xx:xx:xx:xx:xx (off by default)",
        "API_KEY" => "api_key= / x-api-key: assignment form",
        "ACCESS_KEY" => "AKIA / ASIA / LTAI cloud access keys",
        "TOKEN" => "token= / access_token= assignment form",
        "SECRET" => "secret= / client_secret= assignment form",
        _ => string.Empty,
    };
}
