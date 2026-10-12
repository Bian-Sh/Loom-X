## Purpose

定义凭据保护与工具结果压缩两个第一方插件从程序集负载迁移为源码负载的交付契约，保证正式发布、测试和开发环境采用相同负载与运行入口，并保留长期凭据引用、Pipeline安全边界与已有用户配置。

## ADDED Requirements

### Requirement: 两个第一方插件以源码 Runtime 执行
凭据保护和工具结果压缩 SHALL 使用现有 manifest 显式 sources 与入口类型，由正式源码编译器和 Runtime 加载。源码 MUST 不依赖 SDK 隐式 using、宿主私有类型或插件私有 SQLite/ProtectedData 编译引用；静态程序集单测不能替代真实源码包验收。

#### Scenario: 编译并注册两个源码插件
- **WHEN** 正式 Runtime 发现两个第一方源码包
- **THEN** 源码编译成功并按既有扩展声明进入对应 Request/Response Pipeline

#### Scenario: 重载源码实例
- **WHEN** Runtime 重载插件
- **THEN** 新实例可运行，旧实例排空并退休，数据保留且旧句柄不可复用

### Requirement: 源码负载在所有工程中精确一致
Build、Publish、测试和 Playground SHALL 使用 plugins/<id>/plugin.manifest.json、显式源码和 i18n 四语言负载，默认英文嵌入规则保持有效。插件包 MUST 不包含旧业务 DLL/deps、SQLite托管或原生依赖、crypto依赖；不得通过 MSBuild Content 传播形成根目录平铺负载。

#### Scenario: 正式发布源码包
- **WHEN** 发布产品
- **THEN** 两插件目录含全部显式源码、manifest与四语言文案且无旧依赖，根目录无平铺 manifest/i18n

### Requirement: 凭据业务与历史 Vault 保持不变
凭据插件 MUST 继续拥有 credential-tokens.db、CredentialTokens(Token, OriginalHash, ProtectedOriginal, CreatedAtUtc)、SHA256 UTF8大写hex与长期Token；唯一冲突结束失败事务后精确查询旧映射。源码迁移 MUST 保留确定性恢复、跨chunk/SSE fail closed、必选system/developer完整性指令、普通禁用后历史恢复、卸载保留Vault及独立销毁确认，不修改外围协议语法。

#### Scenario: 动态插件恢复历史映射
- **WHEN** 已有原表结构、长期Token与历史Windows密文
- **THEN** 源码实例直接复用原库并恢复相同原值，不产生第二份Vault

#### Scenario: 暂停主动保护
- **WHEN** 用户普通禁用凭据保护
- **THEN** 历史引用仍可恢复，不产生新明文token化且保留强警告

#### Scenario: 存储或解密失败
- **WHEN** Vault或Protection返回不可恢复错误
- **THEN** 插件保持fail closed，不输出可能含敏感明文的数据

### Requirement: 工具结果压缩行为保持兼容
源码迁移 SHALL 保留现有压缩、去重、截断、总结、设置和四语言文案，继续遵守既有continue-on-error策略与UI contribution契约，不引入新的业务算法或额外宿主能力。

#### Scenario: 真实源码压缩工具结果
- **WHEN** 请求包含可压缩历史工具结果
- **THEN** 源码插件按现有配置处理并保留Request Pipeline行为