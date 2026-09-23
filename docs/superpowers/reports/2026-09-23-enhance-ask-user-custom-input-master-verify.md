# AskUser 同页自由输入合入 master 验证报告

## 1. 验证范围

本报告验证 `enhance-ask-user-custom-input` 合入 `master` 后的最终工作树，重点确认：

- 单选/多选可通过 `allow_custom_input` 在同一页显示自由输入框；
- 预设选择与自由输入可以同时保留并提交；
- 文本原文分别通过 `values` / `custom_inputs` 返回助手；
- AskUser 取消后当前助手轮次不重复弹出；
- 合并没有修改插件系统实现，内置 AI 助手与插件系统边界保持隔离；
- 合并后的解决方案可构建、全量测试可通过，并能由发布包完成真实 GUI 交互。

## 2. 完整验证检查表

| 检查项 | 结果 | 证据 |
|---|---|---|
| tasks.md 全部完成 | PASS | 13 / 13 已勾选；`comet classic openspec -- instructions apply --change enhance-ask-user-custom-input --json` 返回 `all_done` |
| 实现符合 change design.md | PASS | 数据模型、Schema、校验、Broker、ViewModel、AskUserCard 与 AgentLoop 均按设计分层实现 |
| 实现符合关联 Design Doc | PASS | `docs/superpowers/specs/2026-09-20-ask-user-custom-input-design.md` 可定位，单字段同页建模、结果双映射和取消终态均有实现与测试 |
| 能力规格场景覆盖 | PASS | AskUser/UserDecision/Assistant/视图契约定向测试包含同页输入、选项共存、必填替代、敏感内容、序列化与取消生命周期 |
| proposal.md 目标满足 | PASS | 实际发布包显示单选与自由输入同页，最终助手同时收到选项与补充文本 |
| delta spec 与设计无矛盾 | PASS | `allow_custom_input`、`custom_input_placeholder`、选择字段 `max_length`、`custom_inputs` 与实现一致 |
| 关联设计文档可定位 | PASS | change design、Superpowers Design Doc、计划和本报告均存在 |

## 3. 代码与边界核对

- `LoomX/Assistant/UserDecisions/UserDecisionModels.cs` 包含选择题自由输入校验、`CustomInputs` 快照复制和 `AssistantContentPolicy` 敏感内容检查。
- `LoomX/Assistant/AssistantTools.cs` 包含 `allow_custom_input`、`custom_input_placeholder`、同字段 `max_length` 说明和根级 `custom_inputs` 序列化。
- `LoomX/Views/AskUserCard.axaml` 在单选与多选模板内按 `AllowsCustomInput` 展示输入框。
- `git diff origin/master..HEAD -- plugins LoomX.PluginHost LoomX.Plugin.Abstractions` 无输出；AskUser 合并没有修改插件 Runtime、PluginHost 或 Abstractions。
- 合并后将旧名称 `SensitiveKeyPolicy` 修正为当前 master 使用的 `AssistantContentPolicy`，避免与插件系统整合后的类型重命名冲突。

## 4. 自动化验证

### OpenSpec 严格校验

`comet classic openspec -- validate enhance-ask-user-custom-input --strict`

结果：`Change 'enhance-ask-user-custom-input' is valid`。

### Release 构建

`dotnet build .\LoomX.slnx -c Release`

结果：成功，0 error。Comet 证据日志：

`openspec/changes/enhance-ask-user-custom-input/.comet/checks/85e7498b-03ac-4285-8a06-c21617be096e.log`

### 全量测试

`dotnet test .\LoomX.slnx -c Release --no-restore --logger "console;verbosity=minimal"`

结果：`1285 / 1285` 通过，0 失败，0 跳过。Comet 证据日志：

`openspec/changes/enhance-ask-user-custom-input/.comet/checks/ec970025-3fd4-46a0-83d7-3bc2fc369841.log`

验证期间还修正了两个既有陈旧 XAML 契约断言，以及 Avalonia 全量测试中的 UI 线程隔离/文化切换调度问题；生产 UI 行为未因契约断言修正而改变。

## 5. 发布与 cua-driver 实机验收

发布目录：

`outputs/2026-09-23-ask-user-master-integration`

启动参数：`--allow-multiple-instances`。本任务实例 PID 为 `36668`，验收后已单独关闭，未影响其他 LoomX 实例。

使用提示词：

`请直接调用 assistant.ask_user，显示一个单选题：问题“请选择处理方式”，选项“方案A”“方案B”，并在同一页允许自由输入，输入框最大80字。不要解释，直接调用工具。`

CUA/UIA 证据：

1. AskUser 显示 `1 / 1`，不是旧版错误建模的 `1 / 2`；
2. 同一面板同时包含“方案A”“方案B”两个 RadioButton 和自由输入 Edit；
3. 自由输入提示为“请输入其他方案（最多80字）”；
4. 选择“方案A”后 UIA 状态为 `selected=true`；
5. 输入“同时保留这段补充说明”后，选项仍保持选中；
6. 提交后助手回复：`已收到：方案A，附补充说明「同时保留这段补充说明」。`

以上证明合并后的 master 发布包同时保留并回传了预设选择和自由输入。

## 6. 结论

| 维度 | 状态 |
|---|---|
| 完整性 | PASS：13 / 13 任务，规格与设计文档齐全 |
| 正确性 | PASS：自动化测试、严格规格校验和 GUI 核心场景均通过 |
| 一致性 | PASS：实现符合设计，未侵入插件系统 |

未发现阻止归档的 CRITICAL、WARNING 或 SUGGESTION。该 change 已准备归档并交付 master。
