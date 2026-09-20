---
comet_change: fix-assistant-decision-subscription-lifecycle
base-ref: e0e1dde
---

# AskUser 悬浮卡片与简版消息队列实施计划

## 目标

在不改变 Broker 并发协议和四类字段结果类型的前提下，使 `assistant.ask_user` 可被直接调用，将第一版独立 Window 改为输入框上方悬浮卡片，并增加兼容后续 Codex 风格能力的简版会话消息队列。

## 文件地图

- 修改 `LoomX/Assistant/AssistantService.cs`、`AssistantTools.cs`：保持通用 AskUser 语义。
- 修改 `LoomX/ViewModels/AssistantViewModel.cs`：`PendingAskUser`、队列状态与串行处理循环。
- 修改/重命名 `LoomX/ViewModels/AskUserDialogViewModel.cs`：卡片完成状态与现有分页能力。
- 修改/重命名 `LoomX/Views/AskUserDialog.*`：由 Window 转为悬浮卡片 UserControl。
- 修改 `LoomX/Views/AssistantView.axaml(.cs)`：overlay、队列、输入宽度和卡片交互。
- 修改 `LoomX/Resources/Strings*.resx`：队列、取消和等待状态文案。
- 修改 `LoomX.Tests/Views/AskUserDialogContractTests.cs`、`AssistantViewStyleTests.cs` 与 Assistant ViewModel 测试。
- 修改 `LoomX.Tests/Views/AssistantDecisionLifecycleTests.cs`：显式 UI 线程调度。

## Task 1：悬浮卡片契约（TDD）

1. 更新源码契约测试，要求 AskUser 视图不再继承 Window、不调用 `ShowDialog`、不包含标题栏或右上角关闭按钮。
2. 增加 AssistantView overlay 契约：卡片锚定输入容器上方，具有受控 MaxWidth，不进入 Messages。
3. 运行定向测试，确认因现有 Dialog 实现而失败。
4. 最小实现 UserControl 卡片与 `PendingAskUser` 完成链路。

## Task 2：简版消息队列（TDD）

1. 为运行中入队、FIFO、删除、正常轮次结束后出队、失败暂停和 SessionId 隔离补充失败测试。
2. 运行测试，确认现有 `SendCommand` 在运行中不可执行而失败。
3. 增加稳定队列项模型、会话队列集合和单一处理循环。
4. 在 AssistantView 输入区上方显示紧凑队列，提供删除入口；不实现编辑、排序或 Steer。

## Task 3：生命周期与线程稳定性（TDD）

1. 使用现有 `AssistantDecisionLifecycleTests.MainWindowViewModel_Dispose幂等释放Assistant并收敛已Claim请求` 复现 UI 线程失败。
2. 把所有 Avalonia UI 对象创建和关闭显式调度到 `Dispatcher.UIThread`。
3. 重复运行该测试和相关生命周期测试，确认不依赖执行顺序。

## Task 4：集成与交付

1. 运行 AskUser、AssistantViewModel、Broker、队列与生命周期定向测试。
2. 运行 OpenSpec strict validate、Release build 和完整测试。
3. 发布到新的 `outputs/2026-09-20-<time>-assistant-ask-user-floating-card`。
4. 使用本地 `cua-driver` 获取 LoomX 顶层窗口，验证悬浮卡片、输入共存、队列删除和顺序出队；优先后台 UIA/虚拟光标，必要时才使用系统鼠标。
5. 更新验证报告并进入 Comet verify。
