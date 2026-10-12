# Runtime 额外授权定向复查：原 I3

## 结论及轮次

- 日期：2026-10-12，Asia/Shanghai；原 reviewer：Mendel（01a12cff-cc69-7fe0-92e3-10a68d44dbf0）。依据用户当日明确授权及 [extra-review-authorization.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/extra-review-authorization.md)，执行原 Runtime 首审的**额外一次修复后定向复查**。
- **历史 reviewRounds=1 保留，本轮计 reviewRounds=2；不是重新首审、不重置轮次。**仅在报告记录，未改 Comet 状态。
- **结论：通过（PASS），原 I3 的 IMPORTANT 残留关闭。本次三处修复及所检查相邻边界未发现新的 CRITICAL / IMPORTANT；没有需提出的 MINOR。**
- I1/I2 沿用上一轮关闭结果，不全包重审。没有审查 Feedback、Compiler 或其他包；本报告不提供其验收结论，**不等于整体发布、最终全量、Verify/Archive 通过**。

## 工作目录、只读与实际改动核对

- 唯一工作树：[指定工作目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)，开始及结束 HEAD 均为 716b59c86f9a5cbd15e7b89f2593d404cdea2aa5。只读执行 git status/rev-parse/diff，没有 pull、提交、推送或清理。
- 与上一轮 [runtime-followup.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-followup.md) 的 24 文件哈希比较，只有 PluginRuntime.cs 和 PluginRuntimeReviewRegressionTests.cs 改变。复查开始到结束这 24 个文件哈希均不变，未改源码/tasks/状态，未派下级、未代修。
- 已读实际代码及 git diff；另外仅在内存逆向以下三处改动，重建结果 SHA-256=a3ec9087fce918cbda8e40e7aa903d3e795a3048ddd2db26e7fe20395ac4e448，**与上一轮保存的 Runtime 源码哈希完全一致**，确认未混入其他 Runtime 改动。内存逆向不写文件、不执行旧代码、不制造 RED。
- 唯一工作树新增文件为本报告；构建、原探针副本、新守卫及证据摘要均在 [Mendel 本轮自有独立目录](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd)。不重建共享 DLL，不杀 testhost，不删除原临时目录或其他 session 产物。

## 原 I3 修复检查

定位：[PluginRuntime.cs:437–446,527–563](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs)。

1. 原 try 外的构造改为 PluginLoadContext? loadContext=null。
2. PluginLoadContext / AssemblyDependencyResolver 构造移入第 446 行 try 内，failureStage 已为 Load；构造抛错进入同一诊断 catch，产生 plugin id + Load + Error + LX_LOAD_FAILED。
3. 候选 finally 使用 loadContext?.Unload()，构造未成功时不因空上下文造成次生清理异常，不覆盖原 Load 诊断；成功构造时仍尝试卸载。

### 两个原失败场景已关闭

- **首次加载损坏 deps**：合法 Manifest / 有效 DLL 的生产 PackageValidation.Success=true，然后依赖解析构造失败；Runtime 拒绝入口，出现具体 LX_LOAD_FAILED，而非空 StructuredDiagnostics。不会调用 BridgeFactory，不创建候选 Bridge。
- **reload 损坏 deps**：返回 false，保留版本 1 及旧 request Pipeline，具体 Load / LX_LOAD_FAILED 与通用 ReloadFailed 可同时存在，不再只有通用诊断。旧 Bridge 与已取得租约不提前释放，候选没有创建额外 Bridge。
- 上一轮独立源码原样复制（仅项目生产 DLL 引用路径指向本轮 artifacts），两个曾失败探针均在本轮转绿。**这不是仅依赖主会话 GREEN 的确认。**

### 相邻修复风险验证

| 边界 | 本轮证据 |
|---|---|
| Load / Initialize / Capability 分类 | 两种 Runtime 入口构造及 reload 构造具体 Load 诊断通过；初始化异常仍为 Initialize / LX_INITIALIZE_FAILED；不兼容能力或缺适配器保持 Capability 分类及执行入口前拒绝。 |
| 非法程序集已创建资源清理 | 独立非法 DLL 例为 Load / LX_LOAD_FAILED，已创建 Bridge 恰好释放一次。 |
| 多步清理异常不覆盖原因 | 独立实例 Dispose、Bridge Dispose、ALC Unloading 同时抛错例通过：三步均尝试、保留 Initialize 原诊断、日志不含敏感原异常，并输出安全 Warning。 |
| 旧 Pipeline / Bridge / 租约 | 损坏 deps reload 失败后旧 Pipeline 引用及实际执行有效，旧 Bridge 释放计数为 0；Runtime Dispose 后在途租约仍可执行且未释放 Bridge，最终释放租约后恰好释放一次，重复 Dispose 不多释放。 |
| 敏感 deps 原文与绝对路径 | 新守卫把敏感标记放入损坏 .deps.json，检查 logger 的格式化文本及异常对象文本、字符串/结构化诊断，均不含标记或夹具绝对路径。Load 诊断为 Error，不伪造 RelativePath/Line/Column。 |
| 空上下文清理安全 | 构造失败时未调用候选 BridgeFactory，也没有新增“候选插件资源清理异常”日志；nullable 卸载不遮蔽原诊断。 |
| 已有退休/观察者安全 | 相邻 RuntimeLease/StorageLifecycle/RegistrationAtomicity 回归通过：候选清理、观察者隔离、旧 ALC 卸载安全 Warning、旧 SSE/操作租约及实际 SQLite 退休不被本次挪动破坏。仅作相邻风险复跑，不重新审这些实现。 |

## 正式原始 RED / GREEN 核对

用户提供的 [runtime-extra 证据](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/runtime-extra) 未修改、未覆盖：

| 文件 | TRX 原始计数 | 判断 |
|---|---:|---|
| EXTRA-RED.trx | **0 pass / 2 fail** | 首次加载没有具体 Load 诊断、reload 仅通用 ReloadFailed。与上一轮 I3 残留一致；不是夹具格式误写、DLL 锁或环境失败。 |
| EXTRA-GREEN.trx | **59 pass / 0 fail** | 正式修复后 GREEN，两个损坏 Probe.deps.json 回归已加入正式 PluginRuntimeReviewRegressionTests；不称全量结果。 |

本轮不回退产品、不补造历史 RED。前轮其他错误 GREEN 命名、夹具失败和全量时序限制保持原报告事实，未由本轮 PASS 覆盖。

## 独立执行结果与计数

- [build.log](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/build.log)：exit=0，**0 errors / 14 warnings**；全部 bin/obj 输出在独立 artifacts。
- [MENDEL-EXTRA-TARGETED.trx](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/results/MENDEL-EXTRA-TARGETED.trx)：**24/24，exit=0**。6 例正式 I3 构造/损坏 deps/reload 诊断 + CapabilityRuntime 4 + SourceRuntimeIntegration 4 + RuntimeLease 8 + RegistrationAtomicity 1 + RuntimeStorageLifecycle 1。没有执行全包/全量，也没有扩大 Feedback 测试范围。
- [MENDEL-ORIGINAL-I3-GREEN.trx](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/results/MENDEL-ORIGINAL-I3-GREEN.trx)：**4/4，exit=0**。原损坏 deps 两例 + 原非法 DLL 一例 + 原三步清理同时抛错一例。原探针源码 SHA-256=0636a25f42c070225f84f594242b5e994912ffb01583356daacfe0dc3ad22810，副本字节不变；null/Register=false 两例没有执行，避免重审 I2。
- [MENDEL-I3-SAFETY-GREEN.trx](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/results/MENDEL-I3-SAFETY-GREEN.trx)：**2/2，exit=0**。追加 I3 敏感 deps/路径和租约最终一次释放守卫。新增守卫只有当前 GREEN，不宣称有独立历史 RED。
- 上述为三组执行结果，部分语义覆盖重叠，不把它们说成 30 个互不重复的需求回归。

自有 [原探针源码](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/original-probes/%E5%8E%9F%E8%BD%AE%E6%AC%A1%E6%8E%A2%E9%92%88.cs) / [新增安全与租约守卫](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/i3-guards/I3%E5%AE%89%E5%85%A8%E4%B8%8E%E7%A7%9F%E7%BA%A6%E8%BE%B9%E7%95%8C.cs)；机器可读计数、冻结哈希、实际三处改动核对及历史 TRX 摘要：[额外复查证据摘要.json](file:///D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/%E9%A2%9D%E5%A4%96%E5%A4%8D%E6%9F%A5%E8%AF%81%E6%8D%AE%E6%91%98%E8%A6%81.json)。

## 执行命令

所有命令工作目录均为 [用户指定唯一工作树](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。以下为实际执行命令，PowerShell 的独立日志重定向及退出码检查省略展示；运行结果已分别读日志/TRX 确认。

~~~powershell
dotnet build D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj --artifacts-path D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/artifacts --nologo -v minimal

$filter='FullyQualifiedName~PluginRuntimeReviewRegressionTests.损坏依赖清单|FullyQualifiedName~PluginRuntimeReviewRegressionTests.入口构造失败|FullyQualifiedName~PluginRuntimeReviewRegressionTests.重载构造异常|FullyQualifiedName~PluginCapabilityRuntimeTests|FullyQualifiedName~SourcePluginRuntimeIntegrationTests|FullyQualifiedName~PluginRuntimeLeaseTests|FullyQualifiedName~PluginRegistrationAtomicityTests|FullyQualifiedName~PluginRuntimeStorageLifecycleTests'
dotnet vstest D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/artifacts/bin/LoomX.Tests/debug/LoomX.Tests.dll "/TestCaseFilter:$filter" '/Logger:trx;LogFileName=MENDEL-EXTRA-TARGETED.trx' /ResultsDirectory:D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/results

dotnet test D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/original-probes/RuntimeExtra.Tests.csproj --artifacts-path D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/probe-artifacts --filter 'FullyQualifiedName~ALC依赖解析构造失败|FullyQualifiedName~实例Bridge与ALC清理|FullyQualifiedName~非法程序集在受保护加载' --logger 'trx;LogFileName=MENDEL-ORIGINAL-I3-GREEN.trx' --results-directory D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/results --nologo -v minimal

dotnet test D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/i3-guards/RuntimeExtraGuards.Tests.csproj --artifacts-path D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/guard-artifacts --filter 'FullyQualifiedName~损坏deps敏感原文路径不泄漏' --logger 'trx;LogFileName=MENDEL-I3-SAFETY-GREEN.trx' --results-directory D:/AppData/Temp/loomx-runtime-extra-mendel-8j6lsd/results --nologo -v minimal
~~~

## 限定验收与停止

- **仅原 I3（task4.6 的加载诊断残留及 task4.7 关联加载阶段）复查关闭；原 I1/I2 关闭历史保留。**本轮三处修复的新风险检查通过，无新的 IMPORTANT，故不需要本轮阻塞修复。
- reviewRounds=2，额外授权已消费，本轮完成后停止；没有自主开启下一轮、没有更换 reviewer 重置次数。
- 未勾任务、未正式验收整个 Runtime 包；如主会话依据历史完整审查和其他验证办理任务验收，应保留本报告限定范围，不把本轮 PASS 当成全包重审或最终全量。
- **不涉及 Feedback 的修复/复查结果，也不代其放行。**整体发布、最后全量、成品 UI、最终 Verify/Archive 仍须主会话依据各独立范围的真实证据判断；本报告不提供整体发布许可。
