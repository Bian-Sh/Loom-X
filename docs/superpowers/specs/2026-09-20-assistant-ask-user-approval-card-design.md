---
comet_change: fix-assistant-decision-subscription-lifecycle
role: technical-design
canonical_spec: openspec
---

# LoomX AskUser 通用能力与 Approval Card 设计

## 1. 背景

AskUser 的 Broker、工具和四类字段已经可用，但系统提示把它主要描述为资料不可用时的兜底，现有 620×680 Dialog 又一次性展示全部字段。用户要求它可以脱离 Skill、Browser Bridge 和 Chrome 被直接调用，并采用 Beautiful UI Approval Card 的逐题交互，同时保持 LoomX 自身主题。

## 2. 组件边界

- `AssistantService`：只负责告诉模型 AskUser 的通用使用原则，不决定具体 UI。
- `AssistantTools`：维持独立注册和无限等待契约，更新描述为通用结构化交互。
- `AskUserDialogViewModel`：拥有分页、当前字段验证、跳过和最终结果构造。
- 字段 ViewModel：继续拥有具体值，并增加清空与当前有效性支持。
- `AskUserDialog`：负责页面切换视觉、按钮/键盘路由和单选短延迟自动前进。
- `AssistantViewModel` / `UserDecisionBroker`：继续负责 Claim、Submit、Cancel 与请求级订阅，不因 UX 改造改变并发模型。

## 3. ViewModel 状态

新增只读或通知属性：

- `CurrentFieldIndex`
- `CurrentField`
- `StepText`
- `HasPreviousField`
- `HasNextField`
- `IsLastField`
- `CanSkipCurrentField`
- `CanContinueCurrentField`
- `PrimaryActionText`

新增行为：

- `MovePrevious()`
- `MoveNextWithoutValidation()`
- `TryAdvanceCurrentField()`
- `TrySkipCurrentField(out bool shouldSubmit)`
- `ClearValue()`（字段层）

构造时不再把其他未访问必填字段的错误全部显示出来。当前页前进时只显示当前字段错误；最终提交时执行完整 `ValidateSubmission`。

## 4. View 映射

Dialog 使用窄窗口和根 Border 卡片：

1. Header：当前字段标题、必填标记、可选关闭按钮。
2. Context：请求问题、说明、字段描述、影响摘要。
3. Body：当前字段 ContentControl，通过四个 DataTemplate 渲染。
4. Footer：上一页、步骤、下一页；Skip；Continue/Submit。

选择项使用整行点击区域和现有 RadioButton/CheckBox 控件，数字与文本输入使用项目统一输入样式。所有背景、边框、文字、危险和强调状态从 DynamicResource 读取。

## 5. 交互规则

- Previous/Next 箭头用于回看，不清空输入。
- Continue 要求当前字段有效；最后一页执行完整提交。
- Skip 只对可选字段生效，先清空当前值再前进或提交。
- 非末页单选后短延迟自动前进；末页单选不自动提交。
- Escape 和关闭按钮仅在 `AllowCancel` 时返回 false。
- 单行输入 Enter 继续；多行输入 Ctrl+Enter 继续/提交。
- XAML 页面切换动画不改变索引与结果状态。

## 6. 通用工具语义

系统提示将明确：AskUser 可用于用户主动测试、偏好收集、必要输入、歧义澄清和行动确认。调用它不需要加载领域 Skill，也不需要 Bridge/Chrome。只有网页访问任务才遵循 Browser Bridge Skill 与 Session 租约。

## 7. 测试与交付

- ViewModel 单测：分页、值保留、跳过、必填、最终结果。
- 工具/提示契约：直接测试 AskUser 不依赖 Skill/Bridge。
- XAML/代码后置契约：紧凑尺寸、当前字段 ContentControl、步骤和按钮、关闭/键盘/自动前进。
- 既有 Broker 并发、生命周期和敏感日志测试全部回归。
- 完整串行测试、Release build、OpenSpec strict validate、发布包与桌面验收。
