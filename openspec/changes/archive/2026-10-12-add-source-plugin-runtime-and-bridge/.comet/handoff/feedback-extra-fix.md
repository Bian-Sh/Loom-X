# Feedback 额外一次修复实施交接

## 状态与授权边界

- 状态：**DONE**。日期：2026-10-12（Asia/Shanghai）。
- 本次只执行用户明确授权的残留 I2 额外一次修复，**不是新首审，不承担复查，不宣告整个 change 放行**。
- 唯一工作目录：[指定共享工作树](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。
- 已先读设计、两份正式 Feedback 测试、Bridge 实现、插件开发 Skill 与 [原复查交接](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-review.md)。
- 未派下级，未提交/推送 Git，未勾选任务，未修改 Comet 状态、reviewRounds 或原 reviewer 记录。未覆盖、移动或清理其他会话产物。
- 后续由主会话恢复原 reviewer **Maxwell** 进行定向复查；本交接不替代其结论。

## 根因与最小修复

原 Basic/Bearer 模式对凭据正文使用至少 8 个字符的长度下限，导致合成短 Basic/Bearer 凭据绕过模板、Text 字段、预展开文本与 Toast 的共同校验。

实际生产改动只有同一正则的长度下限及一行中文解释：

~~~text
旧：(?:bearer|basic)\s+[a-z0-9+/_=-]{8,}
新：(?:bearer|basic)\s+[a-z0-9+/_=-]+
~~~

拒绝非空凭据，不增加 scheme，不扩充 token 字符集，不改变大小写/空白规则或公开协议。稳定错误仍为 InvalidArgument；拒绝诊断仍只有绑定 PluginId 与错误类别，不接收原模板、字段值、通知正文或异常原值。

## 实施文件

- [LoomX/Services/PluginFeedbackBridge.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginFeedbackBridge.cs)
- [LoomX.Tests/Plugins/PluginFeedbackBridgeTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginFeedbackBridgeTests.cs)
- [LoomX.Tests/Plugins/PluginFeedbackOutputTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginFeedbackOutputTests.cs)
- [LoomX/Skills/plugins/development-v1/SKILL.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Skills/plugins/development-v1/SKILL.md)
- [openspec/changes/add-source-plugin-runtime-and-bridge/design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md)
- 新建本交接 [feedback-extra-fix.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-extra-fix.md)。
- 仅在 [本次独立证据目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d) 内新建证据和独立构建产物。

生产文件相对本次起点只改一行正则并加一行注释；两份测试只追加测试；design.md 仅修改第 9 节 Feedback 的 template/预算文字；Skill 仅修改同一反馈段的预算文字。快照、逐文件补丁及 SHA-256 见证据目录。全部文件严格验证为 UTF-8，无 BOM；保留各文件原有换行（Skill 原有 1 处 CRLF 未转码）。

## 正式测试覆盖

- 12 个合成变体分别走模板、Text 字段、真实 Toast 订阅，共 36 项：Basic dTpw、Bearer abc123；混合大小写；双空格；Tab；不间断空格；一字符 token。
- 4 项模板与字段分开都安全、但展开后拼成短 Basic/Bearer 的用例，保护预展开校验。
- 真实输出链为 Microsoft ILogger → Serilog → RuntimeLogSink → RuntimeLogBuffer；同时核验 Serilog 原始模板/ScalarValue、正式 RuntimeLogTextFormatter 输出及实际 ToastService.Requested 消息。
- 先捕获调用错误、再检查实际输出；RED 不依赖 Assert.Throws 缺少异常，也不是夹具初始化失败。
- GREEN 同时断言拒绝错误为 InvalidArgument、异常消息/控制台/模板/字段/文件格式化输出均不含合成原值，且存在只含绑定来源与错误类别的 Warning 诊断。
- 额外 4 项 tagged-union 守卫：Null/Boolean/Integer/Real 不输出未选中的 Text 成员，维持既有联合值映射。
- 原有 17 项继续运行，覆盖敏感字段、绑定身份、防覆盖、速率/长度、模板 format/alignment、展开预算及故障 Toast 订阅隔离。

## 实际命令与结果

两次均在上述唯一工作目录执行，均实际还原、构建并运行测试；没有使用 --no-build。RED 与 GREEN 使用不同的 --artifacts-path，未使用共享 bin/obj。

### RED：修改测试，生产逻辑仍是旧正则

~~~powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path "D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/red-artifacts" --filter "FullyQualifiedName~PluginFeedbackBridgeTests|FullyQualifiedName~PluginFeedbackOutputTests" --logger "trx;LogFileName=FEEDBACK-EXTRA-RED.trx" --results-directory "D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/red-results" --verbosity minimal
~~~

- 实际结果：**61 总计，21 通过，40 失败，0 跳过；退出码 1**。
- 40 项失败全部为实际输出集合中的 Assert.DoesNotContain 失败：模板 12、字段 12、拼接展开 4、Toast 12。
- TRX 中可直接看到 RuntimeLogEntry 的 Information 消息与 ToastNotification 包含合成 Basic dTpw / Bearer abc123，证明旧逻辑真实泄漏；没有用夹具失败冒充 RED。
- [原始 RED TRX](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/red-results/FEEDBACK-EXTRA-RED.trx)；[RED 控制台与构建记录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/RED-console.log)；[RED 计数与实际泄漏样例](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/RED-summary.json)；[RED 源文件摘要](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/RED-source-sha256.json)。

### GREEN：最小拒绝修复后重新独立构建

~~~powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path "D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/green-artifacts" --filter "FullyQualifiedName~PluginFeedbackBridgeTests|FullyQualifiedName~PluginFeedbackOutputTests" --logger "trx;LogFileName=FEEDBACK-EXTRA-GREEN.trx" --results-directory "D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/green-results" --verbosity minimal
~~~

- 实际结果：**61 总计，61 通过，0 失败，0 跳过；退出码 0**。
- [原始 GREEN TRX](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/green-results/FEEDBACK-EXTRA-GREEN.trx)；[GREEN 控制台与构建记录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/GREEN-console.log)；[GREEN 计数与覆盖分组](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/GREEN-summary.json)；[GREEN 源文件摘要](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/feedback-extra-fix-20261012-1c846d/GREEN-source-sha256.json)。
- 定向 git diff --check 退出码 0；新建/未跟踪文件另与本次 UTF-8 快照执行逐文件 git diff --no-index 核对，补丁均保存于独立目录。

## 两项非阻塞文档同步

1. 两份文档均明确：日志 **512 是包含来源前缀的 Bridge 预展开预算**，不保证 Serilog quoting/escaping 后最终输出仍不超过 512；通知摘要使用包含来源前缀的 512 总预算。
2. 两份文档均明确：v1 template 仅允许 **{Name}** 和 **{{ / }}** 字面花括号，拒绝 format/alignment；未扩大模板协议。

## 未扩大验收与已有风险

- 本次只运行两份 Feedback 测试，没有宣称全仓测试、整项 change 或 reviewer 复查通过；未启动 UI、未操作真实 Provider/Vault。
- 构建成功但带既有无关警告（CS8618、CA2024、CS8602、CS0067、xUnit1031）；未越界修复。
- 共享树仍有原有大量其它模块修改，以及期间其它会话新增的授权/Runtime 交接状态文件。它们不是本次产物，未覆盖或清理。
- 过滤仍为纵深防御，不是可信进程内代码的绝对防泄漏沙箱。
