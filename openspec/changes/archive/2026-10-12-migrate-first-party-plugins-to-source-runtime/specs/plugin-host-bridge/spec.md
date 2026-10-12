## ADDED Requirements

### Requirement: Protection Bridge 提供最小宿主加解密能力
宿主 SHALL 提供 `protection/v1`，以异步字符串 Encrypt/Decrypt 和 CancellationToken 表达能力。插件 MUST 显式在敏感值写库前加密、读出密文后解密；宿主不得声称任意 SQL 自动加密。数据库文件、SQL Model、表列、索引、事务、去重和 Token 业务由插件拥有；算法、密钥、平台后端、密文版本及历史兼容由宿主拥有，不开放算法、主密钥或关闭加密参数。

#### Scenario: 插件组合存储与保护
- **WHEN** 已授权插件把敏感值持久化
- **THEN** 宿主返回不含明文的版本化密文，插件自行使用参数化 SQL 保存并能显式解密

#### Scenario: 不受支持平台
- **WHEN** 平台保护后端不可用
- **THEN** 操作明确安全失败，不以明文降级，也不承诺 Windows 历史密文跨平台解密

### Requirement: Protection 授权与生命周期保持兼容
宿主 MUST 按插件身份绑定保护上下文，检查声明、版本、取消及退休状态。旧公共 Bridge 实现和旧构造 SHALL 保持源码兼容，新增成员默认 unavailable。同进程能力代理 MUST NOT 被描述为恶意代码沙箱。

#### Scenario: 旧 Bridge 实现
- **WHEN** 旧实现只提供 Storage、Log、Notifications
- **THEN** 实现仍可编译，Protection 返回能力不可用安全错误

#### Scenario: 未声明或退休调用
- **WHEN** 插件未声明 protection/v1 或其实例已退休
- **THEN** 宿主拒绝调用并返回稳定错误，不执行保护后端

#### Scenario: 已取消调用
- **WHEN** 保护调用使用已取消令牌
- **THEN** 调用响应取消而不执行加解密

### Requirement: 保护失败不得泄密或猜测明文
保护边界 MUST 拒绝未知、损坏、不能解密或非密文输入；异常和日志 MUST NOT 包含明文、密文、Provider正文、凭据、SQL参数或底层异常敏感消息。Windows 后端 SHALL 兼容凭据 Vault 的 `dpapi:v1:`、CurrentUser/null entropy 历史密文，不能与配置保护的 `dpapi:` 混淆。

#### Scenario: 历史密文恢复
- **WHEN** 插件读取既有 credential-tokens.db 的合法 dpapi:v1: 值
- **THEN** 宿主恢复相同原值，无需重建数据库或修改 token

#### Scenario: 非密文或损坏密文
- **WHEN** 插件传入明文、未知版本或损坏密文
- **THEN** 返回不含输入和底层敏感异常的失败，不把输入原样返回