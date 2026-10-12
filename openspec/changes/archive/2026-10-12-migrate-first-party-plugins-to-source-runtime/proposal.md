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