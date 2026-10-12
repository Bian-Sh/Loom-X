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