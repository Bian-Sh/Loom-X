# Brainstorm Summary

- Change: refresh-photon-blue-theme
- Date: 2026-10-11
- Status: 已确认

## 确认的技术方案

- 沿用 `LoomX/Styles/VisualTokens.axaml` 现有 Light/Dark `ThemeDictionary` 与资源键，不新增第二套品牌色 API，也不引入通用渐变画刷。
- 主强调色固定为 Light `#2563EB`、Dark `#60A5FA`；`UpdateEntryActiveBrush` 固定为 `#38BDF8`，仅承担少量辅助高亮。
- Light 使用冷白、灰蓝表面与 `#18202C` / `#5E6B7A` / `#8995A5` 文字层级；Dark 使用石墨蓝黑表面与 `#F1F5FB` / `#9AA8BC` / `#6F7F92` 文字层级。
- 保留所有既有资源键和 `SolidColorBrush` 类型，使 `WindowAppearanceCoordinator` 继续只调整 Alpha；不修改 Success、Warning、Danger 资源。
- `Path.nav-icon` 默认 Stroke 改为 `TextSecondaryBrush`，并以 `Button.nav-button.active Path.nav-icon` 覆盖为 `AccentBrush`；现有 active、hover 与选择指示器行为保持不变。
- 仅审查直接 `AccentBrush` 引用，保留主按钮、焦点、推荐、拖拽、处理中和插件 Accent tone 等明确强调语义。

## 关键取舍与风险

- 不新增渐变，科技感依赖 Photon Blue、冷中性层级、电光青点缀及现有蓝紫应用图标，避免扩大模板和透明主题风险。
- Surface 与 Border 同步去除青绿色偏色，否则只换 Accent 会留下旧主题气质。
- 透明开启时截图颜色会受背景混色影响，精确色值由契约测试验证，GUI 只验证层级、辨识度和状态语义。
- 侧栏 active 选择器必须比默认 `Path.nav-icon` 更具体，避免默认中性色覆盖选中状态。

## 测试策略

- 先增加失败的主题令牌契约测试，覆盖 Light/Dark 精确核心色值、冷中性文字层级、辅助高亮及关键资源仍为 `SolidColorBrush`。
- 再增加失败的导航契约测试，覆盖默认侧栏图标与 active 图标的资源绑定。
- 生产修改后运行导航、窗口外观、设置、插件 UI、Provider 等相关测试并构建解决方案。
- 重新发布到带可读时间的 `outputs/` 目录，通过隐藏启动与 CUA 验收浅色、深色和透明组合。

## Spec Patch

无。现有 delta spec 已覆盖主强调色、默认/选中图标、辅助高亮、透明外观和插件兼容场景。
