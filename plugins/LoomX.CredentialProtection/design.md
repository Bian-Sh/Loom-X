# Credential Protection 存储 Bridge 接线

本文件记录 task 5.1–5.6 的实现映射，不替代 `openspec/changes/add-source-plugin-runtime-and-bridge/` 与 `add-router-credential-protection` 的权威设计和规范。

## 公共契约与所有权

- Manifest 声明 `storage.sqlite/v1`；插件只引用 `LoomX.Plugin.Abstractions`，不引用宿主 SQLite 实现或提供程序。
- `CredentialTokenStore(IPluginStorageBridge)` 只打开相对路径 `credential-tokens.db`。异步初始化、创建、精确查询、恢复和释放均使用公共值 DTO。
- `CredentialEngine(SensitiveRuleStore, CredentialTokenStore)` 要求显式接线，不再按规则目录隐式创建数据库。
- 插件初始化从 `context.Bridge.Storage` 创建 Store，并完成初始化后才注册扩展。插件负责自己的 Store／数据库 handle；宿主负责 Bridge，插件不得释放整个宿主 Bridge。
- 普通停用只停止主动检测；Store 和历史 placeholder 恢复继续有效。实际退休取消在途操作、等待清理并释放 handle，保留 Vault 文件。

## Vault 兼容边界

- 保持 `CredentialTokens(Token, OriginalHash, ProtectedOriginal, CreatedAtUtc)`、主键和唯一索引不变。
- 初始化沿用原建表 SQL。原三条 PRAGMA 按 Bridge 单语句契约分别执行，值仍为 WAL、NORMAL、5000。
- `OriginalHash` 仍为原值 UTF-8 的 SHA256 大写十六进制；密文仍为 `dpapi:v1:` 加当前 Windows 用户、无额外熵的 DPAPI Base64。
- 不迁移、不重写旧记录、不创建第二个 Vault。唯一约束冲突先结束并释放失败事务，再以 `OriginalHash` 精确重查；不存在既有映射时才用新事务重试。

## 同步解析与失败策略

- `CredentialSynchronous` 是生产插件唯一同步等待点：在工作线程启动异步 Store 操作，避免 UI 同步上下文死锁。
- Engine 用调用级 `AsyncLocal<CancellationToken>` 作用域传递 Pipeline 取消，退出时恢复前一作用域；共享 Engine 的其他请求不受污染。
- Store 串行操作、链接调用取消和生命周期取消，异步等待不捕获同步上下文。取消或释放中的失败保持请求／响应 fail closed，输出只有安全摘要。
- JSON／SSE 的原解析算法、受限归一化和未知引用不猜测规则不变。普通文本未知引用按权威规范原样保留；流式未闭合引用的 fail closed 仍由宿主现有流式安全边界执行。

## 测试侧接线

- `CredentialTestFactory` 在测试侧创建真实宿主 `PluginSqliteStorageBridge`，向插件仅传公共接口；测试目录名称作为 Bridge 的测试绑定身份，保持原测试 Vault 路径不变。
- `CredentialLegacyVault` 仅为兼容测试保留旧实现原始 SQL／DPAPI 算法，运行时没有第二套存储实现。
- `CredentialBridgeProbe` 只包装真实 Bridge，以观测事务顺序、确定性安排并发或注入稳定错误，不模拟 SQLite 存储。
- 发布负载验证覆盖插件工程、程序集引用和实际构建目录；移除依赖后的验证使用全新 artifacts 目录，不以删除共享输出掩盖残留。
