# Comet Design Handoff

- Change: unify-input-focus-material
- Phase: design
- Mode: compact
- Context hash: f8dfdf7a32d7a082bd944d696e7ad9033317a702df658e956cff7e44238762db

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/unify-input-focus-material/proposal.md

- Source: openspec/changes/unify-input-focus-material/proposal.md
- Lines: 1-27
- SHA256: aaa65e096c7cbbac54927b7e307a5537c06245652352680768a97d69b97fd911

```md
## Why

应用内多处 `TextBox`、`ComboBox` 和 `NumericUpDown` 在聚焦或选中后会被 Fluent 模板内部的不透明背景覆盖，破坏透明主题的材质连续性。模型搜索框的局部修复已经证明仅设置控件外层 `Background` 不足，需要建立应用级、可复用且保留局部语义的输入材质契约。

## What Changes

- 统一标准输入控件在普通、悬停、聚焦和选中状态下的背景材质，状态变化只强化边框，不切换为不透明白底。
- 应用级模板覆盖绑定回控件自身 `Background`，保留各页面已有的局部背景语义。
- 提供 `input-transparent` 与 `input-embedded` 两个复用 class，分别用于完全透出父级材质和嵌入组合容器的输入控件。
- 分批迁移设置、提供商、网关、活动、控制台、历史会话和模型搜索；AI 助手底部复合输入框最后迁移并单独验证全部既有交互。
- 用真实聚焦控件检查模板视觉树最终画刷，避免属性测试与实机渲染不一致。

## Capabilities

### New Capabilities

- `app-input-material`: 定义应用内标准输入控件在透明主题下的材质、聚焦反馈、复用 class 和复合输入框兼容要求。

### Modified Capabilities

无。

## Impact

- 主要影响 `LoomX/App.axaml`、7 个包含输入控件的视图及对应 Avalonia UI 样式测试。
- 不改变 ViewModel、数据库、公开 API、配置格式、模型协议或业务逻辑。
- AI 助手底部输入框只在自动增高、滚动和键盘行为全部保持时采用统一样式。

```

## openspec/changes/unify-input-focus-material/design.md

- Source: openspec/changes/unify-input-focus-material/design.md
- Lines: 1-42
- SHA256: a2b85fcdb865a559b00d16be882941549d28448710fe212e42cee8c2f8087ae4

```md
## Context

见 `proposal.md`。应用级样式目前只设置控件外层 `Background` 与聚焦边框，Avalonia Fluent 模板仍可能在状态切换时为 `PART_BorderElement`、内容呈现器或子输入部件应用主题背景。模型搜索框已有局部模板覆盖，可以作为真实渲染根因和测试方式的基线。

## Goals / Non-Goals

**Goals:**

- 用应用级样式统一标准输入控件的模板最终背景，同时保留控件自身的局部画刷语义。
- 为透明输入和嵌入式输入提供最少数量的复用 class。
- 用真实窗口、真实焦点与视觉树断言覆盖 `TextBox`、`ComboBox` 和 `NumericUpDown`。
- 将 AI 助手底部复合输入框作为最后一个可独立回退的迁移步骤。

**Non-Goals:**

- 不重写 Fluent 控件模板，不引入第三方主题库。
- 不统一按钮、列表选择项或其他非输入控件的状态材质。
- 不调整页面布局、尺寸、数据绑定或业务行为。

## Decisions

1. 在 `App.axaml` 中保留现有控件级默认背景和聚焦边框，并增加针对 Fluent 模板部件的应用级选择器。模板部件背景绑定回所属控件的 `Background`，而不是硬编码 `Transparent` 或固定资源，以保留 `SurfaceSubtleBrush`、`SurfaceBrush` 等局部语义。
2. 新增 `input-transparent` 与 `input-embedded` 两个 class。前者明确让控件和模板完全透明；后者用于外层容器承担背景和边框的组合输入区，并清除内部输入自身边框。不会为每个页面或控件类型继续扩展 class。
3. 先覆盖普通输入控件并移除模型搜索框已被全局规则替代的重复模板样式，再按语义给提供商 Header、网关名称、历史重命名等特殊输入标注 class。页面已有显式背景在不冲突时保持不动。
4. 测试创建真实控件并显示宿主窗口，依次聚焦 `TextBox`、`ComboBox` 和 `NumericUpDown`，检查模板视觉树中可见背景是否等于控件背景或透明。仅检查外层属性不足以作为通过证据。
5. AI 助手底部输入框最后处理。迁移前后分别验证自动增高、32% 上限、内部滚动条、Enter、Shift+Enter、Watermark、光标、选区以及外层 `input-card.focused` 描边；任一回归时只撤回该控件的 class 或模板适配。

## Risks / Trade-offs

- [Risk] 不同控件的 Fluent 模板部件名称和层级不同。 → 先用失败测试枚举实际视觉树，再只覆盖已确认的部件。
- [Risk] 全局选择器可能改变局部特殊输入的语义。 → 模板背景绑定控件自身 `Background`，并为透明与嵌入场景提供显式 class。
- [Risk] ComboBox 的选中状态与 Popup 列表状态混淆。 → 仅处理选择框模板背景，不修改下拉列表项的选中样式。
- [Risk] NumericUpDown 内含子 TextBox，可能产生双层背景或边框。 → 测试最终视觉树并使用嵌入语义约束内部输入。
- [Risk] 复合输入框迁移影响输入行为。 → 作为最后独立任务，失败时局部回退，不阻塞其他页面统一。

## Migration Plan

1. 增加模板级失败测试并记录当前不透明部件。
2. 实现应用级模板覆盖与两个语义 class，使标准控件测试转绿。
3. 迁移普通页面和特殊输入，运行页面级样式回归。
4. 最后迁移并验证助手底部复合输入框；若不满足全部契约则撤回该步。
5. 完成完整测试、Release 构建、桌面实机验证和时间命名的 exe 发布包。

```

## openspec/changes/unify-input-focus-material/tasks.md

- Source: openspec/changes/unify-input-focus-material/tasks.md
- Lines: 1-18
- SHA256: 9d4363b6c42787afc45c4442be46b909ac0615b6ba7ec9e7f04ea8070a35a6a6

```md
## 1. 模板契约测试

- [ ] 1.1 为 `TextBox`、`ComboBox` 和 `NumericUpDown` 增加真实聚焦模板背景测试，并确认当前实现因不透明模板部件而失败。
- [ ] 1.2 增加 `input-transparent`、`input-embedded` 语义和局部背景保留测试，并确认测试能区分固定画刷覆盖与控件自身背景绑定。

## 2. 全局输入材质

- [ ] 2.1 在 `App.axaml` 实现应用级模板背景覆盖和两个复用 class，并确认第 1 组定向测试全部通过。
- [ ] 2.2 迁移设置、提供商、网关、活动、控制台、历史会话及模型搜索中的普通或特殊输入，移除被全局规则替代的重复局部样式，并运行相关视图测试。

## 3. 助手复合输入框

- [ ] 3.1 最后迁移 AI 助手底部复合输入框，验证自动增高、32% 上限、内部滚动、Enter、Shift+Enter、Watermark、光标、选区和外层聚焦描边；任一回归则只撤回该迁移。

## 4. 验证与发布

- [ ] 4.1 运行完整测试、Release 构建、OpenSpec strict validate 和 `git diff --check`，确认无新增失败。
- [ ] 4.2 通过后台桌面实机验证普通输入与复合输入的透明聚焦效果，并将新的 `win-x64` exe 发布包输出到带可读时间的 `outputs/` 目录。

```

## openspec/changes/unify-input-focus-material/specs/app-input-material/spec.md

- Source: openspec/changes/unify-input-focus-material/specs/app-input-material/spec.md
- Lines: 1-49
- SHA256: 6309f0e0e620c588d98701d76385dc7c2dacab43418917b82d75ec3824032672

```md
## Purpose

确保应用内标准输入控件在透明主题下保持连续、可辨识且一致的材质表现，同时不破坏嵌入式和复合输入场景的既有交互。

## ADDED Requirements

### Requirement: 输入控件聚焦时保持既有背景材质
系统 SHALL 让标准 `TextBox`、`ComboBox` 和 `NumericUpDown` 在普通、悬停、聚焦或选中状态下沿用控件当前背景材质，并仅通过边框等非填充反馈表达聚焦状态。

#### Scenario: 标准输入框获得焦点
- **WHEN** 用户在透明主题下聚焦标准输入控件
- **THEN** 控件模板不得绘制额外的不透明背景
- **AND** 聚焦边框仍清晰可见

#### Scenario: 控件具有局部背景语义
- **WHEN** 页面为输入控件显式设置了背景画刷
- **THEN** 聚焦后的模板背景应继续使用该控件自身背景
- **AND** 应用级样式不得用单一固定画刷覆盖局部语义

### Requirement: 输入材质语义可复用
系统 SHALL 提供统一语义，使完全透明输入和嵌入组合容器的输入能够复用一致的普通、悬停与聚焦材质行为。

#### Scenario: 完全透明输入
- **WHEN** 输入控件标记为完全透明语义
- **THEN** 控件及其模板背景在各交互状态下均保持透明

#### Scenario: 嵌入式输入
- **WHEN** 输入控件嵌入由外层容器负责边框和背景的组合区域
- **THEN** 内部输入不得绘制额外背景或外框
- **AND** 外层容器仍负责整体聚焦反馈

### Requirement: 助手复合输入框保持全部既有行为
系统 SHALL 仅在 AI 助手底部复合输入框复用统一材质后仍保持全部既有布局、滚动和键盘行为时采用该样式。

#### Scenario: 多行输入增长到上限
- **WHEN** 用户在助手底部输入多行文本
- **THEN** 输入框继续自动增高至应用客户区高度的 32%
- **AND** 达到上限后显示内部纵向滚动条

#### Scenario: 发送与换行快捷键
- **WHEN** 用户按下 Enter
- **THEN** 输入框继续发送消息
- **WHEN** 用户按下 Shift+Enter
- **THEN** 输入框继续插入换行

#### Scenario: 复合输入框迁移出现回归
- **WHEN** 自动化或实机验证发现复合输入框的布局、Watermark、光标、选区、滚动或键盘行为回归
- **THEN** 本次变更不得保留该复合输入框的统一样式迁移
- **AND** 其他标准输入控件的统一材质改造仍可独立保留

```
