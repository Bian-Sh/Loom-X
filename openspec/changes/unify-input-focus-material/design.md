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
2. 新增 `input-transparent` 与 `input-embedded` 两个 class。前者明确让控件和模板完全透明；后者清除嵌入输入的背景，但边框厚度继续由页面本地声明，以保留 Provider Header 分隔线、助手输入零边框等不同几何语义。不会为每个页面或控件类型继续扩展 class。
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

## Review Strategy

本 change 使用主会话直接实现与验证，`review_mode` 设为 `off`；原因是改动集中在 Avalonia 样式和对应真实控件测试，用户未要求子代理审查，且助手复合输入框保留独立实机验收门槛。
