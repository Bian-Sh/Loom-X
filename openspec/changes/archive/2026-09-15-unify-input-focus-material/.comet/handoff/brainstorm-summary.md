# Brainstorm Summary

- Change: unify-input-focus-material
- Date: 2026-09-14

## 确认的技术方案

在 `App.axaml` 增加应用级输入模板覆盖，让 `TextBox`、`ComboBox` 和 `NumericUpDown` 的内部模板背景沿用控件自身 `Background`，聚焦时只强化边框。提供 `input-transparent` 与 `input-embedded` 两个复用 class，先迁移普通输入与特殊嵌入输入，AI 助手底部复合输入框最后处理。

## 关键取舍与风险

不重写完整 Fluent 模板，不硬编码统一透明画刷，避免丢失页面局部材质语义。ComboBox 下拉列表项不在本次范围。NumericUpDown 的子 TextBox 与助手复合输入框均通过真实视觉树和交互测试控制风险；复合输入框出现任一回归时只撤回该控件迁移。

## 测试策略

用真实 Window 承载并聚焦三类标准输入，检查模板视觉树最终画刷；补充语义 class 和局部背景保留测试。最后验证助手输入框自动增高、32% 上限、内部滚动、Enter、Shift+Enter、Watermark、光标、选区与外层聚焦描边，并执行完整测试、Release 构建和桌面实机验证。

## Spec Patch

已新增 `app-input-material` capability，定义聚焦材质、复用语义和助手复合输入兼容要求。
