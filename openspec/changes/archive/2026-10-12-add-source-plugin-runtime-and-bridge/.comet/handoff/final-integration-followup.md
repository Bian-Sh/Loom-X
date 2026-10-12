# 唯一最终集成 I-1 第1次定向复查

日期：2026-10-12；模式：standard；独立 reviewer：沿用最终集成首审者（Pasteur）。

## 1. 结论与轮次

**PASS。I-1 关闭；CRITICAL 0、IMPORTANT 0，未发现新增阻塞问题。**

这是“唯一最终集成首审1次 → I-1第1次定向复查”，不是 Runtime/Feedback 分段第3轮。原 Runtime/Feedback 原 reviewer 已授权第2轮 PASS、其历史问题关闭和修复额度全部保留；group1、SQLite、Compiler/followup、Credential、Tools 及 Runtime/Feedback 既有专家审查继续有效，不换人重置、不另开首审。

原 [最终集成首审报告](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-review.md) 和 [逐项覆盖矩阵](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/coverage-matrix.md) 原样保留。本报告仅补充其 I-1 不足项：**任务44/44、需求33/33、场景82/82可全部标为PASS**（旧逐项映射 + 本次补充证据），不是抹除首审42/44、31/33、81/82的历史结论。

独立 reviewer 未运行完整测试、未重审全change、未代修产品/正式测试/设计、未改任务/状态/guard/checkpoint、未提交/推送/归档、未派下级。复查 PASS 是独立审查结论，**不等于主会话已Apply Build/Verify守卫或获归档授权**。

## 2. 冻结输入与实际改动范围

- 唯一工作目录：[add-source-plugin-runtime-and-bridge](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。
- HEAD：`f066e16aeb5a2927337178902d13ba64e371deec`；base-ref：`3160697236ba2301437f4a28aa863ec8eb0265b9`。修复仍是该HEAD上的工作区差异；没有冒称HEAD已经包含修复。
- 最新实际发布：[outputs/20261012-104237](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/20261012-104237)，不使用09:44旧DLL代替新产物。
- 相对首审140项输入基线，仅生产 [PluginRuntime.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L498)、已有测试 [PluginRuntimeReviewRegressionTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeReviewRegressionTests.cs#L236)、[PluginRuntimeLeaseTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeLeaseTests.cs#L215) 与主会话流程状态不同；另新增 [PluginReloadUiValidationTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginReloadUiValidationTests.cs#L9)。其他首审输入摘要与首审基线一致。
- 已纳入后到的命令租约夹具差异；该文件第一次补采摘要的时间写入 input-before.json，并在独立8例重编/运行后再核对。生产、上述3个测试文件及旧两报告前后Hash相同。
- [首审基线差异审计](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/initial-review-baseline-delta.json)、[本轮输入前指纹](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/input-before.json)、[本轮输入后指纹](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/input-after.json)、[实际定向diff](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/targeted-fix.diff) 均保留。新未跟踪正式测试直接全文读取，未因 git diff 不包含它而遗漏。

## 3. I-1 修复与邻界核对

| 核对 | 生产路径/实际方法 | 结果 |
|---|---|---|
| 候选发布门禁 | [LoadPlugin:498–504](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L498) 只在 candidateOnly 调用 ValidateCandidateUi；位于UI订阅507–510、Entry注册518–522之前；[ReloadAsync:135–156](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs#L135) 只在非null候选返回后发布注册快照 | PASS |
| 四文化与结构 | [ValidateCandidateUi:573–593](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L573) 遍历zh-CN/zh-TW/ja-JP/en-US，校验provider、manifest id/slot、重复/null、缺少声明贡献；复用 [PluginUiValidator.Validate](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginUiValidator.cs#L26)，不另造宽松结构判定 | PASS |
| 异常与净化 | [LoadPlugin catch/finally:533–570](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L533)、[Reload catch:163–186](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs#L163) 保留稳定失败代码/安全摘要；独立source和assembly异常夹具捕获日志（含异常对象ToString）和两类诊断，不含合成私密sentinel与data路径 | PASS |
| 失败仅清理候选 | 失败在订阅/注册前返回null；现有finally释放候选Instance、Bridge、ALC；独立标记验证失败候选无订阅、实例已Dispose，候选Bridge恰一次释放，旧实例/旧Bridge及旧卡片仍有效 | PASS |
| 合法候选及旧租约 | 新12例中的合法切换、相邻旧SSE/展示/UI快照/命令用例，以及实际发布source/assembly成功候选；有旧lease时旧Bridge不释放，lease排空后才释放，最终Runtime释放每个Bridge恰一次 | PASS |
| 启动兼容 | candidateOnly为false不调用新门禁；新正式启动降级例与独立两runtime各schema/provider/throws共6组，首次加载插件仍存在、坏UI为空/安全诊断，未变成整体拒绝 | PASS |
| source/assembly一致 | 两种分支在入口初始化/Extension校验后汇入同一门禁；最新实际发布DLL邻界探针36/36验证，不仅推断源码路径 | PASS |

两个既有夹具修改没有弱化行为断言：

1. [旧展示信息/快照等待测试:236–249](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeReviewRegressionTests.cs#L236) 仍由旧SlowUiPlugin在途操作等待ui-continue，候选改ReadyUiPlugin仅取消新一代的同闸门等待；旧Bridge必须在等待期间保持0次释放、旧操作完成后1次释放、新Bridge保持0次，断言全部保留。夹具manifest仍声明panel。
2. [在途UI命令:88–106](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeLeaseTests.cs#L88) 的等待、旧命令Handled、reload成功和旧Bridge释放断言未改变。[LeaseUiProbePlugin:215–227](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeLeaseTests.cs#L215) 仅补IPluginUiContributionProvider及与manifest的panel/detail-body一致的合法schema按钮；新门禁未因测试失败而放宽。

ALC卸载调用及资源退役路径未修改；本次直接证据确认Dispose/Bridge释放，不额外声称每个ALC已被GC物理回收。旧ALC回收风险和已通过的原生命周期审查继续保留。

## 4. 真实RED/GREEN与失败历史

以下为读取原始TRX/消息，而不是信任文件名或任务勾选。[历史TRX解析及SHA256](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/historical-trx-audit.json)、[最新主会话TRX解析](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/main-session-new-trx-audit.json) 保留机器可核对结果。

| 记录 | 真实结果 | 判读 |
|---|---|---|
| [UI-RED.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/UI-RED.trx) | 12总，2 pass / 10 fail，0 skip | 十项均失败于正式测试第31行Assert.False：Expected False / Actual True，覆盖schema/id/slot/duplicate/null/root/missing/provider/throws/ja-JP；是I-1真正错误放行RED |
| [UI-GREEN.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/UI-GREEN.trx) | 63总，61 pass / 2 fail | 新12例已绿；两个旧UI等待夹具各约10s后reload=False，不当作目标缺陷RED或最终全绿 |
| [UI-GREEN-VERIFIED.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/UI-GREEN-VERIFIED.trx) | 63/63，0 fail/skip | ReadyUiPlugin候选夹具调整后的有效相邻GREEN |
| [BUILD-UI-FIX-FULL.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/BUILD-UI-FIX-FULL.trx) | 1896总，1895 pass / 1 fail | 唯一失败是旧命令租约夹具manifest有panel但无provider；该历史完整失败保留 |
| [UI-ADJACENT-FINAL.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/UI-ADJACENT-FINAL.trx) | 71/71，0 fail/skip | 命令夹具补合法provider/panel后主会话相邻结果 |
| [BUILD-UI-FIX-VERIFIED.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/BUILD-UI-FIX-VERIFIED.trx) | 1896/1896，0 fail/skip | 主会话修复后完整结果；[916edcb9实际执行日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/916edcb9-deed-442a-9ab0-5144c36e7615.log) 的末尾计数一致；本review只读核对、不重复执行完整测试 |

修复前1884/1884保持历史有效记录，但不冒称是当前修复输入的完整测试；当前完整输入证据是上表1896/1896。CLI完成记录及阶段guard由主会话负责，本review未绕过。

## 5. 本review独立执行的命令和结果

命令均在唯一工作目录执行，所有artifacts/cache/data仅位于新自有证据根，不碰主会话正常bin。

```powershell
$ev = 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1'
dotnet build "$ev/probe/FinalIntegrationProbe.csproj" -c Release --artifacts-path "$ev/probe-artifacts"
dotnet "$ev/probe-artifacts/bin/FinalIntegrationProbe/release/FinalIntegrationProbe.dll" "$ev/probe-data"
dotnet build "$ev/neighbor-probe/NeighborProbe.csproj" -c Release --artifacts-path "$ev/neighbor-artifacts"
dotnet "$ev/neighbor-artifacts/bin/NeighborProbe/release/NeighborProbe.dll" "$ev/n6"
dotnet test 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj' -c Release --artifacts-path "$ev/test-artifacts" --filter 'FullyQualifiedName~PluginReloadUiValidationTests|FullyQualifiedName~PluginRuntimeReviewRegressionTests|FullyQualifiedName~PluginDevelopmentServiceTests|FullyQualifiedName~PluginUiInteractiveRuntimeTests' --logger 'trx;LogFileName=FOLLOWUP-UI.trx' --results-directory "$ev/test-results"
dotnet test 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj' -c Release --artifacts-path "$ev/test-artifacts" --filter 'FullyQualifiedName~PluginRuntimeLeaseTests' --logger 'trx;LogFileName=FOLLOWUP-LEASE.trx' --results-directory "$ev/test-results"
```

| 独立执行 | 实际结果 | 原始证据 |
|---|---|---|
| 原初审探针原样复跑 | build exit0、0警告/错误；run exit0；initial=1、before=1、reload=false、current=1、after=1（首审为reload=true/current=2/after=0、exit2） | [构建](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/probe-build.log) / [运行](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/probe-result.log) |
| 发布DLL邻界探针 | 最终build exit0、0警告/错误；run exit0、36/36组 | [源码](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/neighbor-probe/Program.cs) / [最终构建](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/neighbor-build-final.log) / [最终运行](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/neighbor-result-final.log) |
| 新回归及相邻正式测试 | 63/63，0 fail/skip，exit0 | [日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/narrow-tests.log) / [FOLLOWUP-UI.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/test-results/FOLLOWUP-UI.trx) |
| 命令等租约正式测试 | 8/8，0 fail/skip，exit0 | [日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/lease-tests.log) / [FOLLOWUP-LEASE.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/test-results/FOLLOWUP-LEASE.trx) |

36组由source/assembly各18组组成：14组无效候选（schema/id/slot/duplicate/null/root/missing/provider/throws/null-list，以及每一种文化特有无效）；1组合规切换及旧lease；3组首次加载schema/provider/throws降级。每组验证当前代际、四文化旧/新卡片、候选订阅前失败、候选实例释放、Bridge释放次数、日志/诊断敏感字段净化。程序集夹具使用版本化DLL路径，避免覆盖被当前进程持有的旧DLL。

本review自有探针编排失败也全部保留，不算产品TDD RED：attempt1缺Logging引用；attempt2在source初始加载无实例处中断（未输出原始诊断，不能据此断言产品根因）；attempt3程序集旧契约误用空capabilities/extensions；attempt4误用不存在的PipelineResult.Continue；attempt5试图覆盖已加载DLL遇到Windows文件锁。分别保留源码快照/构建或运行日志；修正仅限自有探针，最终n6一次完整36组成功。正式产品修复轮次未因此增加。

独立测试的Release重编含既有文件/analyzer警告，全部保留于日志；**没有将两个小探针的0警告扩张为整个solution本轮独立重编0警告**。

## 6. 最新发布与原生UI证据复核

- 两个独立探针复制到运行目录的Host、Abstractions、Logging.Abstractions、CodeAnalysis/CSharp五DLL与10:42实际发布SHA256逐一一致；原初审Program.cs原样复制，只改csproj HintPath引用新发布。
- 主会话最新 [发布闭环证据](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/published-20261012-104237/attempts/20261012-104629-662-17c0a35f/)：layout32/32；PID22092闭环33断言及PID19740重启8断言、均exit0；真实生产组合根六工具、失败更新保留旧实例、新旧代际、cache_hit=true和SQLite计数2→3。读取原始run-info/runtime-proof、两进程日志、hosttrace和来源文件，不以摘要代替。
- 本review重新计算当前发布464文件SHA256：464/464与闭环源基线一致；24条关键托管/native加载来源记录与当前发布Hash一致。[发布/探针/模块来源复核](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/published-rehash.json)。
- “无SDK运行”证据是self-contained/app-local runtimeconfig、实际apphost+runtime模块来源/hosttrace，而不仅清空PATH；构建探针可用SDK，设备本身仍安装SDK，**不是物理无SDK设备**。本review两个dotnet窄探针只承担I-1，不单独声称无SDK运行验收。
- 可选复核了 [最新原生UI壳证据](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/ui-reload-candidate-2026-10-12_10-48-00/) 的README、事件/进程/Hash记录，并实际查看04无效重载与05有效重载两张APP-only图片：旧版本1/旧卡片保持、reload=False；有效候选切到版本3/新卡片、reload=True。六关键DLL/nativeHash与本轮发布一致。PID12308/HWND12851234背景壳、日文/原生关闭记录保留。
- UI证据仅真实PluginsView/VM/Runtime隔离壳。顶部验收按钮与底部结果栏属于探针，**不是完整MainWindow、不是自动Toast隐藏、不是真实Provider/Vault操作**；不因透明主题截图判断真实主题配色。

## 7. 原覆盖矩阵的精确增补

未重写旧矩阵的逐项生产路径、测试及专家范围。完整性/正确性/一致性采用comet-verify和openspec-verify-change方法；按本次授权只核对I-1及邻界，无状态命令、无新的OpenSpec结构变更，因此未重复主会话严格validate。

| 旧不足项 | 对应生产路径 | 本次实际补充证据 | 最终 |
|---|---|---|---|
| 任务4.8：先验证候选再原子替换 | [LoadPlugin门禁](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L500) → [ReloadAsync快照发布](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs#L135) | 原独立复现exit0；正式12例GREEN；source/assembly28个无效候选拒绝并保持旧卡片/Bridge，2个合法切换/lease组 | PASS |
| 任务6.5：reload工具接生产Runtime，失败保留当前实例 | [PluginDevelopmentService.ReloadAsync](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentService.cs#L130) → 同一Runtime门禁 | 正式PluginDevelopmentServiceTests包含于63/63；最新发布六工具闭环33+8仍成功；UI失败由Runtime返回false，不再错误提交 | PASS |
| 需求S10：重新加载保持在途请求代际连续性 | [Runtime生命周期](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs#L123)、原ProviderExecutionPipeline/PluginLeaseStream接线未变 | 原S10.1–S10.3逐场景证据保留；新增前置候选完整性证据；63例内旧SSE和UI操作、8例命令租约，及发布成功候选旧lease释放通过 | PASS |
| 需求D7：提供安全重新加载操作 | [PluginDevelopmentService](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentService.cs#L130)、[候选UI验证](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L573) | 成功/失败工具原场景保留；此次UI错误候选失败已识别，旧实例继续可用；最新闭环与UI图复核 | PASS |
| 场景D7.2：重新加载失败 | [候选拒绝与清理](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L500)、[ReloadFailed诊断](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs#L182) | 原探针从exit2变exit0；正式10类拒绝 + 独立两runtime14类拒绝、异常净化/候选清理/旧卡片，均真实通过 | PASS |

因此：旧42已PASS任务 + 本次2项 = **44/44**；旧31已PASS需求 + 本次2项 = **33/33**；旧81已PASS场景 + 本次D7.2 = **82/82**。S10原三场景原本已PASS，本次只补其设计§14候选完整性前提，不能重复计成新增场景。[复查汇总与覆盖增补JSON](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-followup-20261012-539c05b1/verification-summary.json)。

## 8. 风险边界与非阻塞建议

- 该门禁验证四个产品文化的“候选时快照”，不是保证插件未来所有状态/自定义文化永远输出合法UI；后续展示仍由既有生产PluginUiValidator降级。同步Provider代码可能慢、抛错或产生自己的副作用；本契约没有承诺取消/超时隔离或回滚任意可信C#副作用，不扩大为新的OS安全沙箱承诺。
- Bridge/ALC是服务和加载生命周期边界。可信C#可自行访问当前用户的文件/网络；不得将恶意代码绕开Bridge解释成此change已承诺的OS sandbox漏洞。
- Vault/长期placeholder恢复、Provider完整性system指令、SQLite仅宿主、旧DLL ABI及生产Toast接线本轮无改动；保留先前专家/最终首审证据，不再次操作真实Provider/Vault或重置其审查。
- 既有非阻塞MINOR T-M1（同一Compiler两次查询的composition测试较弱，生产共享及原独立强探针已通过）和文档EOF空行卫生建议保持原分类；**没有新增IMPORTANT，不要求额外修复轮**。
- 全量测试、Build/Verify guard、公开CLI返回记录和最终发布放行由主会话完成。当前独立结论足以关闭I-1；归档仍未授权。
