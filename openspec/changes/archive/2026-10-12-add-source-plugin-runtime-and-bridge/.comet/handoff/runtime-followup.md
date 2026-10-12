# Runtime standard 唯一一轮定向修复复查

## 结论、轮次与冻结边界

- 日期：2026-10-12，Asia/Shanghai。延续 [原 Runtime 首审](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-review.md)，**不是新首审**；原 authoritative 502 不计轮次。本次完成唯一一轮 standard 修复复查，**reviewRounds：0 → 1，额度已用完**。只在报告中记录，不修改实际 Comet 状态。
- **结论：仍请求修改（REQUEST_CHANGES）。I1、I2 关闭；I3 部分修复但仍残留 1 项 P2 / IMPORTANT。不能宣称本次整体复查通过，不能整包验收或放行发布。没有已证实 CRITICAL。**
- 用户冻结范围的 14 个核心生产/UI/四语言资源文件及 10 个测试文件共 **24 个文件**，复查开始/结束 SHA-256 全部一致。相对原首审的 14 个核心文件，仅 PluginRuntime.cs 改变；其余 Pipeline、Registry、Lifecycle、Provider、Host 接线及 UI 资源没有漂移。
- 工作树：[指定工作目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。开始 HEAD=12996da6b5b4b6e8ff6fb3177b4f77876319c7ce；结束 HEAD=a6c688d26272bc7889180b42391448bc73a5f6df，期间主会话提交其他范围。冻结文件内容未改变；本审查者仅执行 git status/rev-parse 等只读核对，没有 pull、提交、推送或任何工作树整理。
- **只新增本报告**；不改源码/tasks/Comet 状态，不派下级，不扩大到 Feedback/Compiler。所有构建、测试探针及证据摘要只写 [独立临时 artifacts](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU)，不重建共享 output，不杀 testhost，不清理任何其他 session 产物。

## I1 / I2 / I3 定向判断

| 首审项 | 本轮判断 | 实际代码及证据 |
|---|---|---|
| I1：assembly Discovery 与生产验证快照混用 | **关闭** | [PluginRuntime.cs:401–413](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs) 在能力工厂/执行入口前对比两份解析后 Manifest 的序列化语义快照，变化返回 LX_MANIFEST_CHANGED 并拒绝加载。原双包公开 Start 复现及 capabilities / host_capabilities / extensions / runtime 边界均通过。 |
| I2：不完整候选被当成成功替换 | **关闭** | [PluginRuntime.cs:471–524](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs) candidateOnly 对 null、重复、未声明、Kind/FailurePolicy 不符、缺失声明和 Register=false 整体拒绝；按 declared.Kind 创建 Pipeline。source/assembly 原失败策略及八项残缺候选回归通过；独立 null / 实际 Register=false 两项也通过。旧快照、旧租约与 Bridge 保留，失败候选资源释放。 |
| I3：一般加载异常缺结构化诊断 | **部分修复，未关闭** | [PluginRuntime.cs:526–534](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs) 已补 LX_LOAD_FAILED 并保留 Capability/Initialize 分类，source/assembly 构造和 reload 具体诊断回归通过；非法程序集独立探针也通过。但第 437 行创建 PluginLoadContext 在第 444 行 try 之外，依赖解析构造失败仍绕过统一映射，详见下一节。 |

### 清理隔离已核实

[PluginRuntime.cs:536–569](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs) 分别隔离 UI 取消订阅、实例、Bridge、ALC 清理异常；不会因为某一步抛错跳过其余步骤或覆盖原始诊断。独立探针使候选 Initialize 失败，同时实例 Dispose、Bridge Dispose、ALC Unloading 三步都抛错：实例/ALC 标记均出现，候选 Bridge 释放一次，Initialize 原阶段/code 保留，日志/诊断不含敏感夹具标记，三个清理异常有安全 Warning，旧 Bridge 在最后租约释放前仍未退休。该测试通过。

## 唯一残留 IMPORTANT：原 I3 的 ALC 依赖解析构造漏映射

**级别：P2 / IMPORTANT；任务：4.6，关联 4.7 的加载失败诊断。沿用原 I3，不新增审查范围、不重置轮次。**

### 定位与原因

- [PluginRuntime.cs:437–444](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs)：new PluginLoadContext(...) 先执行，诊断映射 try 随后才开始。
- [PluginLoadContext.cs:19–22](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginLoadContext.cs) 的构造会初始化 AssemblyDependencyResolver；这里只核对 Runtime 调用交互，不重新审其设计。
- 依赖描述文件损坏可在解析器构造时产生 InvalidOperationException，因此到不了已补的 LX_LOAD_FAILED catch。首次启动外层 DiscoverAndLoad 仍只记录字符串/安全日志；reload 外层只有 LX_RELOAD_FAILED，没有真实 Load 阶段诊断。

### 确定性复现与真实结果

使用独立构建的合法插件 DLL 和合法 assembly Manifest，放入内容为单个左花括号的 Probe.deps.json；**PluginPackageValidation.Success=true** 已先行断言。该依赖描述损坏是有意的加载故障输入，不是之前两个新增 Manifest 夹具写错导致的误 RED。

1. **首次加载**：Runtime 拒绝插件，StructuredDiagnostics=[]，字符串仅“插件 review 加载失败：InvalidOperationException”。缺少 plugin id + Load + Error + 稳定 code。
2. **reload**：当前版本仍为 1、ReloadAsync=false，旧 Bridge 释放计数为 0，旧租约上的实际 request Pipeline 可继续执行；但 StructuredDiagnostics 仅为 Reload / LX_RELOAD_FAILED，没有 Load / LX_LOAD_FAILED。

两个独立探针均失败于“应存在具体 Load 诊断”的断言，其他前置行为断言通过。**未证实旧代际被提前释放，也未据此声称敏感值泄漏或 ALC 资源泄漏；残留是批准要求中的统一加载诊断缺口。**

首审 I3 已明确要求“一般加载/依赖加载异常同样可分类，reload 保留真实阶段，不只通用 ReloadFailed”，故此不是扩展到 Compiler、重新审 ALC 或提出新的需求。

### 最小修复及最小回归（仅建议，未实施）

- 将 PluginLoadContext/依赖解析器构造纳入安全的加载诊断映射边界；成功创建的上下文仍按现有独立清理流程处理。无需修改 Compiler、Storage、Feedback 或设计新的加载框架。
- 保留原异常的安全阶段和稳定 code；不拷贝异常 Message、依赖原文、外部绝对路径；现有 Capability / Initialize 分类不被覆盖，清理异常继续隔离。
- 将本次两个故障夹具落入正式 Runtime 回归：合法 Manifest + 可用 DLL + 损坏 .deps.json，分别验证首次加载和 reload 都有 Load / Error / 稳定 code；reload 仍保留旧版本、旧 Pipeline、旧 Bridge/租约。非法 DLL、source/assembly 构造、Initialize 分类以及同时三步清理异常继续通过。

**本轮已发现 IMPORTANT，故复查到此停止。没有实施修复、没有追加第二轮；后续额外修复复查必须另获用户明确授权。**

## 原已通过边界与逐 task 限定结论

以下是本轮证据判断，不是 task 勾选、正式整包验收或发布许可。因 I3 未关闭，本次未形成整体通过后的正式任务验收授权。

| task | 本轮限定结果 |
|---|---|
| 1.4 | Runtime 增量 Bridge 初始化/二进制兼容接线未回归；不重新审已首审公共契约。 |
| 1.5 | 能力精确协商/未声明拒绝及加载前拒绝继续通过；I1 授权/验证快照一致性阻塞已关闭。 |
| 2.7 | Runtime source/assembly 提供程序夹带拒绝六例继续通过；SQLite authorizer/SQL 内部不重审。 |
| 2.8 | 实际 SQLite Runtime 退休回滚、旧句柄不可复用、同库重开和其他插件不受影响继续通过。 |
| 4.6 | **不能验收：I3 的 ALC 依赖构造加载诊断仍漏映射。** |
| 4.7 | I1/I2 对应 Manifest 一致性和完整候选契约修复已核实；加载失败诊断关联 I3，不能据正常对照通过宣称整体完成。 |
| 4.8 | 原子候选替换/失败保旧、Register=false/残缺拒绝、资源清理、observer 隔离及旧 ALC 卸载异常仅安全 Warning 未回归；I1/I2 行为缺陷关闭，但不替代 I3。 |
| 4.9 | 四语言 source-trust 20 例继续通过；RuntimeKind、卡片/详情/启用 Warning 明确同进程/当前用户权限、ALC 与 Bridge 非 OS 沙箱。没有新增桌面启动/CUA 截图，不冒称实机验收。 |
| 4.11 | 实际 request/response 同代际及并发启停、旧 SSE 实际响应处理/新请求换代、零长度读不当 EOF、EOF/Dispose 一次释放、UI 命令/展示信息/Contribution 操作租约全部继续通过。 |

## 正式 RED/GREEN 原始证据核对

原文件位于 [Runtime evidence](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/runtime)，没有改名、覆盖、回退产品或补造历史 RED。

| 原文件 | TRX 真实计数 | 处理 |
|---|---:|---|
| STANDARD-REVIEW-RED.trx | 18 例，13 pass / 5 fail | 原首审 I1×1 / I2×2 / I3×2 的真实失败，正式迁入回归。 |
| STANDARD-BOUNDARY-RED.trx | 32 例，13 pass / 19 fail | 17 个真实产品失败；host_capabilities / runtime 两项是新增 Manifest 夹具格式错误，不计产品 RED。原输出明确为 Manifest 分类，而非预期 Load。 |
| STANDARD-REVIEW-GREEN.trx | **65 pass / 2 fail** | 两项上述夹具失败；保留错误的 GREEN 名字，**不是全绿**。 |
| STANDARD-REVIEW-GREEN-FINAL.trx | **66 pass / 1 fail** | host_capabilities 夹具仍落入 CapabilityNotSupported，而非预期 Load；**不是全绿**。 |
| STANDARD-REVIEW-GREEN-VERIFIED.trx | **67 pass / 0 fail** | 用户修正合法夹具并正常重建后的通过：32 新回归 + 35 原包；本轮独立重编再次核实这些测试均通过。 |

两项新增语义快照探针修正夹具后的合法形式，**没有对应修复前产品 RED，不声称有完整 TDD 红绿链，也不通过回退补造**。这一历史证据缺口保留，不被 verified GREEN 覆盖。

原首审中的 PACKAGE-GREEN 44/1、RETIRE-RED 的 DLL 删除锁排除、FINAL-GREEN 45/45 与 1798 全量早于最后两个 ProviderGeneration 测试的限制，仍按原报告保留；本轮没有更新为最终全量，也没有重审其他包。

## 独立构建与复跑

### 冻结生产快照 + 正式范围测试

- [build.log](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/build.log)：exit=0，0 errors / 14 warnings，独立 artifacts；不把既有警告说成无警告构建。
- 仅在独立临时目录补同一 CredentialProtection manifest，以满足旧 RuntimeTests 向上四层源码目录夹具依赖；未修改仓库夹具或共享输出。
- [STANDARD-FOLLOWUP-EXISTING.trx](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/results/STANDARD-FOLLOWUP-EXISTING.trx)：**87/87，exit=0**。覆盖用户的 67 例正式包并增加原 source-trust 20 例。

| 测试类 | pass |
|---|---:|
| PluginRuntimeReviewRegressionTests | 32 |
| PluginRuntimeLeaseTests | 8 |
| PluginRuntimeStorageLifecycleTests | 1 |
| PluginProviderGenerationTests | 2 |
| SourcePluginRuntimeIntegrationTests | 4 |
| PluginPackageValidationTests | 10 |
| SourcePluginTrustDisclosureTests | 20 |
| PluginRuntimeTests | 5 |
| PluginCapabilityRuntimeTests | 4 |
| PluginRegistrationAtomicityTests | 1 |

### 同一复查中的六项独立边界探针

[探针源码](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/boundary-probes/%E5%94%AF%E4%B8%80%E5%A4%8D%E6%9F%A5%E8%BE%B9%E7%95%8C.cs)、[独立测试项目](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/boundary-probes/RuntimeFollowup.Tests.csproj) 引用本轮生产 DLL，不派下级、不改产品。

[STANDARD-FOLLOWUP-BOUNDARY.trx](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/results/STANDARD-FOLLOWUP-BOUNDARY.trx)：**6 例，4 pass / 2 fail，exit=1**。

- pass：null Extension 候选整体拒绝；Kind getter 在验证后改变导致真实 Register=false 的候选整体拒绝；实例/Bridge/ALC 三步清理同时抛错仍全部尝试并保留 Initialize 诊断；非法程序集稳定 Load 诊断且释放已创建 Bridge。
- fail：本报告原 I3 残留的首次加载/reload 两例。不是 Manifest 夹具错误、不是 DLL 重建/删除锁，不可以 87/87 抵消。
- 这些都是**同一轮复查**的验证，没有测试后修改产品、没有追加修复复查轮次。

原命令日志：[范围复跑日志](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/tests.log)、[边界复跑日志](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/boundary.log)。计数、24 文件 SHA-256 与正式 TRX 摘要：[复查证据摘要.json](file:///D:/AppData/Temp/loomx-runtime-followup-TLdtKU/%E5%A4%8D%E6%9F%A5%E8%AF%81%E6%8D%AE%E6%91%98%E8%A6%81.json)。全部临时产物保留，不清理其他 session 目录。

## 停止条件与整体阻塞

1. reviewRounds=1，唯一 standard 定向复查已使用；原 I3 仍为 IMPORTANT，**停止，不自主追加第二轮、不重新开首审、不换 reviewer 重置额度**。
2. task4.6 不能验收，整个 Runtime 包不放行；I1/I2 的关闭与其他边界通过照实保留，不否认已完成修复。
3. Feedback task3.1/3.2 短 Basic/Bearer 原 I2 仍待用户额外授权。**本轮没有代修、代审或放行 Feedback，整体发布仍被其阻塞**；报告仅引用用户明确给定的跨包阻塞事实。
4. Compiler 提交与只读集成验证属于主会话范围，本轮不审 Compiler、不更改其结果。未声称最新全量、正式发布、无 SDK 成品/实机 UI 闭环或归档完成。
