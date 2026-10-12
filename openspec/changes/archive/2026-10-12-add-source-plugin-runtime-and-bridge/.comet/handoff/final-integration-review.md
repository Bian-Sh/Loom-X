# Comet Verify 唯一最终集成独立审查

## 最终结论

**IMPORTANT（不通过最终集成放行）**。CRITICAL **0**，新 IMPORTANT **1**：**候选声明式 UI 未通过结构验证也能 reload 成功并替换旧可用版本**。该问题已即时反馈主会话，不预设通过，不以全量1884/1884或44/44勾选抵消。

日期：2026-10-12（Asia/Shanghai）。本报告是用户指定的 **standard、唯一最终集成 reviewer** 产物，不是 Runtime/Feedback 换人首审或额外修复轮；未派下级，未代修。

|维度|核对结果|
|---|---|
|Completeness|44/44任务已勾选；独立审源/证据核对42 PASS、2不足（4.8、6.5），不是重新勾任务|
|Correctness|33/33需求、82/82场景均有生产路径和实际测试/证据映射；31需求PASS、2不足；81场景PASS、1不足|
|Coherence|主要分层、ABI、共享编译器、能力/路径/资源/安全边界符合；design §14的候选UI验证前置条件存在I-1|
|最终等级|IMPORTANT 1 / CRITICAL 0；另有既有MINOR测试建议及非阻塞文档空行提示|

两个需求不足（S10、D7）与两个任务不足来自**同一个I-1**，不是四个问题。S10三个列举的在途代际场景都通过，但候选验证完整性前提仍不满足。逐项详表：[coverage-matrix.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/coverage-matrix.md)；机器映射：[coverage-mapping.json](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/coverage-mapping.json)。

## 1. 身份、只读范围与方法

- 唯一工作目录：[指定 worktree](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/)。分支 comet/add-source-plugin-runtime-and-bridge。HEAD **f066e16aeb5a2927337178902d13ba64e371deec**；base-ref **3160697236ba2301437f4a28aa863ec8eb0265b9**。未执行pull/fetch/merge、提交、推送、删除、状态修改、勾选、Apply guard或归档。用户的只读指定优先于默认开发pull流程。
- 实际审查基线为完整 base-ref→HEAD diff，共 **140文件、13472新增/383删除**。保存 [真实全change diff](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/full-change.diff)；不仅看交付摘要。围绕公共契约、Manifest/包验证、Compiler输入/引用/缓存、Runtime候选/租约、Provider生产接线、SQLite宿主边界、Credential迁移、工具/Skill/本地化/发布与Playground的真实diff和当前实现交叉审阅；专家内部算法不另起全面首审。
- 140个diff涉及的tracked文件前后SHA256全部相同、产品范围104文件无变化、HEAD相同：[输入前指纹](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/input-before.json) / [输入后指纹](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/input-after.json)。本次新增仅此报告、覆盖矩阵及 [自有evidence目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1)（探针源码/构建artifacts/合成包/数据/日志）。没有覆盖共享bin/obj或旧产物。
- 已读 [AGENTS.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/AGENTS.md)、本change proposal/design/tasks/三个delta spec及 [Credential权威设计](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/design.md) / [Credential权威spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/specs/credential-protection/spec.md) / [原Runtime/Pipeline权威spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/specs/plugin-runtime-pipeline/spec.md)。
- 按 comet-verify 的Completeness/Correctness/Coherence与集成风险方法，结合openspec-verify-change逐requirement/Scenario映射。CLI只走 **comet classic openspec --**；status和strict validate均只读，未执行select/transition/checkpoint/set等状态命令。用户已指定Verify/44完成，不以skill默认状态操作覆盖只读边界。
- CodeGraph只读status/query/callers：435文件、10447节点，报告有1项待同步；新组合根查询无结果、Runtime行号仍属旧版。因此使用它识别既有关系后以真实代码优先，未更新共享索引。

## 2. 新阻塞 I-1：reload 发布前缺少候选 UI 结构验证

**等级：IMPORTANT（P2，正确性/候选原子替换契约）。**没有证据表明该例导致凭据泄漏，不把它上升为CRITICAL或恶意代码沙箱问题。

### 准确定位

1. [PluginRuntime.cs:498–525](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L498)：候选取得IPluginUiContributionProvider，只检查provider是否缺失、写字符串诊断、订阅事件；之后注册Extension并直接accepted=true。**未对候选返回的声明式UI调用PluginUiValidator，也未检查UI声明/返回一致性。**
2. [PluginRuntimeLifecycle.cs:151–156](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntimeLifecycle.cs#L151)：BuildSnapshot仅构建Pipeline；随后直接替换plugins/registry并把旧实例标记退休。此时UI仍未被验证。158–161行退休旧实例、日志成功、通知UI、return true。
3. 对照 [PluginDevelopmentService.cs:191–201](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentService.cs#L191)：隔离test实际调用GetUiContributions并按声明比对，验证失败产生LX_TEST_UI_FAILED；正式 [测试拒绝无效UI和缺失声明的Extension](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginDevelopmentServiceTests.cs#L138) 在VERIFY-FINAL已Passed。生产工具reload仅透传Runtime成功结果，因而会把相同UI无效候选报告为reloaded。
4. 事后UI访问 [PluginRuntime.cs:196–223](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginRuntime.cs#L196) 才调用PluginUiValidator，SchemaVersion=999被忽略。这个渲染防护有效，但**发生在旧实例已经替换之后**，不能满足design §14“候选Extension/UI校验全部成功后才发布”。

### 最小真实复现与结果

本review用 **outputs/20261012-094447中的真实PluginHost/Abstractions/Roslyn发布DLL** 构建独立控制台探针，没有ProjectReference重建产品。包、data和cache都在本次自有evidence；无Provider、无生产Vault、无核心配置访问。探针构建使用SDK；此探针不承担无SDK设备证明。

1. 创建review.ui版本1，清单声明card-body、四语言负载与显式source，UI返回SchemaVersion=2。生产Runtime.Start后版本1、有效卡片数1。
2. 只改合成插件为版本2、UI SchemaVersion=999；源码本身正常编译、入口和Extension合法。
3. 调用生产ReloadAsync，再读取PluginInfos及GetUiContributions。

实际输出：

~~~json
{"initial":"1","before":1,"reload":true,"current":"2","after":0,"diagnostics":["插件 review.ui UI Contribution card 已忽略：UNSUPPORTED_SCHEMA"]}
~~~

预期守卫是reload=false、版本仍1、旧卡片仍1；实际相反。**probe build exit=0；probe run exit=2。**这是真实产品行为不符合候选守卫，不是编译失败/探针夹具初始化失败。结构化诊断只有编译warning CS0067，没有UI失败error；不能据此把工具reload成功当作候选UI验证已通过。

- 探针源码：[Program.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe/Program.cs)；独立工程：[FinalIntegrationProbe.csproj](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe/FinalIntegrationProbe.csproj)。
- 原始构建：[probe-build.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe-build.log)；原始结果：[probe-result.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe-result.log)。

### 影响、最小后续建议与审查轮次边界

- 用户的可用旧UI被无效新UI替换，工具/日志却显示reload成功，旧实例资源也按正常成功流程退休。当前UI过滤防止渲染非法结构，但不保留最近可用实例。
- 违反design §14，影响task4.8、6.5，及source-plugin-runtime的候选验证前提、plugin-development-skill安全reload要求。不是对原Mendel I1/I2/I3或Maxwell I2重新开审，未重置任何reviewRounds。
- 最小建议：在候选发布前，使用候选自身的既有UI验证/声明一致性规则确认可接受；无效schema、缺失声明、provider异常等应生成安全诊断并仅清理候选，保留旧实例/租约。不要对当前旧实例执行检查后当作候选通过，不新增第二套UI规则；初次加载的既有非关键UI降级语义应与reload候选承诺分开处理。
- 最小回归：合法旧卡片→SchemaVersion=999候选，断言reload失败、旧版本/旧UI/旧Bridge/在途租约保持有效、候选释放，敏感provider异常不泄漏；对有效候选仍成功的对照。**只建议，不在Verify实施，不派修复代理，不自主启动复查轮。**

## 3. 保留既有专家审查，不能用旧阻塞摘要重置历史

|工作包|采信的先前范围与最新结论|本次集成处理|
|---|---|---|
|Group1|[group1-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/group1-review.md)：注册原子性/缺适配器两IMPORTANT已由原reviewer定向关闭|保留；检查候选清理/能力工厂前置在跨包Runtime路径实际接入|
|SQLite|[sqlite-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/sqlite-review.md)：I1 RETURNING DONE/Busy、I2 PRAGMA首轮校验、M1/M2关闭；2.7/2.8当时有Runtime集成边界|保留；结合Runtime两种提供程序夹带拒绝、真实SQLite旧租约回滚/Disposed测试补闭合|
|Compiler|[compiler-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/compiler-review.md)及 [compiler-followup](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/compiler-followup.md)：原I1关闭，多进程/迁移/缓存输入/引用/i18n通过|不重审其内部全部实现；核对共享Compiler、发布固定引用、实际缓存负载与无SDK证据|
|Runtime|[runtime-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-review.md) / [runtime-followup](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-followup.md) / [runtime-extra-followup](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-extra-followup.md)：I1/I2上一轮关闭，原I3在用户授权第2轮PASS|**原reviewRounds=2保持**；旧“剩余阻塞”报告是历史。I-1是最终集成的UI候选新问题，不对原加载诊断修复重新放量|
|Feedback|[feedback-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-review.md) / [feedback-extra-followup](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-extra-followup.md)：原短Basic/Bearer I2在授权第2轮PASS，61正式测试+12组相邻守卫|**原reviewRounds=2保持**；不把旧feedback-review的阻塞误当当前状态，不替换reviewer|
|Credential|[credential-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/credential-review.md)：5.1–5.6限定PASS，真实旧Vault/DPAPI/冲突/取消/历史生命周期|保留；只核对公共Bridge、实际路径/退役与Provider请求租约跨包闭合|
|Tools|[tools-review](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/tools-review.md)：六工具/Skill/生产入口限定PASS，MINOR T-M1保留|共享实际Compiler/Runtime审源+原更强独立探针；本次补旧ABI探针，发现test/reload在UI守卫上分叉|

## 4. 重点组合边界的实际核对

- **共享Compiler/生产验证**：[PluginDevelopmentComposition.cs:15–26](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentComposition.cs#L15)注册Singleton Compiler、生产PackageValidation/路径/读取入口；[LoomXHost.cs:102–145](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomXHost.cs#L102)把同一DI Compiler传给Runtime并把六工具接入同一ToolRegistry。没有新增另一套Manifest/Roslyn规则。正式composition测试的Singleton自比较不足由原tools独立实际对象探针补证，不伪称它自身能证明全部接线。
- **生产Bridge与Toast**：[App.axaml.cs:140–148](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/App.axaml.cs#L140)创建同一Toast并交给Gateway/MainWindow；[GatewayProcessService.cs:25、54、100](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/GatewayProcessService.cs#L25)将其传入LoomXHost；[PluginHostBridgeFactory.cs:11–24](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginHostBridgeFactory.cs#L11)按不可变id和预期数据目录创建能力实例。MainWindow现有订阅通过UI dispatcher渲染并2.5秒隐藏，单插件投递故障被隔离。当前CUA壳的底部事件显示不作为MainWindow自动隐藏实测。
- **旧DLL/ABI**：两参数PluginInitializationContext/旧record解构、PluginManifest位置构造均保留；本review原样导出base-ref的8个契约文件，在自有artifacts编译旧DLL后只复制业务DLL、用新发布Runtime加载并执行。真实结果legacy.review/assembly/“合成输入-旧二进制”、StructuredDiagnostics=[]，exit0。不是仅当前源码反射断言。未调用Storage的该实例数据库文件数0：[无默认数据库核对](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/no-default-database.json)。
- **旧Vault**：CredentialTokenStore唯一OpenSqliteAsync credential-tokens.db；表/索引、dpapi:v1、OriginalHash保持；rollback→dispose→精确requery来自实际冲突测试。CredentialInitialize取消/退休清理无第二Vault兜底；普通禁用保持历史解析Bridge。未读真实用户Vault。
- **错误净化**：Roslyn受控消息/实际相对行列，缺源码/缺固定引用/缓存/load/init诊断分类；Runtime任意插件异常改安全异常，SQLite稳定枚举错误，工具结果受控消息。Feedback第2轮短凭据真实日志/Toast证据采信；不把识别已知形态的过滤描述为绝对防泄漏保证。
- **reload租约**：Provider在完整调用入口AcquireLease并保持成对request/response；普通JSON、SSE网络chunk/event、EOF/Dispose/异常/取消通过现有真实测试。PluginLeaseStream不将零长度读取视为EOF；旧Bridge直到最后租约释放才退休；SQLite活动事务退休时回滚，另一插件继续；候选编译/初始化/Extension失败只清理候选。**UI候选校验是本次单独发现的缺口。**
- **缺失/恶意负载**：四语言输入缺失安全拒绝，实际固定引用摘要/manifest缺失不搜索SDK/网络，残缺/篡改缓存重编。Runtime包扫描在执行入口前拒绝SQLite托管/原生夹带，两种Runtime都覆盖；不把C#直接绕过Bridge的能力当作已承诺沙箱漏洞。
- **工具信任/安装与reload**：test/reload默认trust_code=false拒绝；Read/Write风险沿用ToolRegistry/AgentLoop。create不覆盖不安装，compile不执行，test临时数据/独立Toast/NullLogger无Provider/配置服务，reload只用注入Runtime已安装路径，不以开发目录偷偷替换安装包。trust_code是显式调用声明，不是不可绕过的人工批准票据。
- **SQLite仅宿主**：发布宿主Microsoft.Data.Sqlite与native e_sqlite3各一份，插件目录无禁止负载；Credential插件不引用宿主SQLite实现，契约无DbConnection/任意object。宿主既有配置DB依赖不在本轮迁移去重范围，运行时配置路径仍为AppDataPaths，不创建第二配置库。
- **安全披露/UI**：四语言宿主级可信源码警示、列表/详情/启用Warning与语言切换真实视图/VM证据通过；原Credential系统指令修改/暂停保护等文案仍在插件自有负载，未把插件专属键塞宿主resx。未声称完整桌面启动、安装器或自动Provider/Vault验收。

## 5. 命令、真实结果与证据边界

### 本review实际执行

|命令/方法|实际结果|原始证据|
|---|---|---|
|git status / rev-parse / diff base HEAD；全diff与前后SHA256|HEAD f066e16一致，140输入指纹不变；仅只读|[diff](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/full-change.diff)、[after](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/input-after.json)|
|comet classic openspec -- status --change add-source-plugin-runtime-and-bridge --json|exit0，三个spec/设计/tasks完整；不证明功能通过|[status](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/openspec-status.json)|
|comet classic openspec -- validate add-source-plugin-runtime-and-bridge --strict --json|exit0，1/1 valid、issues=[]|[strict validate](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/openspec-validate.json)|
|解析VERIFY-FINAL.trx / 校验逐方法存在且Passed|1884/1884、0fail/skip；82场景所有引用都找到真实方法/Passed；不重新执行|[VERIFY-FINAL.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/VERIFY-FINAL.trx)、[映射](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/coverage-mapping.json)|
|对现存发布负载/模块来源原件独立SHA256重算|464/464、24/24一致|[独立hash核对](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/published-rehash.json)|
|dotnet build FinalIntegrationProbe.csproj --artifacts-path 自有artifacts -c Release|exit0、0警告/错误|[build](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe-build.log)|
|dotnet 自有FinalIntegrationProbe.dll 自有probe-data|**exit2**，真实UI候选错误放行，I-1|[run](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe-result.log)|
|真实base-ref契约8文件导出；dotnet build LegacyReview.csproj --artifacts-path 自有legacy-artifacts；新发布Host运行旧DLL|两个构建exit0、0警告/错误；legacy run exit0|[legacy build](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/legacy-build.log)、[host build2](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/probe-build2.log)、[legacy run](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/legacy-result.log)|
|git diff --check base HEAD|exit2，仅计划文档395行EOF空白；非产品缺陷|[diff-check](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/final-integration-20261012-896415b1/diff-check.log)|

探针全部位于自有evidence，无产品ProjectReference、没有写正常bin。旧DLL探针编排中一次PowerShell变量名用了只读$Host，导致该次shell未执行run；改为只读执行已构建的DLL后exit0。该夹具编排错误不算产品RED，前后build日志保留；UI探针exit2则是明确守卫失败，不混为同类。

### 复用且检查过原始结果的主会话/专家证据

|证据|真实结果 / 采信限度|
|---|---|
|[正式Verify check log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/a740db92-f537-4cd6-9662-b2b931dd3902.log) + VERIFY-FINAL.trx|1884/1884，0 fail/skip，主会话exit0；本review读取log与XML，不声称自己再跑一次|
|[Build公开CLI复验log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/f626724b-15f0-4a69-b940-e89b224ec3ed.log)|实际0警告/0错误；读取证据，不更改checkpoint|
|[Playground latest log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/evidence/coordinator-extra-final-20261012-0946.log)|85/85、0失败、真实exit0；不是1884中的TRX例数|
|[发布/暂存依赖闭包验收](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/published-extra-acceptance.md)|独立暂存导出正常重编1884/1884；快照42/44是当时原始状态，后来只补2勾选/文档，无后续产品变更。未在本review重跑|
|[latest发布JSON](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/final-verification.json) / [闭环原log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/attempts/20261012-095051-201-5edbd57a/CLOSED-LOOP.log) / [重启原log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/attempts/20261012-095051-201-5edbd57a/RESTART.log)|PID30456/33608，33+8断言，各exit0；cache_hit=true，SQLite计数2→3，唯一AppDataPaths新id路径；布局32/32|
|[runtime-proof](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/attempts/20261012-095051-201-5edbd57a/runtime-proof.json) / [hosttrace](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/attempts/20261012-095051-201-5edbd57a/CLOSED-LOOP-HOSTTRACE.log) / [module provenance](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/attempts/20261012-095051-201-5edbd57a/loaded-provenance.json)|真实self-contained apphost、产品runtimeconfig原Hash、app-localCoreCLR等24模块；构建探针用SDK合法，运行不调用外部dotnet/SDK，不仅PATH。**不是物理无SDK设备**|
|[latest四语言CUA原证据](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/ui-source-trust-extra-2026-10-12_09-47-00/README.md)|8份背景真实窗口UIA/PNG/事件、四语言与启停/详情/关闭；非完整MainWindow，未操作真实Provider/Vault|

主会话全量及公开Build检查输入manifest也已存在，前后hash相同由主会话确认；本review另外对本change140文件独立前后核验，全部一致。没有用文件名含GREEN替代实际结果；历史RED遗漏、夹具失败、早期只读报告范围均保留，未回填或清理。

## 6. 非阻塞建议（不与I-1混级）

- **既有MINOR T-M1，保留不重开修复**：[PluginDevelopmentCompositionTests.cs:19–26](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginDevelopmentCompositionTests.cs#L19)对同一个DI Compiler查询两次Assert.Same，仅证明Singleton。生产当前确实共享，原tools更强实际Runtime/服务对象绑定探针也通过；建议未来将该更强探针纳正式回归。不是当前生产接线缺陷。
- **SUGGESTION / 文档卫生**：[计划文档:395](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/docs/superpowers/plans/2026-10-12-add-source-plugin-runtime-and-bridge.md#L395)多一个EOF空行导致diff --check exit2。本阶段不修改，不阻塞功能。

## 7. 最终风险边界与停止

- 此change是可信进程内源码扩展，ALC/Bridge/临时数据不是OS沙箱；任意C#直接访问文件/网络属于明确披露的权限边界。SQLite路径检查不是彻底TOCTOU消除，过滤不保证识别任意秘密，Future Worker/IPC未实现。
- SQLite Busy/Corrupt/只读/约束/取消等有真实操作证据；部分Locked/Unknown为安全码映射，不能宣称硬件故障已全部真实复现。同步Credential JSON/SSE适配仍占用线程，不宣称全链路非阻塞。
- 发布“无SDK运行依赖”有自包含/runtime/module/hosttrace完整证据，可采信当前设计验证方式；物理无SDK设备、完整MainWindow、安装器、真实外部Provider不属于这些证据已验证范围。不会因“不在物理无SDK设备”错误阻塞，也不会夸大证明。
- **停止在IMPORTANT报告交付，不批准最终集成、不启动产品修复或额外审查、不修改Comet verify_result/checkpoint/tasks、不开Apply guard、不归档、不git提交/推送。**后续由主会话按用户授权处理I-1；原Runtime/Feedback专家第2轮PASS和额度完整保留。
