# Feedback 第 2 轮额外授权定向复查

## 审查身份、轮次与结论

- 原 reviewer：**Maxwell**；用户授权日期：**2026-10-12**。
- 历史 **reviewRounds=1** 保留；本次额外授权实际复查为 **reviewRounds=2**，不重置为首审、不换 reviewer、不派下级、不代修。
- **结论：PASS。原 I2「短 Basic/Bearer 漏检」关闭；本轮定向范围内 CRITICAL=0、IMPORTANT=0、MINOR=0。**
- 上轮两项非阻塞 MINOR 已按约定口径关闭：512 指含宿主前缀的预展开预算，不是 Serilog 最终长度；v1 仅支持命名占位及字面花括号，不允许 format/alignment。
- 不重审 Runtime、Compiler、Credential 或其他已通过工作包；不宣称整仓全量、Playground、发布、UI 真实应用或整个 change 已通过。

## 实际差异与源码一致性

- [PluginFeedbackBridge.cs:66–67](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginFeedbackBridge.cs)：实际对比实施前快照，仅增加一行中文说明，并把 Basic/Bearer 的 {8,} 改成 +；未扩充 scheme、字符集、能力接口或模板协议。
- 解析、受限 union 投影、预展开再校验、不可覆盖来源、Toast 投递隔离、安全稳定错误与限频拒绝诊断保持原路径。
- 当前 Bridge、两份正式反馈测试、design 第9节、Skill 的 SHA-256 **全部匹配 Faraday GREEN 记录**。
- 审查前后产品、Toast、两份正式测试、design、Skill 共6文件 SHA-256 完全相同；审查期间没有修改它们。
- [实际生产差异](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E5%AE%9E%E9%99%85%E7%94%9F%E4%BA%A7%E5%B7%AE%E5%BC%82.patch)、[实施GREEN与源码对照](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E5%AE%9E%E6%96%BDGREEN%E4%B8%8E%E5%BD%93%E5%89%8D%E6%BA%90%E7%A0%81%E5%AF%B9%E7%85%A7.json)、[审查后未变核验](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E5%AE%A1%E6%9F%A5%E5%90%8E%E6%BA%90%E7%A0%81%E6%9C%AA%E5%8F%98%E6%A0%B8%E9%AA%8C.json)均保存在本轮自有证据目录。

## 原始 RED / GREEN 核验

不是仅采信实施者摘要，已直接读取原始 TRX 的计数、结果与失败正文：

| 原始证据 | 实际时间（2026-10-12，UTC+08:00） | 结果 |
|---|---|---|
| [FEEDBACK-EXTRA-RED.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/red-results/FEEDBACK-EXTRA-RED.trx) | 09:39:38–09:39:39 | 61例：40 fail / 21 pass，未中止 |
| [FEEDBACK-EXTRA-GREEN.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/green-results/FEEDBACK-EXTRA-GREEN.trx) | 09:41:09–09:41:10 | 61/61 pass，0 fail，未中止 |

RED 的40失败准确归属为：短值模板12、Text字段12、真实Toast12、模板展开拼接4。失败来自真实业务输出泄漏/产生不应存在的业务事件，不是夹具初始化错误或仅缺少 Assert.Throws 异常。

RED 使用旧正则生产源码；GREEN 使用最小修复；两次正式测试源码哈希一致。旧审查中 Capture.Dispose 递归导致的中止证据不计入本轮有效 RED。正式61例由原17项、短凭据输出40项、非Text union4项组成；不把它称作全量测试。

## Maxwell 独立还原、构建与复跑

独立 artifacts 路径在本轮证据目录中，不使用共享 bin/obj、不使用 --no-build、不修改产品来重放旧错误。实际命令：

~~~powershell
dotnet "test" "LoomX.Tests/LoomX.Tests.csproj" "--artifacts-path" "D:\AppData\Github\Loom-X\.worktrees\add-source-plugin-runtime-and-bridge\openspec\changes\add-source-plugin-runtime-and-bridge\.comet\evidence\feedback-extra-followup-20261012-maxwell-094549-19b7ae\artifacts" "--filter" "FullyQualifiedName~PluginFeedbackBridgeTests|FullyQualifiedName~PluginFeedbackOutputTests" "--logger" "trx;LogFileName=MAXWELL-ROUND2-GREEN.trx" "--results-directory" "D:\AppData\Github\Loom-X\.worktrees\add-source-plugin-runtime-and-bridge\openspec\changes\add-source-plugin-runtime-and-bridge\.comet\evidence\feedback-extra-followup-20261012-maxwell-094549-19b7ae\results" "--verbosity" "minimal"
~~~

- 实际执行还原、编译及 VSTest；命令退出码 **0**。
- [MAXWELL-ROUND2-GREEN.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/results/MAXWELL-ROUND2-GREEN.trx)：09:46:02–09:46:03，**61/61 pass，0 fail，0 skip**。
- 真实短值覆盖：Basic dTpw、Bearer abc123、混合大小写、双空格、Tab、NBSP、1字符；覆盖模板、字段、预展开拼接、Toast。
- 真实管线：Microsoft ILogger → Serilog → RuntimeLogSink/RuntimeLogBuffer，以及正式 RuntimeLogTextFormatter 和实际 ToastService.Requested。
- 拒绝均为安全 InvalidArgument；原值不进入模板、结构化标量、控制台、文件格式化输出或Toast。拒绝后有绑定 PluginId 和错误类别的 Warning 诊断。
- [构建测试日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E7%8B%AC%E7%AB%8B%E6%9E%84%E5%BB%BA%E5%A4%8D%E8%B7%91.log)、[实际命令](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E7%8B%AC%E7%AB%8B%E6%9E%84%E5%BB%BA%E5%A4%8D%E8%B7%91%E5%91%BD%E4%BB%A4.txt)、[TRX计数](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E5%8E%9F%E5%A7%8B%E4%B8%8E%E7%8B%AC%E7%AB%8BTRX%E8%AE%A1%E6%95%B0.json)保留。
- 编译带无关既有警告 CS8618、CA2024、CS8602、CS0067、xUnit1031；没有越界修复，也没有声称零警告。

## 相邻守卫：12/12组通过

在证据目录保存审查用脚本，只加载本轮独立构建的程序集，没有把守卫代码加入产品或正式测试。实际命令：

~~~powershell
pwsh -NoProfile -File "D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/运行反馈相邻守卫.ps1"
~~~

| 组 | 守卫与实际结果 |
|---|---|
| 01 | SourceContext等11个保留来源字段（含大小写变体）安全拒绝，真实事件仅安全Warning |
| 02 | {Name}和字面花括号正常，OriginalFormat、PluginId、文件输出来源保留 |
| 03 | format/alignment、未知占位、不闭合模板6例安全拒绝 |
| 04 | Null/Boolean/Integer/Real只投影选中Kind，真实属性和文件不泄漏未选中的Text凭据 |
| 05 | 含来源前缀预展开512成功；513及重复展开超预算拒绝 |
| 06 | 预展开512、Serilog quoting后1012与校准文档一致；不再把最终长度误称≤512 |
| 07 | 同实例20次短Basic拒绝，只产生10条安全Warning；真实属性、异常、文件不含原值 |
| 08 | Success/Info/Warning/Error映射、宿主前缀、Toast含前缀512/513边界正确 |
| 09 | 故障订阅移除、健康链继续；另一插件正常投递；OperationFailed无原异常，诊断不泄漏 |
| 10 | 并发120次日志调用：100业务成功 / 20 ResourceLimit；拒绝诊断继续限频 |
| 11 | logger及诊断同时故障：仍安全OperationFailed，无原异常、无递归放大 |
| 12 | 已取消调用保持取消语义，不产生业务或诊断输出 |

守卫退出码 **0**。[守卫源码](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E5%8F%8D%E9%A6%88%E7%9B%B8%E9%82%BB%E5%AE%88%E5%8D%AB.cs)、[运行脚本](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E8%BF%90%E8%A1%8C%E5%8F%8D%E9%A6%88%E7%9B%B8%E9%82%BB%E5%AE%88%E5%8D%AB.ps1)、[原始守卫输出](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-followup-20261012-maxwell-094549-19b7ae/%E7%9B%B8%E9%82%BB%E5%AE%88%E5%8D%AB%E8%BE%93%E5%87%BA.log)保存完整。

**计数口径为「正式测试61例＋独立守卫12组」；后者不是新增TRX测试例，不相加称为73例正式测试。**

## 文档核对与等级结论

- [design第9节:176](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md)及[Skill:20](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Skills/plugins/development-v1/SKILL.md)明确含前缀的预展开512预算、Serilog quoting/escaping最终长度口径和v1模板限制。
- 原 I2 最短凭据漏检在正式真实输出回归及独立复跑中均已消失；**关闭**。
- 相邻守卫没有发现由本次最小修复引入的 CRITICAL/IMPORTANT 或需保留的 MINOR。
- 过滤仍是已知凭据形态的纵深防御，不是可信进程内插件绝对防泄漏或恶意代码沙箱；不扩展承诺为识别任意秘密。

## 逐任务结论与停止边界

| task | 结论 |
|---|---|
| 3.1 Logging | **可验收本任务范围**：原I2阻塞关闭，其余上轮关闭项维持，真实输出及相邻守卫通过 |
| 3.2 Notifications | **可验收本任务范围**：短凭据真实Toast拦截、安全诊断、来源与故障隔离通过 |
| 3.3 宿主组合根 | **维持原可验收结论**；本轮没有重审整个组合根或其他已通过包 |

本轮标准守卫结果为 PASS，不请求也不启动下一轮。仅保存本报告与本轮自有证据目录；不改全局Comet状态、不勾选task、不提交/推送、不启动发布app，不触碰其他会话 artifacts、全量或Playground产物。
