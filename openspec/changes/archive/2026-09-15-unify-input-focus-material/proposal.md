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
