# Comet Subagent Progress

- Change: structured-config-assistant-decisions
- Plan: docs/superpowers/plans/2026-09-16-structured-config-assistant-decisions.md
- Review mode: thorough
- Current task: Whole branch review
- Stage: review
- Fix round: 0

## Rulings
- Task 1 同时向 `LoomX` 与 `LoomX.Tests` 添加 Tomlyn 2.10.1，以满足 OpenSpec 1.1。





- Task 2 允许补充不可变 `TomlReadResult`，字段固定为 `Exists`、`IsValid`、`TopLevelKeys`、`Errors`；示例使用既有 `TomlValue.Value`。

- Task 4 首轮审查接受两个 Important：敏感键名必须在 read/get 安全投影中隐藏；嵌套结构字符串必须同时受 JSON Schema 和运行时长度约束。
- Task 4 审查 Minor（日志测试覆盖）纳入首轮修复，以覆盖 Patch JSON、敏感用户文本、服务错误与异常文本不进入 ToolResult/日志。
- Task 4 审查 Minor（OpenSpec tasks.md 未在实现 diff）不属于实现缺陷；按 Comet 分工由协调者在审查通过后统一勾选，避免实现代理修改流程状态。
- Task 4 计划中的通用 Step 2/Step 6 文本与其他任务重复；为满足 task-checkoff 唯一性，最小改名为“运行 TOML 工具测试确认红灯”和“运行 TOML 工具定向测试并提交”，不改变任务语义。
- Task 5 首轮审查接受 Important 1–5：补齐内容级敏感检测、字段判别封闭性、Submit 单次快照、无订阅者/释放收敛与 OwnerId 日志安全，进入修复轮 1。
- Task 5 首轮审查 Important 6 不作为实现代理缺陷：OpenSpec 勾选按 Comet 分工由协调者在复审通过后完成；4.1–4.2 届时勾选，4.4 因还覆盖 Task 6 的 ToolResult 边界延迟到 Task 6 后整项勾选。代价是 4.4 会多保持一个任务周期未完成，但避免虚假完成。
- Task 5 审查 Minor（UserDecisionModels.cs 文件较大）本轮不拆分：计划明确该文件承载模型与集中校验，新增拆分文件会扩大任务写集；保留给最终整分支审查复核。代价是短期维护认知成本较高。
- Task 5 修复轮 1 复审结论：Important 1–5 全部 addressed，修复 diff 无新增 Critical/Important/Minor breakage。
- Task 5 计划中的通用 Step 6 文本与其他任务重复；为满足 task-checkoff 唯一性，最小改名为“运行 AskUser 模型与 Broker 定向测试并提交”，不改变任务语义。
- Task 6 首轮审查接受 2 个 Critical 与 2 个 Important：Text 原文不得进入 ToolResult/Session；AskUser 必须拒绝完整 TOML、请求/响应正文、Header 形态并封闭运行时属性；owner 传播不得依赖跨 yield 的 AsyncLocal；人工决策不得继承默认 30 秒工具超时，进入修复轮 1。
- Task 6 的 OpenSpec 4.3–4.4 与计划 Step 勾选继续由协调者在复审 clean 后统一完成，避免在阻断缺陷未修复时虚假标记完成。代价是状态文件会晚于首轮实现提交更新。
- Task 6 修复轮 1 复审：Text 安全结果、运行 owner 与无限人工等待已 addressed；Header 边界仍漏掉 `Server`、`Date`、`Location`、`Tenant` 等无连字符名称，接受该剩余 Critical，进入修复轮 2。修复应封闭单 token ASCII Header 名，同时保留带空格的普通短句。
- Task 6 修复轮 2 复审结论：剩余 Header 边界 finding 已 addressed，修复 diff 无新增 Critical/Important breakage。
- Task 6 计划 Step 2 与 Step 6 最小改名为“运行 AskUser 工具测试确认红灯”和“运行 AskUser 工具与会话恢复定向测试并提交”，确保 Comet task-checkoff 文本语义明确且全计划唯一。
- Task 6 complete：`assistant.ask_user`、会话恢复、敏感边界、稳定 owner 与人工等待生命周期均通过两轮修复后的 thorough review；OpenSpec 4.3–4.4 已由协调者统一勾选。
- Task 7 首轮审查接受 1 个 Critical、2 个 Important：Broker request ownership 必须原子且排他，未 claim 的 UI 不得取消他人请求；生产页面/主窗口必须接入激活、停用与释放；事件流程必须补齐安全结构化日志，进入修复轮 1。Minor 本地化同时纳入修复，避免新增硬编码用户文案。
- Task 7 修复轮 1 已实现并进入 scoped re-review：以真实 Broker 原子 claim、生产生命周期、安全日志和 Locale 回归测试作为首轮 finding 的验收边界。
- Task 7 complete：原子 request ownership、生产生命周期、安全结构化日志与 Locale 资源已通过第 1 轮 scoped re-review；计划 Step 1–7 与 OpenSpec 5.1–5.3 已由协调者统一勾选。
- Task 8 实现已完成并进入 thorough review；资料通道契约、挑战交还、安全边界、全量验证与 standalone 发布为审查范围，formatter 全仓失败按既有基线单独核查。
- Task 8 首轮审查接受 1 个 Important 与 1 个 Minor：历史会话恢复必须应用当前资料通道/挑战安全提示；standalone 验证必须区分 bootstrap 与实际应用进程并给出可复核日志，进入修复轮 1。
- Task 8 修复轮 1 已实现并进入 scoped re-review：历史会话恢复会以唯一当前 System Prompt 替换旧策略；新 standalone 产物版本绑定代码 HEAD，并以多实例参数完成同 PID Path/日志核验。
- Task 8 complete：资料通道顺序、网站挑战交还、历史会话当前策略、无绕过边界与可追溯 standalone 发布均通过修复轮 1 scoped re-review；计划 Step 1–8 与 OpenSpec 6.1–7.4 已由协调者统一勾选。
- 整分支 review 开始：以原始基线 a9e755d2 到当前完成 HEAD 覆盖 TOML、AskUser、UI 生命周期、敏感边界、资料通道、数据库路径、日志与 standalone 交付；整分支仅允许一个完整修复 wave。
