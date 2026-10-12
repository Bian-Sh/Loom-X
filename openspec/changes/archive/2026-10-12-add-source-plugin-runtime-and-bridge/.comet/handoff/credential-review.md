# Credential Bridge：standard 独立首审（task 5.1–5.6）

## 结论与审查边界

- **限定范围认可：task 5.1–5.6 未发现必修 CRITICAL / IMPORTANT 源码缺陷。** 不是整个 change 的验收，不勾选任务、不改变批准状态，也不把 190 pass 当全量结果。
- 审查日期：2026-10-12，Asia/Shanghai；证据整理时间约 06:05。Git 基线：`12996da6b5b4b6e8ff6fb3177b4f77876319c7ce`，分支 `comet/add-source-plugin-runtime-and-bridge`。
- 本审查独立于实现者 Aquinas；未派下级代理。按用户 autonomous + TDD + standard 和正式批准的 design/spec 核查实现，不重新讨论设计。
- 本次仅首审，**复查尚未使用，后续最多一轮定向复查**；本轮测试过滤调整和探针接线排错不属于实现者修复后的复查。没有写 Comet 状态、提交、推送或 task 勾选。
- 只读审查工程源码。新增内容仅为此报告及本次独立验证的 `outputs/` 证据、构建/发布产物、隔离探针；没有覆盖、删除或清理其他会话产物。
- 主会话正在修改的 PluginRuntimeTests / Provider / Playground 接线不归本审查负责。Provider 的现有定向测试与隔离流式探针只作为 Credential 安全语义的辅助证据，不据此批准这些模块的接线任务。

## 权威依据与方法

已读取并对照：

- [工程 AGENTS.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/AGENTS.md)，尤其 Vault 路径、日志安全、插件负载及 Credential 生命周期约束；同时遵循用户提供的全局约束。
- [本 change design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md) 第 12、14、15 节及 [plugin-host-bridge spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md) 的旧 Vault、事务与退休生命周期要求。
- [Credential 权威设计](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/design.md)、[Credential 安全 spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/specs/credential-protection/spec.md)、[Runtime/Pipeline spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/specs/plugin-runtime-pipeline/spec.md)。
- 实际代码、Git 基线差异、新增测试、原始 TRX/log，再独立还原/构建/执行定向测试和发布。既有 `codegraph query CredentialTokenStore` 仍返回旧同步方法，索引已陈旧；未修改共享索引，以实际源码为准。
- 用户要求只读，因此未执行会改变工作区的 `git pull` 或工作流激活；只使用 status / diff / show / rev-parse 等只读 Git 操作。

## 逐 task 核对

| Task | 首审判断 | 实际代码与验证证据 |
|---|---|---|
| **5.1** Manifest / 新 Vault 结构 | **认可** | Manifest 声明 `storage.sqlite/v1`。Store 只经公共 `IPluginStorageBridge` 打开相对路径 `credential-tokens.db`；四列、主键、OriginalHash 唯一约束不变。旧三条 PRAGMA 分拆执行，仍为 WAL / NORMAL / 5000。新旧实际 SQLite 的 `sqlite_master` 与索引结果比较通过，不只是比 SQL 文本。 |
| **5.2** 异步 Bridge / 同步适配 / 取消与释放 | **认可** | Store 异步创建、精确查询、去重恢复及 `IAsyncDisposable` 接线完整；所有异步等待不捕获 UI 上下文。生产插件唯一阻塞点是 `CredentialSynchronous.Run`；Engine 的调用级 AsyncLocal 将取消令牌传入存储，退出时恢复前值。稳定 token、跨 Store 恢复、去重、预取消/事务中取消/在途响应取消、退休与宿主 Bridge 所有权、非泵送 UI SynchronizationContext 下 JSON/SSE 均通过。同步解析仍占用调用线程，不宣称全链路非阻塞。 |
| **5.3** rollback → dispose → 精确 requery | **认可** | Store 35–83 行捕获 `ConstraintViolation`，使用 `CancellationToken.None` 回滚，在 `await using` 完成释放后才按 OriginalHash 精确查询。实际双 Store + 真实 SQLite 的并发 OriginalHash 冲突、Token 主键碰撞两种测试通过，观测顺序分别包含 `rollback → dispose → query-single` 和随后 `begin`；数据库没有第二个同明文映射，没有嵌套 placeholder。 |
| **5.4** 真正旧格式 / DPAPI / OriginalHash | **认可** | `CredentialLegacyVault` 不是自行猜写的新格式：与基线旧生产 Store 比较，规范化名称、可见性、命名空间、测试用途注释和行尾后，**正文完全一致**。旧实现真实建库及写 DPAPI 记录；升级 Store 能恢复并稳定复用旧 token；记录的 Token / OriginalHash / ProtectedOriginal / CreatedAtUtc 前后完全相等，旧实现仍能读回，仅一个 `.db`。SHA256 UTF-8 大写十六进制、`dpapi:v1:`、DPAPI CurrentUser、无额外熵均未改变。该测试没有做迁移或重写密文。 |
| **5.5** 错误与安全语义 | **认可** | Busy / Locked / Corrupt / Disposed 的真实 Bridge 包装故障、非法前缀/Base64/不可解密 DPAPI、真实损坏文件、未知及 Unicode 易混/残缺引用均有覆盖。请求/响应遇存储或解密错误返回 Blocked + 空 Payload + 安全摘要；不会把原始正文放行。正常停用仍恢复历史，实际退休保留 Vault 并拒绝旧 Store 访问。单独补做真实 Bridge 的跨 chunk/SSE、tool arguments、停用及流式 fail closed 探针，7 项通过。 |
| **5.6** 移除插件 SQLite 依赖 / 负载 | **认可（本次干净构建/发布范围）** | csproj 已移除 Microsoft.Data.Sqlite，插件不引用宿主实现；程序集依赖断言、插件构建目录及测试镜像布局测试通过。独立全新 Windows 发布中 Credential 插件目录 11 个文件、SQLite 禁止负载 **0**；宿主根包含单份 Microsoft.Data.Sqlite、SQLitePCLRaw 组件及 e_sqlite3。Manifest 和四语言 i18n 保持每插件目录，根没有平铺 Manifest/i18n。未动既有复制布局；干净实际构建和发布已验证能正确工作。 |

### 关键源码与最小保留回归

- [CredentialTokenStore.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialTokenStore.cs)：35–83 行冲突算法，85–111 行精确去重恢复，123–151 行初始化，156–178 行退休，181–203 行 Hash/DPAPI。
- [CredentialEngine.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialEngine.cs)：58–80 行显式 Store 和取消作用域；170、620 行调用同步适配。
- [CredentialSynchronous.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialSynchronous.cs)：唯一生产同步等待点。
- [CredentialProtectionPlugin.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialProtectionPlugin.cs)：37–48 行 Bridge 初始化，154–165 行扩展/资源所有权，250、419 行请求/响应取消转发；Manifest 的双 fail-closed 策略未退化。
- [CredentialStorageBridgeTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialStorageBridgeTests.cs)：25 行新库结构；65 行旧库四列不变；85 / 116 行两种实际冲突顺序；151 / 174 / 254 / 300 行取消、释放与 UI 无死锁；202 / 224 / 350 / 367 行故障、DPAPI、损坏库不重建及日志敏感哨兵；327 行停用/退休历史兼容。
- [PluginPublishContractTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginPublishContractTests.cs)：61–95 行项目、程序集、实际插件输出及镜像负载无 SQLite 依赖。

**必修缺陷及对应新最小回归：无。** 上述是必须保留的既有回归，不要求为首审通过虚构修复任务。

### 不应误判的安全边界

权威 spec 明确规定未知本地映射的完整普通引用原样保留。`UnknownAndDamagedReferences_AreNeverFuzzilyResolved` 验证不猜测，而不是要求任何含 `LOOMX` 的普通文本都 Blocked。SQLite/DPAPI 失败、流式未闭合或异常超长候选则必须 fail closed。两者不能混为一谈。Engine 原 JSON/SSE 解析和受限归一化算法未重写，完整性 system/developer 注入也未加入关闭开关或改成 user message。

## 本次独立执行结果（不是实现者自报）

证据目录：[independent-first-review-20261012](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012)。

### 1. 全新 artifacts 的首次正常构建及广义过滤

```powershell
dotnet test D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj --artifacts-path D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/independent-first-review-artifacts-20261012 --filter 'FullyQualifiedName~Credential|FullyQualifiedName~PluginPublishContractTests|FullyQualifiedName~PluginPayloadLayoutTests' --logger 'trx;LogFileName=credential-review.trx' --results-directory D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012 --verbosity minimal
```

**136 pass / 2 fail / 138 total，exit 1。** 没有 `--no-build` / `--no-restore`，所有项目在全新 artifacts 下还原/构建。

两项失败必须保留，不计作本范围源码缺陷：

1. `PluginRuntimeTests.CredentialProtectionPlugin_LoadsInIsolatedAlc_WithSharedContractIdentity`：Stage 从测试输出根找 `plugin.manifest.json`，该根不应有平铺 Manifest；属于用户明确由主会话修复的接线范围。
2. `GatewayViewContractTests.EndpointCardsExposeCredentialAndReasoningControls`：UI 测试源码定位在隔离 artifacts 布局下指向 `outputs/credential-20261012-bridge/LoomX/Views/...`；属于广义 Credential 过滤意外选中的范围外测试。

[首次 log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/credential-review.log) / [首次 TRX](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/credential-review.trx)。

### 2. 明确范围的正常构建定向测试

同一独立 artifacts，不跳过构建，过滤为：

```text
FullyQualifiedName~LoomX.Tests.Plugins.Credential|FullyQualifiedName~LoomX.Tests.Plugins.PluginPublishContractTests|FullyQualifiedName~LoomX.Tests.Plugins.PluginPayloadLayoutTests|FullyQualifiedName~LoomX.Tests.Plugins.PluginUiContractTests|FullyQualifiedName~LoomX.Tests.Services.ProviderExecutionPipelineTests
```

**145 pass / 0 fail / 0 skip，exit 0。** 其中新增 StorageBridge 24、StorageMigration 3、PublishContract 7；另含 Credential 旧回归、负载/UI 契约和 Provider 现有辅助测试。是过滤后的定向通过，不是全量。

[定向 log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/credential-scoped-review.log) / [定向 TRX](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/credential-scoped-review.trx) / [逐测试类计数](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/scoped-test-counts.json)。

### 3. 独立 Windows 发布与实际负载检查

正常 `dotnet publish`：`-c Release -r win-x64 --self-contained false`，另一个全新 `independent-first-review-publish-artifacts-20261012` 和 `independent-first-review-publish-20261012` 输出，**exit 0**。这是本次负载检查用 framework-dependent 发布，不冒称整个任务 7.3 的正式打包、无 SDK 环境或真实桌面验收。

[publish log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/credential-publish.log) / [实际负载清单](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/publish-layout.json)。

### 4. 真实发布程序集的隔离流式探针

仅在证据目录生成控制台探针，引用**本次新发布**的插件、公共契约、宿主 Bridge / Pipeline 程序集；真实本地临时 SQLite + DPAPI，假 HttpMessageHandler，3-byte 网络读取，不访问生产 Vault 或真实 Provider。通过公开 PipelineRegistry 注册真实 CredentialResponseExtension，而非用字符串替换的假 Pipeline。

最终 **7 项断言通过，exit 0**：

1. 跨 SSE event + 网络 chunk 的实际 Store 恢复；
2. tool arguments 跨事件恢复并保持有效 JSON；
3. 停用主动保护后仍恢复历史；
4. 未闭合历史引用在流结束时 fail closed；
5. 超长候选 fail closed；
6. 查询包装注入 Busy 后流式响应被阻断；
7. Store 实际退休后流式恢复被阻断。

[探针源码](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/stream-probe/Program.cs) / [最终 run6 log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/stream-probe-run6.log)。这是控制台断言，**无 TRX，不合并进 145 pass**。之前 run1–5 的探针自身 namespace/内部 API/程序集依赖接线错误也已保留；不是 Credential 源码失败，详见 reviewer-method-notes。

## 原始 RED / GREEN 证据核实

[实现者原始证据目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence) 的 TRX 实际计数：

| 原始证据 | 总数 | pass | fail | 说明 |
|---|---:|---:|---:|---|
| 01-contract-RED | 3 | 0 | 3 | Bridge 构造、异步生命周期、Manifest 能力缺失确实 RED |
| 02-contract-GREEN | 102 | 99 | 3 | **文件名不是整体结果，实际 Failed**；旧磁盘读取句柄/Runtime stage/UI artifacts 路径失败 |
| 03-behavior-RED | 116 | 113 | 3 | 预取消请求、预取消响应、在途响应取消未 Blocked |
| 04-behavior-GREEN | 116 | 116 | 0 | 取消行为修复后的 GREEN |
| 05-payload-RED | 3 | 1 | 2 | 插件仍引用/携带 SQLite 时两项 RED |
| 06-payload-GREEN | 170 | 170 | 0 | 新 artifacts 负载与定向测试 GREEN |
| 07-regression-GREEN | 186 | 186 | 0 | 仍为定向集合 |
| 08-final-GREEN | 186 | 186 | 0 | 同上 |
| 09-integrated-GREEN | 186 | 186 | 0 | 同上 |
| 10-integrated-final-GREEN | 190 | 190 | 0 | 最终定向集合；log 只有测试运行，不单独证明重建，不是全量 |

[原始计数机器可读汇总](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/original-evidence-summary.json)。本次没有还原源码来重演 RED，以免违反只读及多会话边界；RED 审核限于原始日志/TRX与断言的对应关系，GREEN 则另有独立重建的当前代码证据。

## 非阻塞提醒与最小后续验证

### E1（MINOR，证据表述）：“GREEN” 文件名掩盖整体 Failed

`02-contract-GREEN.trx` 实际 99/102、3 fail。原始文件应保留以维持 TDD 轨迹；交接摘要必须准确写其整体 Failed，并区分“新增三项契约已绿”与“整个筛选集已绿”。本报告已纠正，不要求改源码或重命名历史证据。最小核验就是读取 ResultSummary.Counters 与失败项，而非信文件名。

### E2（非阻塞，持续回归范围）

现有 Provider 跨事件测试使用替代 Response Pipeline；本次已用真实发布 Bridge 补齐隔离验证，但这些 7 项尚不在 xUnit 常驻套件内。任务 7.5 集成时，最低应把“真实 Bridge 跨事件恢复”和“Busy/未闭合流安全失败”固化成少量回归，并检查主会话的实际 Runtime 代际租约接线。此项属于后续集成证据，不能反向冒称本次已验收 Provider / Playground 接线。

此外，task 5.6 当前自动断言以插件工程构建输出及测试 Manifest 镜像为主，不是每次执行真实宿主发布；本次 publish 清单已补证。后续发布继续使用全新输出或显式验证实际目录，不能用删除其他会话旧产物来掩盖残留。

## 保留的限制与交付

- 未运行整个 `dotnet test` 套件，未作真实桌面 CUA、无 SDK 安装环境、热重载代际租约及 Playground 全链路验收；相关 task 7.x 与主会话范围保持待验证。
- 本范围的旧 Vault、DPAPI、精确冲突算法、取消/UI 同步点、停用/退休和流式安全边界有实际执行证据；没有从旧 DLL 或实现者最终 190 pass 推导通过。
- [旧夹具与 Git 基线逐字符对照](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/legacy-fixture-comparison.json)、[被审文件 SHA256](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/source-hashes.json)、[方法/失败探针说明](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/reviewer-method-notes.txt)、[验证后工作区状态](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/credential-20261012-bridge/evidence/independent-first-review-20261012/worktree-status-after-tests.txt) 均已保存。
- 没有修复、提交、推送、勾 task 或修改流程状态；没有删除既有 `.planning/`、`.codegraph/`、`outputs/` 或其他会话文件。