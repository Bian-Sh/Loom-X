# 修复方案

在 `WindowAppearanceCoordinator.Apply` 中将 `AccentSoftBrush` 纳入现有 `SetBrushAlpha` 调用，沿用 `NavigationHoverBrush` 的基线 Alpha、透明度因子和磨砂色调计算规则。该更新作用于共享动态资源，因此左侧导航选中态及其他使用该资源的选中表面会同步当前透明外观。

不新增资源键、专用样式或页面级分支。透明外观关闭时，现有画刷计算保留 `Alpha=255` 的回退行为。

## Runtime NodeGraph 透明画布

Overview 的 Runtime NodeGraph 保留覆盖完整控件区域的透明绘制层，以维持空白区域的滚轮缩放和拖拽平移命中。外框使用共享 `BorderBrush`，不再使用深色 Graph 边框。

节点和 Provider 分组直接复用现有 Surface、Border、Text 与 Accent 资源，不对半透明边框资源二次降低 Alpha。Endpoint 使用 `AccentSoftBrush`，Combo、Model 和 Provider 分别使用现有 Surface 层级，以保证透明背景下仍有清晰的层级和文字对比度。
