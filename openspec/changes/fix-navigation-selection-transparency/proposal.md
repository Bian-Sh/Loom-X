# 左侧导航选中态透明度修复

## 问题

启用窗口透明外观后，左侧导航已选中的条目仍使用完全不透明的 `AccentSoftBrush`，与侧栏、悬停态和用户设置的透明度不一致。

## 根因

`WindowAppearanceCoordinator` 仅在应用外观时更新了 `NavigationHoverBrush` 等表面资源，遗漏了同样用于导航选中态的 `AccentSoftBrush`。该资源保留了令牌定义中的 Alpha `255`。

## 修复目标

- `AccentSoftBrush` 与其他共享表面资源一起响应透明度和磨砂程度。
- 透明外观关闭时，选中态保持完全不透明。
- 不改变导航命令、选中逻辑、RGB 配色或现有资源键。
