---
comet_change: fix-assistant-decision-subscription-lifecycle
role: technical-design
canonical_spec: openspec
---

# LoomX AskUser 悬浮卡片与简版消息队列设计

## 1. 背景

AskUser 的 Broker、工具和四类字段已经可用，通用工具语义也已与 Skill、Browser Bridge 和 Chrome 解耦。第一版逐题 Approval Card 采用独立 Window；真实用户验收确认字段提交正确，但产品目标应是锚定在 AI 输入框上方的悬浮卡片，而不是弹窗或对话流消息。

用户还确认，AskUser 等待期间不应锁死输入框。运行中的后续消息应按 Codex 类交互先进入可管理队列，当前 Assistant 轮次完整结束后再依次发送。

## 2. 组件边界

- `AssistantService`：维持通用 AskUser 提示，不感知桌面卡片或队列 UI。
- `AssistantTools` / `UserDecisionBroker`：保持请求、Claim、Submit、Cancel 协议不变。
- AskUser 状态 ViewModel：拥有分页、当前字段验证、跳过、最终结果构造和完成状态。
- `AskUserCard`：由原 Dialog 视图转换而来的 `UserControl`，只负责字段模板、按钮、键盘路由和单选自动前进。
- `AssistantViewModel.PendingAskUser`：当前唯一悬浮卡片状态，负责把完成结果提交或取消到 Broker。
- `AssistantViewModel.QueuedMessages`：当前会话尚未正式发送的消息集合。
- `AssistantView`：承载消息流、队列、输入框及 overlay；不创建 AskUser 第二顶层窗口。

## 3. 悬浮布局

Assistant 页面底部形成一个共享 composer anchor：

```text
┌──────── AskUserCard（可选，MaxWidth≈560）────────┐
└─────────────────────────────────────────────────┘
       ┌──── 待发送队列（可选，紧凑列表）────┐
       └───────────────────────────────────────┘
┌──────── 输入容器（MaxWidth≈760）───────────────┐
└─────────────────────────────────────────────────┘
```

AskUserCard 与队列使用 overlay 层覆盖消息区底部，不成为 `Messages` 项，也不创建原生 Popup/Window。输入容器保持正常布局；overlay 根据输入容器实际位置和高度向上排列，间距约 10–12px。卡片使用 DynamicResource、边框、圆角和轻阴影，不显示独立标题栏或右上角关闭按钮。

窄窗口中，卡片、队列和输入容器按可用宽度收缩；宽窗口中通过 MaxWidth 避免横向拉伸产生大块空白。

## 4. AskUser 状态与完成

`PendingAskUser` 只允许一个活动实例，并保存 RequestId、SessionId 和字段状态。卡片提供：

- Previous / Next；
- Skip（仅可选字段）；
- Continue / Submit；
- Cancel（仅 `allow_cancel=true`，位于底部低强调区域）。

提交或取消时，先把卡片标记为已完成，防止重复点击，再由 `AssistantViewModel` 校验当前 Ownership 并调用 Broker。页面离开、会话切换、停止生成或 Dispose 会幂等终止卡片；迟到回调只能被忽略。

## 5. 简版队列数据模型

队列项至少包含：

- 稳定 `Id`；
- `SessionId`；
- `Text`；
- `CreatedAt`；
- 显式状态（Queued/Dispatching/Paused）。

本次 UI 只显示顺序、文本预览和删除按钮。数据模型不暴露“永远不可编辑或不可排序”的契约，为后续 Codex 风格能力保留扩展空间。

## 6. 发送状态机

用户在空闲状态发送：直接启动一轮 Assistant 请求。

用户在 `IsRunning=true` 时发送：

1. 捕获并清空输入框；
2. 创建绑定当前 SessionId 的 Queued 项；
3. 不写入正式消息历史；
4. 不调用当前 AgentLoop。

当前轮次正常结束后，由单一队列处理循环取出队首，将其转换为正式用户消息并启动下一轮。AskUser 卡片完成只是当前轮次内部的工具返回，不是出队边界。

异常、服务不可用或用户停止时，处理循环暂停，剩余项目保留。切换会话时只展示目标会话队列，不得跨 Session 发送。

## 7. 当前版本与后续版本边界

当前版本实现：

- 运行中发送入队；
- 顺序显示；
- 删除未发送项；
- 当前轮次正常结束后顺序出队；
- 失败暂停；
- SessionId 隔离。

后续完整 Codex 风格需求见 `docs/superpowers/specs/2026-09-20-assistant-message-queue-requirements.md`，包括编辑、拖动排序、立即发送、Steer、Stop-and-Send、跨重启恢复和更完整的失败恢复。本次结构必须允许这些能力在不替换队列核心模型的前提下增加。

## 8. 测试

- 悬浮卡片契约：无 Window、无标题栏/右上角关闭按钮、overlay 锚定输入容器、响应式宽度。
- AskUser 状态：分页、值保留、跳过、必填、取消、最终结果。
- 队列：运行中入队、删除、FIFO、完整轮次边界、失败暂停、SessionId 隔离。
- Broker：Claim、Submit、Cancel、请求结束和迟到事件。
- Avalonia：异步准备后 UI 对象始终在 Dispatcher UI 线程创建和关闭。
- 桌面验收：使用本地 `cua-driver` 获取 LoomX 顶层窗口截图；优先 UIA 和虚拟光标，必要时才使用系统鼠标。
