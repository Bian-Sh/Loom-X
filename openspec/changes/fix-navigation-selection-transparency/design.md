# 修复方案

在 `WindowAppearanceCoordinator.Apply` 中将 `AccentSoftBrush` 纳入现有 `SetBrushAlpha` 调用，沿用 `NavigationHoverBrush` 的基线 Alpha、透明度因子和磨砂色调计算规则。该更新作用于共享动态资源，因此左侧导航选中态及其他使用该资源的选中表面会同步当前透明外观。

不新增资源键、专用样式或页面级分支。透明外观关闭时，现有画刷计算保留 `Alpha=255` 的回退行为。
