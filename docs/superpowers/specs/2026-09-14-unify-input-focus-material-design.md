---
comet_change: unify-input-focus-material
role: technical-design
canonical_spec: openspec
---

# 应用输入聚焦材质统一设计

## 背景

应用全局样式已经为 `TextBox`、`ComboBox` 和 `NumericUpDown` 设置半透明背景与聚焦边框，但 Avalonia Fluent 模板在 focus、selected 或 pointerover 状态仍可能给内部部件填入主题画刷。模型搜索框的局部修复证明，外层 `Background` 属性与用户最终看到的模板背景并不等价。

## 样式契约

1. 标准输入控件继续由自身 `Background` 决定材质，普通、悬停、聚焦和选中状态不得切换为另一块不透明填充。
2. 应用级模板部件样式绑定回所属控件的 `Background`，不得硬编码 `Transparent` 或某个 Surface token。
3. 聚焦反馈由 `AccentBrush` 边框承担；透明背景不等于无聚焦反馈。
4. `input-transparent` 用于父级表面完全承接材质的输入；`input-embedded` 用于组合输入中清除内部背景。边框厚度继续由页面本地声明，以保留方向性分隔线和外层聚焦轮廓。

## 控件覆盖

### TextBox

覆盖 Fluent 模板中实际绘制背景的 `PART_BorderElement`。默认、pointerover 与 focus 都读取 TextBox 自身背景。`input-transparent` 和 `input-embedded` 强制背景透明；嵌入控件的边框几何由页面本地声明。

### ComboBox

只处理选择框本体的模板背景，不修改 Popup 内 `ComboBoxItem` 的 hover 或 selected 语义。测试以关闭状态的选择框为准，确保局部背景画刷在 focus 后不变。

### NumericUpDown

同时检查外层控件和模板内子 TextBox，避免双层背景。外层背景沿用控件自身画刷，内部编辑器视为嵌入内容，不再注入第二层不透明填充。

## 页面迁移

普通输入依赖全局规则即可，不批量写重复 class。只为语义明确的特殊输入标注：完全透出 Popup 的模型搜索、网关内联名称等使用 `input-transparent`；由组合容器承担边框的 Provider Header 键值输入等使用 `input-embedded`。历史会话重命名保留显式 `SurfaceBrush`，验证聚焦后仍沿用该画刷。

## 助手复合输入框

底部输入框位于 `input-card` 中，外层容器负责背景、圆角和 `.focused` 描边，内部 TextBox 还承担自动增高、多行编辑和滚动。该控件最后标记为 `input-embedded`，且必须满足以下验收后才能保留：

- `AcceptsReturn=True`、自动换行和 32% 客户区高度上限不变。
- 达到上限后内部纵向滚动条可用，横向滚动保持禁用。
- Enter 发送、Shift+Enter 换行不变。
- Watermark、光标、文本选区清晰可见。
- 外层 `input-card.focused` 仍显示 `AccentBorderSoftBrush` 描边。

若任何一项失败，只移除该 TextBox 的统一 class 或局部适配，不撤销其他页面已经通过的全局材质规则。

## 验证

自动化测试显示真实宿主窗口并获取键盘焦点，读取视觉树中 `Border`、`ContentPresenter` 和嵌套 TextBox 的最终画刷。桌面验证在透明主题下抽查设置、提供商、网关、活动、控制台、历史重命名、模型搜索与助手输入，截图只作为视觉辅助，不单独用于推断透明主题的真实色值。
