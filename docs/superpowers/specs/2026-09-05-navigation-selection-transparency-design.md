# 左侧导航选中态透明度统一设计

## 背景

Loom-x 的左侧导航通过 `ToggleButton.nav-button:checked` 使用共享的 `AccentSoftBrush` 表达当前页。窗口透明设置在运行时由 `WindowAppearanceCoordinator` 更新动态画刷，但该共享资源未被协调器处理，因此选中态始终不透明。

## 目标

- 选中态背景随全局透明度、磨砂程度同步变化。
- 关闭透明外观时，选中态背景为完全不透明。
- 保持现有的导航绑定、资源键、颜色和边框样式。

## 设计

`WindowAppearanceCoordinator` 继续作为共享画刷 Alpha 的唯一更新入口。在 `Apply` 中将 `AccentSoftBrush` 使用与 `NavigationHoverBrush` 相同的基线 Alpha 计算后更新；画刷 RGB 保持由 `VisualTokens.axaml` 定义的原始值。

该资源被左侧导航选中态和若干现有控件共用，因此所有消费者都能获得一致的透明外观，无需新增左侧导航专用资源或样式分支。透明外观关闭时，协调器沿用已有的计算规则将 Alpha 固定为 `255`。

## 验证

- 单元测试：透明开启时 `AccentSoftBrush` 的 Alpha 等于预期计算值，且随磨砂程度变化。
- 单元测试：透明关闭时 `AccentSoftBrush` 的 Alpha 为 `255`。
- 运行相关测试与 `dotnet build LoomX.slnx`。

## 非目标

- 不改动导航交互、页面切换或视觉色彩。
- 不为单一控件新增专用透明度配置。
- 不处理其他尚未报告的 UI 表面透明度问题。
