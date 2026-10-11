# Runtime / Provider 代际与源码可信披露：standard 独立首审

## 结论与边界

- 日期：2026-10-12，Asia/Shanghai。恢复原未完成首审；原 authoritative 502 并发错误未产生结论，不计轮次。本次是首审，不是修复复查；已用修复复查 **0 轮**，后续最多 **1 轮**。
- **结论：请求修改（REQUEST_CHANGES）。3 项 IMPORTANT 必修：I1/I2 为 P1，I3 为 P2；范围内没有已证实 CRITICAL。不能验收整个 Runtime 包，更不能放行整体发布。**
- 依据用户已正式批准的 [design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md)、[source-plugin-runtime spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md)、[plugin-host-bridge spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md)；不重做设计、不变更任务状态，保持 autonomous + tdd + standard。
- 工作树：[指定工作目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)；开始核对 git status，HEAD=12996da6b5b4b6e8ff6fb3177b4f77876319c7ce。审查包括未提交及未跟踪实现，不只审 HEAD。只读约束下未 pull、未改源码、未提交/推送、未勾任务、未写 Comet 流程状态；唯一工作树新增文件是本报告。
- 对 14 个核心生产/UI/资源文件记录 SHA-256，探针结束后重算，全部相同；主会话没有修改该冻结范围。全部重编及新增探针位于 [独立临时 artifacts](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78)，不使用共享 output DLL，不杀任何进程，不清理其他 session 产物。
- 范围：PluginRuntime / PluginRuntimeLifecycle / Pipeline / PipelineRegistry / PluginPackageValidation，ProviderExecutionPipeline / PluginLeaseStream / LoomXHost，PluginsViewModel / PluginsView、四语言 source-trust 资源，以及用户指定九类测试。Compiler、SQLite 内部实现、Credential、tools/Skill 只核对 Runtime 交互；Compiler cache 修复不在本报告内。

## 必修缺陷

### I1 — [P1] assembly 验证了新 Manifest，却仍执行 Discovery 的旧快照

**任务：4.7 / 4.8，关联 1.5 授权快照一致性。**

定位：[PluginRuntime.cs:394,407–424](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs)；[PluginPackageValidation.cs:31–71](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginPackageValidation.cs)。LoadPlugin 保留 package.Manifest，随后 ValidateAsync 再读磁盘并返回 validation.Manifest，但调用方只使用 Success，不对比两份 Manifest。assembly 的路径、入口、能力和 Extension 仍取旧快照。source 分支后续输入/编译边界已有快照对照；这里只核实该交互，不重新审 Compiler。

**公开生产路径的确定性复现：**PluginRuntime.Start 先 Discovery 收集 a/b 两个 assembly 包。加载 a 的 BridgeFactory 将 b 磁盘清单从版本 1 / 正常入口更新为版本 2 / Review.MissingEntry。b 随后的生产 ValidateAsync 成功并返回新入口，但 Runtime 仍加载旧入口，b.Version=1。磁盘当前清单声明的入口不能按其声明加载，旧入口却已执行，能力和注册也可能来自一份没有经过本次生产验证的快照。**这是验证/执行身份不一致，不是宣称 ALC 能提供恶意代码沙箱。**

**最小修复：**执行插件代码及创建 Bridge 前，用统一、确定性的 Manifest 快照对照绑定 Discovery 与生产验证；不一致时拒绝候选并输出安全 Manifest/Load 诊断。不要只悄悄改用新 Manifest，而让已完成的能力协商继续基于旧值；身份、Runtime、能力和注册必须来自同一验证快照。

**最小回归：**PackageValidation / RuntimeIntegration 加公开 Start 双包夹具；a 更新 b 后，b 构造/初始化不得执行，有稳定结构化诊断。至少改变 entry_type + version；能力、extensions 或 runtime kind 改变应由同一规则拒绝。reload 不一致时旧版本、Pipeline、Bridge 保持有效，仅释放失败候选；正常 assembly/source 对照继续通过。

独立 RED：[REVIEW-PROBES-FINAL.trx](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/results/REVIEW-PROBES-FINAL.trx) 中“assembly发现快照与生产验证清单不同必须在执行前拒绝”失败；输出记录 production validation=True / Review.MissingEntry 与 Runtime b.Version=1。

### I2 — [P1] 候选 Extension 校验失败被跳过，reload 仍提交残缺管线

**任务：4.7 / 4.8，关联 4.11 新请求行为。**

定位：[PluginRuntime.cs:476–488,505–513](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs)；[PluginRuntimeLifecycle.cs:138–156](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs)。Kind/FailurePolicy 与 Manifest 不符时写字符串诊断后 continue；其余注册完成即 accepted=true。ReloadAsync 将残缺候选作为“验证成功”发布并退休旧实例。Register=false 也只被忽略，不能靠跳过失败 Entry 满足完整候选验证。

**确定性复现：**版本 1 有实际修改数据的 request Extension；版本 2 清单改为 fail-closed，实现仍是 continue-on-error。source 与 assembly 均出现 reload=True、Version=2、ExtensionCount=1、request Pipeline 不存在；原有效 request 处理被移除，旧版本没有保留。若 Entry 承担数据保护，新请求可能不再经过保护。即使历史启动路径允许 Entry 降级，**新的原子 reload 不能把候选契约失败解释为成功替换**。

**最小修复：**候选初始化/Extension 契约/注册验证失败，必须使整个 reload 候选失败，走已有注册撤销、实例、Bridge、ALC 清理路径；保留旧快照并输出安全结构化诊断。无需重新设计 Pipeline；如需保留启动兼容性，区分启动降级与 candidateOnly 严格成功条件。

**最小回归：**RuntimeLease / RegistrationAtomicity / ProviderGeneration 覆盖 source/assembly 失败策略不匹配；断言 ReloadAsync=false、旧版本及 request/response 输出不变、旧 Bridge 不提前释放、候选实例和 Bridge 释放一次、在途旧请求可继续。同类 Kind 不匹配、未声明 Extension、实际 Register=false 不得留下候选 Entry。正常替换及旧 ALC 卸载 Warning 不回退继续通过。

独立 RED：同一 TRX 的“候选Extension失败策略不匹配必须拒绝重载并保留旧代际”两例均失败；输出保留旧处理结果及新 request Pipeline 缺失的实际值。

### I3 — [P2] 入口构造等一般加载异常没有统一映射为 PluginDiagnostic

**任务：4.6，关联 4.7 加载失败分类。**

定位：[PluginRuntime.cs:376–386,451–458](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs)。DiscoverAndLoad 仅对 CapabilityNotSupported 补结构化诊断；Activator.CreateInstance 构造失败等一般异常只有字符串诊断和安全日志。Initialize 的 LX_INITIALIZE_FAILED 不能代表构造/程序集加载失败已覆盖。

**确定性复现：**source 与 assembly 入口构造函数均抛异常。Runtime 正确拒绝加载，捕获日志不泄漏夹具敏感标记，但 StructuredDiagnostics=[]，只有“加载失败：TargetInvocationException”字符串；调用方不能通过统一诊断取得稳定阶段/错误码。这里确认的是**缺失诊断**，不是已发现敏感日志泄漏。

**最小修复：**补齐加载/实例化/注册异常安全映射；保留已分类 Capability/Initialize/Compile 诊断，不回显异常 Message、源码、外部绝对路径或运行数据。reload 保留真实失败阶段，不仅返回笼统 ReloadFailed；清理异常不能抹掉原失败原因。

**最小回归：**两种 Runtime 构造异常均有 plugin id、Load、Error、稳定 code；非法程序集/依赖加载失败、CreateExtensions 或注册异常同样可分类，不伪造源码行列。捕获 logger 验证原异常敏感标记不进入日志/诊断；Initialize 原有分类及候选清理继续通过。

独立 RED：同一 TRX 的“入口构造失败应生成安全结构化加载诊断”source/assembly 两例失败，StructuredDiagnostics 为空；敏感标记不进入捕获日志的断言已先行通过。

## 逐 task 判断及最小验证

“局部认可”只表示本范围未发现该交互的必修缺陷，**不是勾任务、完整验收或发布许可**。

| task | 本轮判断 | 必修及最小回归 |
|---|---|---|
| 1.4 | 已首审契约仅核对 Runtime 增量接线，局部认可 | Initialize 收到 Bridge；预编译共享契约/旧初始化方式仍可用，相关 CapabilityRuntime / RuntimeTests 通过。不重审公共契约。 |
| 1.5 | 精确协商/拒绝代理局部认可，整体快照受 I1 阻塞 | CapabilityRuntime 4 例通过：不兼容能力和缺适配器在构造前拒绝，授权错误不初始化，合法声明能调用。补 I1 授权/Manifest 快照一致性。 |
| 2.7 | 仅 Runtime 提供程序替换/夹带拒绝交互局部认可 | 独立 source/assembly 各覆盖 Microsoft.Data.Sqlite、SQLitePCLRaw.core、嵌套 e_sqlite3，6 例通过。authorizer / SQL 限制不重审，不宣称阻止恶意进程绕过。 |
| 2.8 | 真实 Runtime 退休闭环局部认可 | 实际 SQLite 测试通过：租约期间旧事务可用，退休回滚未提交写入，旧句柄 Disposed，新代际重开同库为 0 条，另一插件仍可写。普通启停不退休；不重审 SQLite 内部。 |
| 4.6 | **不通过：I3** | Compile/Initialize/Capability/InvalidEntry 已有通过证据；补一般加载/构造/注册异常映射及敏感日志守卫。 |
| 4.7 | **不通过：I1、I2** | 正常同功能 source/assembly 对照通过，但不能豁免验证/执行身份与失败候选完整契约。补双包快照竞态及两种 Runtime 的失败候选。 |
| 4.8 | **不通过：I1、I2** | 编译失败保旧、注册冲突清理、UI 取消订阅抛错、observer 隔离、旧 ALC Unloading 抛错仅安全 Warning 已通过。补被跳过校验错误的假成功替换及旧 Manifest。 |
| 4.9 | 源码可信披露局部认可 | RuntimeKind 保留；四语言卡片/详情、启用 Warning 明确同进程/当前用户/文件网络权限、ALC 仅加载卸载/Bridge 仅服务边界/均非 OS 沙箱；无自带详情仍有宿主入口，语言刷新/既有程序集反馈通过。 |
| 4.11 | 请求/响应代际实现局部认可，Runtime 包受 4.8 阻塞 | 实际非流式 request/response 同代际及并发启停通过；独立 SSE 实际响应处理核实旧请求/响应=1，新请求/响应=2。同步/异步零长度读不释放，EOF/Dispose 释放一次。UI 命令、展示信息、快照均持操作租约。 |

## 已核实的 Runtime 边界

- [LoomXHost.cs:135–149](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomXHost.cs) 生产 DI 注入 Runtime、BridgeFactory、SourceCompiler，Provider 不固定持有启动时 request/response 引用；真实 Provider 测试不是只调用 GetPipeline 的浅层替身。
- [ProviderExecutionPipeline.cs:95–127,141–143](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/ProviderExecutionPipeline.cs) 每次调用取得成对租约；流式在 response 恢复流外包 [PluginLeaseStream.cs:13–52](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginLeaseStream.cs)，外层实际 EOF/释放才退休旧代际，零长度读有长度守卫。
- [PluginRuntimeLifecycle.cs:47–65,192–258](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs) UI 操作持旧实例租约；Retiring / Leases=0 / !Released 才退休。独立展示信息和 Contribution 两例、现有异步命令例都核实不提前释放 Bridge。
- observer 逐个隔离，坏观察者不改变已提交结果、不阻止健康观察者；失败候选 UI 取消订阅抛错不跳过 Bridge 清理。旧 ALC Unloading 抛错仅安全 Warning，不回退新实例。实际 SQLite 例核实资源回滚，不仅是释放计数替身。
- 上述通过点不消除 I1–I3，候选必须完全有效才可发布。

## 原始 TDD / UI 证据审计

原始文件没有覆盖、改名；计数来自 TRX Counters，不凭文件名判绿。

| Runtime 原始证据 | 真实结果（pass / fail） | 解释 |
|---|---:|---|
| RED.trx | 0 / 3 | 最初租约/重载边界 RED。 |
| GREEN.trx | **11 / 2** | 两例 Assert.IsType 对 Task<bool> 精确类型假设失败；不是全绿，也不计本轮产品 RED。 |
| SOURCE-RED → SOURCE-GREEN | 0/3 → 12/0 | 源码加载、入口、安全初始化诊断。 |
| PROVIDER-RED → PROVIDER-GREEN | 0/1 → 25/0 | 流式代际接线。 |
| UI-LEASE-GREEN | 28/0 | UI 操作通过证据。 |
| VALIDATION-RED → VALIDATION-GREEN | 0/7 → 7/0 | 生产验证和读取入口。 |
| DEPENDENCY-RED → DEPENDENCY-GREEN | 0/1 → 20/0 | 执行前拒绝 SQLite 夹带。 |
| BOUNDARY-RED → BOUNDARY-GREEN | 0/4 → 38/0 | 三项读取 API 缺失/一项观察者故障的真实 RED。 |
| METADATA-RED | 4/1 | RuntimeKind 增量缺口。 |
| CLEANUP-RED | 14/1 | UI 取消订阅异常导致候选清理遗漏。 |
| RETIRE-RED | **0/2** | **仅 ALC 卸载异常缺 Warning 为 1 个真实产品 RED**；另一个是夹具删除已映射 DLL 的 UnauthorizedAccessException，不能计产品 RED。 |
| CREDENTIAL-INTEGRATION-RED | 1/4 | 旧夹具未注入已声明 storage 适配器，不扩大为 Credential 产品结论。 |
| RUNTIME-PACKAGE-GREEN | **44/1** | manifest 夹具 capabilities 为空被旧 assembly 规则拒绝；**不是 GREEN，也不是产品 RED**。ASSEMBLY-PROBE 0/1 保留根因。 |
| RUNTIME-PACKAGE-FINAL-GREEN | **45/0** | 修正合法夹具后的主会话包证据，不能覆盖本次首审缺陷。 |

原文件：[Runtime evidence](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/runtime)。[INTEGRATION-PROBE.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/INTEGRATION-PROBE.trx) 为 **1798/1798**，文件内没有最后新增两个 PluginProviderGenerationTests，不能当最终/最新全量。本首审未跑全量，不宣称最终全量通过。

[task4.9 UI 证据](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/task4.9-trust-ui-2026-10-12_05-48-53)：GREEN-FINAL=20/20、GREEN-REGRESSION=71/71；早期 GREEN=9/9，两个 RED 文件各为 1 pass / 8 fail。目录提供 TRX / Avalonia UI 渲染与交互证据。本首审没有新启动桌面 app、没有新 CUA 截图，不冒称已完成实机截图验收，也不据透明主题截图判断真实配色。

## 独立 artifacts 执行记录

- [build.log](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/build.log)：独立构建 exit=0，0 errors / 14 warnings；没有把既有警告说成无警告构建。
- 首跑 [REVIEW-RUNTIME.trx](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/results/REVIEW-RUNTIME.trx) 为 51 pass / 4 fail，均来自 PluginRuntimeTests 夹具依赖 AppContext.BaseDirectory 向上四层源码 manifest 路径。仅在本审查临时目录复制同一 manifest 后重跑，没有改仓库测试/产品。该已存在夹具可移植性限制不冒称新产品 RED。
- [REVIEW-RUNTIME-FINAL.trx](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/results/REVIEW-RUNTIME-FINAL.trx)：**55/55**。RuntimeLease 8、RuntimeStorageLifecycle 1、ProviderGeneration 2、SourceRuntimeIntegration 4、PackageValidation 10、SourceTrustDisclosure 20、RuntimeTests 5、CapabilityRuntime 4、RegistrationAtomicity 1。
- [探针源码](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/probes/Runtime%E9%A6%96%E5%AE%A1%E6%8E%A2%E9%92%88.cs) 和 [独立测试项目](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/probes/RuntimeReview.Tests.csproj) 引用本轮独立构建生产 DLL，不改工作树。
- [REVIEW-PROBES-FINAL.trx](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/results/REVIEW-PROBES-FINAL.trx)：**18 例：13 pass / 5 fail**；5 个失败对应 I1×1、I2 source/assembly×2、I3 source/assembly×2，是未修复缺陷的真实 RED，非 GREEN。
- 13 个通过：真实 SSE 同代际×1、同步/异步 EOF/Dispose 与零长度读×4、UI 展示信息/Contribution 操作租约×2、两种 Runtime 提供程序夹带拒绝×6。未把 DLL 删除锁/共享编译锁计入这 5 个产品 RED。
- 初始 [REVIEW-PROBES.trx](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/results/REVIEW-PROBES.trx) 为 15 例 / 10 pass / 同一 5 fail；仅追加三个通过边界探针，无产品修复，不是修复复查。
- 计数、核心文件原始 SHA-256 与历史证据摘要：[审查证据摘要.json](file:///D:/AppData/Temp/loomx-runtime-review-cVVb78/%E5%AE%A1%E6%9F%A5%E8%AF%81%E6%8D%AE%E6%91%98%E8%A6%81.json)。全部独立产物留存，不清理其他 session outputs、Comet 状态或临时目录。

构建：dotnet build D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj --artifacts-path D:/AppData/Temp/loomx-runtime-review-cVVb78/artifacts --nologo -v minimal

九类复跑：dotnet vstest D:/AppData/Temp/loomx-runtime-review-cVVb78/artifacts/bin/LoomX.Tests/debug/LoomX.Tests.dll，使用九类 FullyQualifiedName 的 OR 过滤、TRX logger、独立 results；完整执行结果见以上 TRX。

探针复跑：dotnet test D:/AppData/Temp/loomx-runtime-review-cVVb78/probes/RuntimeReview.Tests.csproj --artifacts-path D:/AppData/Temp/loomx-runtime-review-cVVb78/probe-artifacts --logger "trx;LogFileName=REVIEW-PROBES-FINAL.trx" --results-directory D:/AppData/Temp/loomx-runtime-review-cVVb78/results --nologo -v minimal

## 后续与整体阻塞

1. 原实现方最小修复 I1–I3，把独立 RED 落入仓库最小回归。本审查者未修改源码、未勾任务。
2. 同一冻结范围最多 **1 轮**定向修复复查，核对三项、候选清理、原已通过代际/退休/UI/提供程序边界。原 502 不计轮次，补探针不是修复复查。
3. [Feedback 独立报告](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-review.md) 的 task3.1/3.2 短 Basic/Bearer 漏检已用唯一 standard 复查，用户未授权追加，**仍阻塞整体发布**。本报告不重审、不代修、不放行，不换 reviewer 重置次数。
4. Compiler cache、Credential、tools/Skill 保留各自独立边界；局部认可不替代其结果。最终全量、正式发布包、无 SDK 成品闭环、实机 UI、最终集成/归档不能据此宣称完成。
