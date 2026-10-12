# Comet Design Handoff

- Change: migrate-first-party-plugins-to-source-runtime
- Phase: design
- Mode: compact
- Context hash: 355c8c0ac421f318a160371b6e24972b9df2670d4753152e95710cf97b6943a6

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/migrate-first-party-plugins-to-source-runtime/proposal.md

- Source: openspec/changes/migrate-first-party-plugins-to-source-runtime/proposal.md
- Lines: 1-34
- SHA256: bbe51f6fdfffaa2f480ed9f7898c39c647241a4d61fdc5728dc4945b53a0e9e5

```md
## Why

两个第一方插件仍依赖 assembly runtime，凭据插件直接引用 Windows DPAPI，无法由既有源码 Runtime 在受控公共引用集下编译。本 change 完成源码迁移，并把加解密基础设施移回宿主，同时保留长期 placeholder 和历史 Vault。

## What Changes

- 新增 `bridge.Protection` 与 `protection/v1` 授权，仅提供异步字符串加密、解密；宿主负责算法、平台、密钥和密文版本。
- 保留旧 Bridge 实现与构造兼容，未提供能力默认 unavailable；实例退休后禁止调用。
- 凭据插件以 Storage + Protection 组合保留自己的 SQL、数据库文件、事务、去重和 token 业务，移除 ProtectedData 引用。
- 凭据保护和工具结果压缩均改用现有显式 `sources`、`entry_type` 源码 manifest；Build、Publish、测试与 Playground 使用同一源码负载布局。
- 验证真实源码编译、初始化、Pipeline、重载、旧 Vault 和正式发布包，不以静态测试替代动态验收。

## Capabilities

### New Capabilities
- `first-party-source-plugins`: 两个第一方源码包的负载和动态行为约束。

### Modified Capabilities
- `plugin-host-bridge`: 增加薄 Protection 能力、授权、生命周期和历史密文兼容。

## Impact

涉及公共契约、PluginHost、宿主/Playground组合点、两个插件、负载目标和相关测试。Windows 使用 DPAPI CurrentUser/null entropy，兼容 `dpapi:v1:`；不实现 Mac/Linux 后端，不承诺 Windows 密文跨平台可解。不同于配置库的 `dpapi:`。

## 已确认范围与执行方式

沿用既有工作树及分支，`current` 指此隔离目录；不修改主目录，不新建工作树，不自动合并 master。执行 autonomous + TDD + standard 独立审查，Runtime/Feedback 各允许一次定向修复复查。此文档落实交接中已确认决定，不新增凭据 CRUD、统一 Vault、SQL 自动加密、算法/密钥开关或恶意代码沙箱。两插件迁移是一个交付目标，先能力后凭据再压缩；保留其他会话产物和既存 evidence。

## 验收摘要

- 旧公共 Bridge 和旧构造可编译；未知/未声明/退休能力安全失败，取消有效。
- 明文不回退、非密文不回传、异常日志不泄密；历史 DPAPI 密文和原表/Token可恢复。
- 两个实际源码包经 SourcePluginCompiler/Runtime 完成加载、Pipeline和重载。
- 发布仅含显式源码、manifest、四语言文案；插件目录无旧 DLL/deps/SQLite/crypto依赖。
- 定向及相关完整测试通过，重新发布到可读时间命名 outputs，完成独立集成审查、中文提交和推送。
```

## openspec/changes/migrate-first-party-plugins-to-source-runtime/design.md

- Source: openspec/changes/migrate-first-party-plugins-to-source-runtime/design.md
- Lines: 1-38
- SHA256: 1145b749e5c84563bd38412cda057dac203a9d33caff2b0b3928a72b09eaeb53

```md
---
comet_change: migrate-first-party-plugins-to-source-runtime
role: technical-design
canonical_spec: openspec
---

# 技术设计

## Context

动机与范围见 proposal.md。已归档平台提供 SourcePluginCompiler、SourcePluginRuntime、PluginBridgeScope 和 SQLite Bridge。两个插件仍声明 assembly，CredentialTokenStore 内部直接调用 ProtectedData。生产组合点为 PluginHostBridgeFactory，Playground另有组合点；不能只修改公共接口。

## Goals / Non-Goals

插件保有SQL和业务，宿主只增加字符串保护基础设施。沿用用户已批准的薄Protection方案；撤回统一Vault、宿主凭据CRUD和表列映射。不改变配置保护、不扩展Mac/Linux后端、不新增凭据算法或模型恢复规则。

## Decisions

1. `IPluginProtectionBridge` 只提供 `ValueTask<string> EncryptAsync(string plaintext, CancellationToken)` 与 `DecryptAsync(string ciphertext, CancellationToken)`。`IPluginBridge.Protection` 默认接口实现返回 unavailable，旧实现不必增成员。Scope构造在已有参数末尾追加可选protection，并注册精确`protection/v1`。授权、取消、退休沿用现有Check，释放时同对象只释放一次。
2. 宿主PluginHost提供Windows保护适配器，以不可变pluginId绑定上下文，平台失败返回安全错误。保留`dpapi:v1:`/CurrentUser/null entropy；不复用配置库`dpapi:`的非密文回退。底层异常不成为Bridge的InnerException，不记录输入、输出和具体异常消息；记录安全事件摘要。该同进程绑定不是恶意代码隔离。
3. CredentialTokenStore接受Storage + Protection，把原Protect/Unprotect替换成显式await。SQL、原表、哈希、冲突回滚、Token和销毁流程不变。静态测试与开发自测同步接入真实宿主保护，不把ProtectedData重新引回插件。
4. 两个manifest使用平台既有sources/entry_type。最小源码调整解决ImplicitUsings，不改变业务。开发静态项目继续用于单测，动态包仅拷显式源码、manifest/i18n；英文兜底按编译器已有资源能力处理。输出构建目标采用精确清单，不把插件bin递归打包。
5. 先宿主能力及其兼容测试，再凭据源码化，再压缩源码化；最终以真实SourcePluginCompiler/Runtime的加载、历史Vault、Pipeline与重载验证闭环。发布测试检查无依赖污染。产品重新发布到outputs的时间命名目录，UI只对完整产品做验收，透明主题截图不作为颜色依据。

## Risks / Trade-offs

- 旧Vault与长期Token失效 → 不改数据库文件名/表列/哈希/密文语义，用历史密文动态集成测试守护。
- 静态项目隐式using掩盖动态失败 → 使用真实源码编译和发布包验收。
- 平台或底层异常泄密 → 安全Bridge错误、禁止InnerException、敏感canary日志测试；不进行明文降级。
- 旧Bridge使用者断编译 → 默认接口实现与旧构造兼容测试。
- 旧bin目录混入发布 → 精确源码清单及正式publish契约测试，不删除历史outputs/evidence。

## Migration Plan

先实现并测试公共能力，再组合生产和Playground，迁移凭据与压缩源码负载。Windows库不进行任何数据迁移；回滚代码仍可读取同样dpapi:v1:密文。只有完成必要验证、standard独立审查和打包后才能归档，中文提交并推送当前分支，不自动合并master。

## 批准记录

2026-10-12 交接已确认：薄Protection设计、两个插件迁移范围、既有工作树/分支、current隔离、autonomous + TDD + standard；Runtime与Feedback各最多一次修复并由原审查者复查。此设计没有新增范围，因此沿用批准，不重复询问。新范围或真实设计漂移必须重新确认。
```

## openspec/changes/migrate-first-party-plugins-to-source-runtime/tasks.md

- Source: openspec/changes/migrate-first-party-plugins-to-source-runtime/tasks.md
- Lines: 1-18
- SHA256: f2e5c5b0fbdb6fe1f05e4d9422386139722b5ed82b3fd3c7ccba6639e7f80fd8

```md
## 1. 宿主 Protection

- [ ] 1.1 建立Protection契约、默认兼容、能力授权和退休测试并实现Scope。 <!-- comet-task:ddb0af83-e457-40da-940e-c5339e893511 -->
- [ ] 1.2 实现宿主Windows保护与生产/Playground组合，验证取消、失败、泄密与历史密文兼容。 <!-- comet-task:4e3aeb40-0929-4a21-89fc-d5b21ad89279 -->

## 2. 凭据保护源码化

- [ ] 2.1 将凭据TokenStore组合Storage/Protection，保留SQL与生命周期并通过历史Vault及冲突测试。 <!-- comet-task:9271a759-7110-47cf-99f6-51cb56e7b59b -->
- [ ] 2.2 将凭据插件改为显式源码负载，补齐动态编译与真实Runtime/Pipeline/重载验收。 <!-- comet-task:26e1c0e4-e1b1-443d-a969-6253a8fd4da9 -->

## 3. 工具结果压缩源码化

- [ ] 3.1 迁移工具结果压缩源码负载，验证真实动态压缩、配置与本地化行为。 <!-- comet-task:154ae18d-77ae-4e44-85d3-b85cd711dd82 -->

## 4. 交付闭环

- [ ] 4.1 统一Build/Publish/测试/Playground精确负载，验证正式发布布局与无旧依赖污染。 <!-- comet-task:a2b28ecd-5ac4-47ce-b016-934575f8a876 -->
- [ ] 4.2 完成相关完整测试、standard独立审查及必要定向修复复查，重新打包并完成产品验收。 <!-- comet-task:7b021879-ef1a-4925-9236-a5ac3ad473ed -->
- [ ] 4.3 整理Comet验证、归档与中文提交/当前分支推送所需交付资料，保留所有既存证据。 <!-- comet-task:376a839a-5174-4c26-aa6c-cfbc0affba52 -->
```

## openspec/changes/migrate-first-party-plugins-to-source-runtime/.openspec.yaml

- Source: openspec/changes/migrate-first-party-plugins-to-source-runtime/.openspec.yaml
- Lines: 1-2
- SHA256: 1dfdca412094f24473d8b842866201ec26f200b29b17064b03f1e4982c35a7b9

```md
schema: spec-driven
created: 2026-10-12

```

## openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/first-party-source-plugins/spec.md

- Source: openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/first-party-source-plugins/spec.md
- Lines: 1-44
- SHA256: 6db4f5d851c21871034f3c32457f16d0e22fe001a2d98f872c4634286bdd91e5

```md
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
```

## openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/plugin-host-bridge/spec.md

- Source: openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/plugin-host-bridge/spec.md
- Lines: 1-37
- SHA256: ea49990f595e67a68d6c3be7996e89a465f8eee413ba54a8a63aeccc9512abd5

```md
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
```
