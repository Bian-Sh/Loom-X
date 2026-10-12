# task7.1 Playground 独立 E2E 设计与执行计划

## 范围与权威来源
- 正式需求：`openspec/changes/add-source-plugin-runtime-and-bridge/` 的 design、三个 spec 与 tasks 7.1；Credential 同时遵循既有 credential-protection change。
- 用户已批准 autonomous + TDD + standard。本侧包只写 `LoomX.PluginPlayground/**`，不维护 Comet 状态、不提交 Git、不发布、不改 Runtime/Tests/桌面端。
- 当前共享 worktree 有其他 session 的修改，全部保留。已有 codegraph 仅用于查询，不重新索引。

## 设计
- 保留可执行断言式 CLI，输出中文检查名称、检查数量和安全结果，失败退出码为 1。
- `GetPipeline` 只按公共接口执行；注册身份检查读取 `runtime.Pipelines` metadata，不能假设动态代理是 `Pipeline`。
- 使用公共 `SourcePluginCompiler`、`PluginPackageValidation` 和 `PluginRuntimeOptions.SourceCompiler/BridgeFactory`，不复制编译/加载规则。
- 显式引用宿主侧 `LoomX.Plugin.Storage.Sqlite`，Credential 只动态加载；不引用 LoomX 桌面工程。
- 本次运行使用系统临时目录中带 task7.1 与随机标识的独立根；包、数据、源码缓存分离。`AppContext.BaseDirectory` 仅读取构建随附插件负载，不作为运行时数据路径。
- 源码夹具通过公共 Bridge 执行 CRUD、封闭值类型、显式事务、路径拒绝、日志及通知，并注册成对 request/response Pipeline。Bridge adapter 仅接受夹具规定的安全事件/字段/通知，捕获内存摘要；不冒充生产 feedback 安全实现。
- reload 验证成功切换、编译失败和初始化失败保留、旧租约继续使用原代际、排空后旧 Bridge/SQLite 句柄拒绝复用、新代际继续可用。
- 独立历史 Vault 夹具按旧 `CredentialTokens` 表、SHA256、`dpapi:v1:` + CurrentUser DPAPI 格式预置记录，验证 Bridge 插件首次读取及跨两个 runtime 重新打开后恢复相同 token；不读取生产 Vault。
- 不使用真实 Provider/配置/网络；可信源码数据隔离不是操作系统沙箱。不验证发布包无 SDK，不对 tasks 7.2–7.5 背书。

## TDD 执行顺序
- [x] 原版真实 RED：隔离构建输出，运行 Playground，保存控制台与退出码（9 项，3 PASS / 6 FAIL，退出码 1）。
- [x] 先写扩展 E2E 断言与测试夹具，再运行未装配版本保存扩展 RED（22 项，11 PASS / 11 FAIL，退出码 1）。
- [x] 实现最小 Playground 装配与捕获 adapter，首次 GREEN 76/76；自查追加未激活日志 union 敏感哨兵，先取得 76 PASS / 1 FAIL 的真实 RED，再修复本侧包 adapter 并取得 GREEN 77/77。
- [x] standard 自查边界、敏感信息、资源释放、失败断言真实性；修复后两次独立 Release 运行均 77/77，真实日志无敏感哨兵。正式独立 standard 审查由主会话协调；已有额外审查文件只作非正式参考。
- [x] 保存中文交接、真实控制台检查数量和本次文件清单。

## 已知独立遗留
feedback 的短 Basic/Bearer standard 复查残留等待用户授权，本侧包不修改它，不依赖它背书。历史 Vault 使用虚构凭据，任何日志及 CLI 结果不得输出该值、token、SQL 参数或 DPAPI 密文。

## 收尾补强
仅在 Playground 内补齐失败 reload 成对 Extension 身份/同租约执行、失败候选 g3 区分，以及最近可用 g2 全部缓存的 SHA256 摘要检查。新增断言通过临时故障注入取得真实 RED（85 项，83 PASS / 2 FAIL），恢复最终源码后连续两次 Release GREEN（85/85）。不修改公共 Runtime；正式独立验收由主会话负责，已有额外审查文件保留但不作正式验收。