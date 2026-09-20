# Comet Design Handoff

- Change: fix-assistant-decision-subscription-lifecycle
- Phase: design
- Mode: compact
- Context hash: 60317dc656bb09c75f866cea85f6a5b9bcc95be68681d4c90b2290ded12a6302

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/fix-assistant-decision-subscription-lifecycle/proposal.md

- Source: openspec/changes/fix-assistant-decision-subscription-lifecycle/proposal.md
- Lines: 1-27
- SHA256: 36f16195e947b31770685728e33f6e7c0b81a928655052cc51684be5d57748e1

```md
## Why

AskUser 已经具备结构化收集单选、多选、数字和文本的底层能力，但当前规格与系统提示把它过度关联到高影响配置决策、资料兜底、Skill 和 Browser Bridge，容易让模型拒绝用户明确要求的 AskUser 测试，也让功能显得依赖 Chrome。现有大型表单 Dialog 同时展示全部字段，缺少逐题引导、跳过、步骤导航和紧凑反馈，不符合用户指定的 Approval Card 体验。

## What Changes

- 将 `assistant.ask_user` 明确为通用 Human-in-the-loop 工具：用户明确要求测试、收集偏好、澄清歧义、确认行动或输入结构化数据时均可直接调用。
- 明确 AskUser 不依赖任何 Skill、Browser Bridge、Chrome Extension、搜索或资料通道；只有真实网页任务才按 Skill 指引启用 Bridge。
- 保留决策订阅的请求级生命周期：导航不激活，真实 Assistant 请求开始后订阅，请求结束后解除。
- 将 AskUser Dialog 重做为主题协调的紧凑 Approval Card：一页一个字段、步骤导航、跳过、继续/提交、关闭和键盘操作。
- 继续支持现有四种字段类型和 Broker Submit/Cancel 契约，不把网页参考中的 React 实现或固定配色直接移植到 Avalonia。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `assistant-user-decisions`: 扩展 AskUser 的通用可用性，并定义 Approval Card 的分页、导航、跳过、取消、验证与提交行为。

## Impact

- 影响 Assistant 系统提示、`assistant.ask_user` 工具描述和相关契约测试。
- 影响 `AskUserDialogViewModel`、字段 ViewModel、`AskUserDialog.axaml` 与代码后置。
- 增加本地化资源、分页与交互测试；不改变数据库、Browser Bridge、SkillStore 或 UserDecisionBroker 的外部协议。

```

## openspec/changes/fix-assistant-decision-subscription-lifecycle/design.md

- Source: openspec/changes/fix-assistant-decision-subscription-lifecycle/design.md
- Lines: 1-53
- SHA256: 69b4a635dd47eb68e2160d752a8c9d2367bd528509336db6251219e009284083

```md
## Context

见 `proposal.md`。底层 `assistant.ask_user` 工具本身由 ToolRegistry 直接注册，实际并不依赖 Skill 或 Browser Bridge；过度限制来自规格措辞与系统提示中的使用场景偏置。当前 Dialog 为 620×680 的全部字段表单，无法表达用户指定的逐题 Approval Card 交互。

视觉与交互参考来自 Beautiful UI 的 Approval Card registry item：单题分页、步骤计数、Skip/Continue、单选自动前进和多选等待确认。实现只借鉴交互模型，不复制 React/Tailwind 代码或配色。

## Goals / Non-Goals

**Goals:**

- 让模型在用户明确要求或确有交互需要时直接调用 AskUser。
- 让 AskUser 与 Skill、Bridge、Chrome 生命周期彻底解耦。
- 用紧凑、逐题、可回看且不丢值的 Approval Card 替换大型全量表单。
- 保持四类字段、敏感信息边界、Broker Submit/Cancel 和请求级订阅生命周期稳定。

**Non-Goals:**

- 不增加新的字段类型或改变 UserDecisionResult 的值类型。
- 不为单选/多选混入“自定义文本”复合返回值；自由文本继续使用独立 `text` 字段，避免破坏 option id 契约。
- 不修改 Browser Bridge 租约协议、Skill 内容加载协议或 Chrome Extension。

## Decisions

### 1. AskUser 是通用工具，而不是资料兜底工具

更新系统提示与工具描述：用户明确要求测试 AskUser、收集选择/数字/文本、澄清歧义或确认行动时，可以直接调用。普通步骤“不强制询问”只表示默认不打扰，不构成禁止条件。Skill 与 Bridge 仅在任务本身需要领域知识或浏览器时加载。

### 2. 每个 UserDecisionField 对应一页

`AskUserDialogViewModel` 增加当前索引、当前字段、步骤文本、前后导航、跳过、继续和提交状态。字段 ViewModel 继续保存真实输入，因此来回切换不会丢值。最终仍由 `TryBuildResult` 一次性验证和构造原有字典结果。

### 3. 跳过只处理当前可选字段

可选字段可跳过并清空当前值；必填字段的跳过按钮禁用。最后一页跳过后尝试提交整个请求。关闭按钮和 Escape 只在 `allow_cancel=true` 时取消整个请求。

### 4. 单选自动前进属于 View 交互

选择单选项后由 Dialog 代码后置执行短延迟自动前进；ViewModel 只提供确定性的 `TryAdvanceCurrentField`。多选、数字和文本必须由用户点击继续。这样业务状态可单元测试，动画与延迟不会渗入模型层。

### 5. 主题与动画采用 Avalonia 原生能力

窗口使用固定紧凑宽度、内容驱动高度、现有 DynamicResource、无系统装饰和 Owner 居中。字段内容使用 ContentControl/DataTemplate 切换，并使用轻量透明度/位移过渡；窗口与内容在禁用动画时仍完整可用。不会照搬参考页面的黑色主题。

### 6. “Something else”映射为 text 字段

现有 selection 返回 option id，直接加入自由文本会导致返回类型含义不稳定。因此选择页只显示定义的 options；需要补充文本时，AI 在同一请求中追加一个 `text` 字段，它会作为下一页呈现，并使用类似参考图的透明输入视觉。

## Risks / Trade-offs

- [逐题模式隐藏了其他字段] → 提供明确步骤计数、前后导航和最终统一校验。
- [单选自动前进可能过快] → 使用短延迟，并仅在当前页和选中项仍有效时前进。
- [可选字段与跳过含义混淆] → Skip 明确清空当前字段；Cancel 明确取消整个请求。
- [窗口动态高度动画在不同平台表现不同] → 高度自适应优先，动画为渐进增强，不作为提交逻辑前提。

```

## openspec/changes/fix-assistant-decision-subscription-lifecycle/tasks.md

- Source: openspec/changes/fix-assistant-decision-subscription-lifecycle/tasks.md
- Lines: 1-21
- SHA256: 98e52f07b07e4d886f51cdfb9f30ae41a2243f9a3875f61de079cb6904895d8a

```md
## 1. 通用 AskUser 契约

- [ ] 1.1 先补充失败测试，证明用户明确要求测试 AskUser 时系统提示允许直接调用且不要求 Skill、Bridge 或 Chrome
- [ ] 1.2 更新 Assistant 系统提示、工具描述和规格测试，使 AskUser 成为通用 Human-in-the-loop 工具

## 2. Approval Card 状态模型

- [ ] 2.1 先为当前字段、步骤导航、跳过、必填限制、值保留和最终提交补充失败测试
- [ ] 2.2 实现 AskUserDialogViewModel 的逐题分页状态与字段清空/当前页验证能力

## 3. Approval Card 视图

- [ ] 3.1 先更新 XAML/代码后置契约测试，覆盖紧凑卡片、步骤导航、关闭、Skip、Continue/Submit 和四类字段模板
- [ ] 3.2 重做 AskUserDialog XAML 与代码后置，接通导航、单选自动前进、键盘行为、主题资源和 Broker 提交/取消
- [ ] 3.3 补齐中英日繁体本地化，并验证透明/非透明主题下不使用固定网页配色

## 4. 集成与交付

- [ ] 4.1 运行 AskUser、AssistantService、Broker 和生命周期定向测试，修复回归
- [ ] 4.2 运行 OpenSpec strict validate、Release build 和完整串行测试
- [ ] 4.3 重新发布桌面包到带可读时间的 outputs 目录，并完成 Approval Card 桌面交互验收

```

## openspec/changes/fix-assistant-decision-subscription-lifecycle/specs/assistant-user-decisions/spec.md

- Source: openspec/changes/fix-assistant-decision-subscription-lifecycle/specs/assistant-user-decisions/spec.md
- Lines: 1-80
- SHA256: 78b69d96128cf09e075b10253b4da026df8e3e9d807b5021dff81502b1f0a81a

```md
## MODIFIED Requirements

### Requirement: Assistant 必须支持结构化用户决策请求
系统 SHALL 支持由助手直接发起包含标题、问题、字段、选项、默认值、必填标记和可取消状态的结构化 AskUser 请求，并 SHALL 支持单选、多选、数字输入和自由文本字段。AskUser SHALL 是通用 Assistant 工具，不得要求预先加载 Skill、启动 Browser Bridge、连接 Chrome Extension或具备搜索能力。

#### Scenario: 用户明确测试 AskUser
- **WHEN** 用户要求展示或验收 AskUser 的单选、多选、数字或文本交互
- **THEN** 助手直接调用 `assistant.ask_user`，不得以缺少 Skill、Bridge、Chrome 或资料通道为由拒绝

#### Scenario: 发起组合决策
- **WHEN** 助手需要同时确认上下文窗口、reasoning levels 和补充说明
- **THEN** 系统可以在一个 AskUser 请求中按字段逐页展示对应的单选、多选和文本输入

#### Scenario: 用户取消决策
- **WHEN** 用户关闭或取消允许取消的 AskUser Card
- **THEN** 助手收到结构化取消结果，不得把取消解释为用户同意任何默认值

### Requirement: AskUser 必须控制询问时机和信息边界
系统 SHALL 允许调用方说明询问原因和影响摘要；普通内部步骤默认不强制询问，但用户明确请求、输入缺失、存在歧义、需要偏好或行动确认时 SHALL 允许直接使用 AskUser。桌面端 SHALL 仅在活动 Assistant 请求期间订阅用户决策 Broker，页面挂载、导航、模型选择或配置浏览不得单独激活订阅；AskUser 展示内容不得包含 API Key、Authorization、完整请求正文或其他敏感数据。

#### Scenario: 高影响配置决策
- **WHEN** Catalog Profile 缺少关键字段或配置变化需要用户决定是否稍后重启 Codex
- **THEN** 助手可以发起 AskUser，并展示安全摘要和可选行动

#### Scenario: 普通内部步骤
- **WHEN** 助手拥有完成普通内部步骤所需的全部信息且用户没有要求确认
- **THEN** 系统不强制额外弹出 AskUser，但不得禁止模型在合理场景主动调用

#### Scenario: 通用澄清与偏好收集
- **WHEN** 用户输入存在歧义、缺少必要选择，或用户要求收集偏好
- **THEN** 助手可以直接调用 AskUser，不需要加载任何领域 Skill

#### Scenario: AskUser 与 Browser Bridge 解耦
- **WHEN** AskUser 请求不涉及网页读取或浏览器自动化
- **THEN** 系统不得启动 Browser Bridge，也不得要求 Chrome Extension 在线

#### Scenario: 页面导航不激活决策订阅
- **WHEN** 用户进入或离开 Assistant、Provider、控制台页面，且没有正在发送的 Assistant 请求
- **THEN** 系统不订阅用户决策 Broker，也不记录“助手决策订阅已激活”

#### Scenario: 用户请求激活决策订阅
- **WHEN** 用户发送 Assistant 请求且服务初始化成功
- **THEN** 系统在 AgentLoop 和工具执行前订阅用户决策 Broker，使模型可以直接或在加载 Skill 后调用 `assistant.ask_user`

#### Scenario: 用户请求结束解除决策订阅
- **WHEN** Assistant 请求完成、失败、取消或因页面离开而停止处理用户决策
- **THEN** 系统解除 Broker 订阅，并对已领取的决策请求执行幂等取消，不能遗留等待项

## ADDED Requirements

### Requirement: AskUser 必须以逐题 Approval Card 完成输入和提交
桌面端 SHALL 使用与应用主题协调的紧凑 Approval Card，一次展示一个字段，并提供步骤计数、前后导航、跳过、继续/提交和允许取消时的关闭入口。字段切换 SHALL 保留输入，最终 SHALL 继续通过 Broker 提交结构化结果。

#### Scenario: 逐题浏览组合请求
- **WHEN** AskUser 请求包含多个字段
- **THEN** Card 每次只展示当前字段，显示当前位置与总数，并允许用户前后查看且不丢失已输入值

#### Scenario: 跳过可选字段
- **WHEN** 当前字段不是必填且用户选择跳过
- **THEN** 系统清空当前字段并进入下一字段；若已是最后字段，则验证并提交整个请求

#### Scenario: 必填字段不能跳过
- **WHEN** 当前字段是必填且没有有效值
- **THEN** 跳过与继续操作不可完成，并在用户尝试前进或提交时显示当前字段的安全校验摘要

#### Scenario: 单选自动前进
- **WHEN** 用户在非末页选择一个单选项
- **THEN** Card 在短暂选择反馈后自动进入下一字段，所选 option id 保持不变

#### Scenario: 多选数字文本等待确认
- **WHEN** 当前字段是多选、数字或文本
- **THEN** Card 保留当前输入并等待用户点击继续或提交

#### Scenario: 取消整个请求
- **WHEN** 请求允许取消且用户点击关闭按钮或按 Escape
- **THEN** Card 取消整个请求并关闭，不提交部分字段或默认值

#### Scenario: 请求不允许取消
- **WHEN** 请求的 `allow_cancel` 为 false
- **THEN** Card 不显示关闭入口，Escape 不取消请求，用户必须完成有效输入或由外部请求生命周期终止

```
