---
comet_change: fix-assistant-decision-subscription-lifecycle
base-ref: e0e1dde
---

# AskUser 通用能力与 Approval Card 实施计划

## 目标

在不改变 Broker 并发协议和四类字段结果类型的前提下，使 `assistant.ask_user` 可被直接调用，并把桌面交互改造成紧凑、逐题、可导航和可跳过的 Approval Card。

## 文件地图

- 修改 `LoomX/Assistant/AssistantService.cs`：通用 AskUser 提示与 Skill/Bridge 解耦。
- 修改 `LoomX/Assistant/AssistantTools.cs`：工具描述。
- 修改 `LoomX/ViewModels/AskUserDialogViewModel.cs`：分页、跳过、当前页验证和值清空。
- 修改 `LoomX/Views/AskUserDialog.axaml`：Approval Card 视觉结构。
- 修改 `LoomX/Views/AskUserDialog.axaml.cs`：按钮、键盘和单选自动前进。
- 修改 `LoomX/Resources/Strings*.resx`：步骤和操作文案。
- 修改 `LoomX.Tests/Views/AskUserDialogContractTests.cs`：状态与 XAML 契约。
- 修改 `LoomX.Tests/Assistant/AssistantServiceTests.cs`、`AssistantToolsTests.cs`：通用使用语义。

## Task 1：通用 AskUser 语义（TDD）

1. 在 AssistantService/AssistantTools 测试中加入失败断言：系统提示和工具描述明确支持用户直接测试，且不要求 Skill、Bridge 或 Chrome。
2. 运行定向测试确认按预期失败。
3. 最小修改系统提示与工具描述。
4. 重跑测试并提交。

## Task 2：分页状态模型（TDD）

1. 为 `CurrentField`、步骤文本、前后导航、值保留、可选字段跳过、必填字段拒绝跳过、末页提交写失败测试。
2. 运行测试确认失败原因是分页 API 尚不存在。
3. 为 Dialog ViewModel 和字段 ViewModel 实现最小分页/清空/验证能力。
4. 重跑 ViewModel 测试并提交。

## Task 3：Approval Card 视图（TDD）

1. 更新源码契约测试，要求紧凑窗口、当前字段 ContentControl、步骤导航、Skip、Continue/Submit、关闭按钮和四类模板。
2. 更新代码后置契约测试，要求关闭/键盘路由和单选自动前进。
3. 重做 XAML 和代码后置，保持 DynamicResource 与透明主题兼容。
4. 补齐本地化资源并运行 UI 契约测试。

## Task 4：集成验证

1. 运行 AskUserDialog、AssistantViewModel、AssistantService、AssistantTools 和 UserDecisionBroker 定向测试。
2. 运行 OpenSpec strict validate、Release build 和 xUnit 串行完整测试。
3. 发布到 `outputs/2026-09-20-<time>-assistant-ask-user-approval-card`。
4. 后台启动发布包并核对进程路径；用应用级截图验证单选、多选、数字和文本页面、步骤导航、主题与取消/提交行为。
