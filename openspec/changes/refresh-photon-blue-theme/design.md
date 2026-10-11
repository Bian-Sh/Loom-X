---
comet_change: refresh-photon-blue-theme
role: technical-design
canonical_spec: openspec
---

# Design

## Context

参见 `proposal.md` 的动机与范围。桌面端颜色集中定义在 `LoomX/Styles/VisualTokens.axaml` 的 Light/Dark `ThemeDictionary` 中，并通过 `DynamicResource` 传播到应用控件、插件声明式 UI 和页面局部样式。`WindowAppearanceCoordinator` 会在运行时调整一组 `SolidColorBrush` 的 Alpha，因此这些受管理资源必须继续保持相同资源键和画刷类型。

当前普通 `icon-glyph` 已使用 `TextSecondaryBrush`，但 `MainWindow.axaml` 的 `Path.nav-icon` 无条件使用 `AccentBrush`，是侧栏未选中图标长期呈现青绿色的直接原因。现有蓝紫应用图标已经与 Photon Blue 方向一致，无需修改位图资产。

## Goals / Non-Goals

**Goals:**

- 通过集中主题令牌完成浅色与深色 Photon Blue 配色迁移，避免页面级批量硬编码替换。
- 让默认文字、普通图标、边框和表面使用冷中性色，把品牌蓝收敛到主行动、聚焦、选中和明确强调状态。
- 保持透明外观协调、主题即时切换、插件语义色和现有语义状态色兼容。
- 以契约测试守住关键色值、资源类型和侧栏图标状态规则，并以真实桌面窗口完成视觉验收。

**Non-Goals:**

- 不重绘应用图标，不调整字体、间距、圆角、布局或动画体系。
- 不新增可配置的用户自定义主题、调色板编辑器或运行时品牌色设置。
- 不修改 Success、Warning、Danger 的语义定义，不把电光青用作业务状态色。
- 不为每个页面单独清理所有合理的 Accent 用法；只修复无条件染色的默认内容，并保留明确的强调语义。

## Decisions

### 1. 复用现有主题令牌，不建立第二套品牌色系统

直接更新 Light/Dark ThemeDictionary 中现有的 Surface、Border、Text、Accent 与 Update Entry 资源。`AccentBrush` 在浅色主题使用 `#2563EB`，在深色主题使用 `#60A5FA`；`UpdateEntryActiveBrush` 使用 `#38BDF8` 承担少量辅助电光高亮。

不新增通用渐变画刷或新的 Accent API。主按钮、焦点边框、插件 Accent tone 和页面强调继续复用 `AccentBrush`，可避免资源分叉，也保证 `WindowAppearanceCoordinator` 对 `SolidColorBrush` 的处理继续有效。蓝紫渐变主要由现有应用图标提供。

**替代方案：**新增 `AccentGradientBrush` 并把主按钮、导航和品牌区域全部改为渐变。该方案科技感更直接，但会扩大模板改动、增加文字对比和透明主题风险，也容易再次形成“满屏品牌色”，因此不采用。

### 2. 深浅主题分别校准，不使用同一 RGB 的机械明暗变体

浅色主题采用更深的 `#2563EB` 保证白色或浅色表面上的可读性；深色主题采用更亮的 `#60A5FA` 保证石墨蓝黑表面上的辨识度。Surface、Border、Text 令牌同步改为去青绿色倾向的冷白、石墨蓝黑和灰蓝层级。

`AccentSoftBrush` 与 `AccentBorderSoftBrush` 分别针对主题设置，而不是只调整 Alpha。透明外观协调器仍可覆盖 Alpha，但保留每个主题的 RGB 基色。

**替代方案：**只替换 `AccentBrush` 两个值。该方案改动最小，但原有青绿色表面、边框与文字层级仍会残留，无法实现用户确认的完整 Photon Blue 气质，因此不采用。

### 3. 侧栏图标使用状态选择器表达语义

`Path.nav-icon` 默认 Stroke 改为 `TextSecondaryBrush`；新增 active 状态选择器，使当前导航项内的图标使用 `AccentBrush`。悬停仍通过现有 `NavigationHoverBrush` 和文字前景变化反馈，不让所有未选中图标长期占用品牌色。

其他 `AccentBrush` 直接用法按语义审查：拖拽中、推荐、处理中、插件 Accent tone 等明确强调状态保持不变。普通 `icon-glyph` 已使用中性色，无需全局重构。

**替代方案：**把所有 XAML 中的 `AccentBrush` 批量替换为 `TextSecondaryBrush`。该方案会破坏合法强调状态和插件语义，风险高且不可维护，因此不采用。

### 4. 测试先约束语义，再修改生产资源

先在现有 View 契约测试中增加失败断言：检查 Light/Dark Accent 精确值、冷中性色关键令牌、辅助高亮色，以及侧栏默认/active 图标选择器。随后做最小资源和样式修改使测试通过。

真实视觉验收覆盖浅色、深色，以及至少一种透明开启/关闭组合。截图只截应用窗口；透明主题截图的背景混色不作为令牌色值的精确证据，色值契约由自动化测试负责。

## Implementation Contracts

### 核心令牌

| 角色 | Light | Dark |
| --- | --- | --- |
| `AccentBrush` | `#2563EB` | `#60A5FA` |
| `AccentBorderSoftBrush` | `#662563EB` | `#6660A5FA` |
| `UpdateEntryActiveBrush` | `#38BDF8` | `#38BDF8` |
| `TextPrimaryBrush` | `#18202C` | `#F1F5FB` |
| `TextSecondaryBrush` | `#5E6B7A` | `#9AA8BC` |
| `TextTertiaryBrush` | `#8995A5` | `#6F7F92` |

`AccentSoftBrush` 在 Light 使用冷白蓝 `#EAF1FF`；Dark 使用低饱和石墨蓝 RGB，并继续保留现有可调 Alpha。Window、Glass、Surface、Navigation、Dialog、Popup、Border 与 Acrylic Tint/Fallback 资源同步迁移到冷白灰蓝或石墨蓝黑色域，但保留原资源键、画刷类型和 Alpha 语义。Success、Warning、Danger 资源值不变。

### 导航选择器

默认规则为 `Path.nav-icon` → `TextSecondaryBrush`；激活规则为 `Button.nav-button.active Path.nav-icon` → `AccentBrush`。激活规则必须具有更高选择器特异性，并且不得改变现有 `Classes.active` 绑定、悬停背景、选择轮廓或共享选择指示器。

### 回归边界

直接 `AccentBrush` 引用只做语义审查，不批量替换。主按钮、聚焦边框、选择轮廓、推荐、拖拽、处理中和插件 Accent tone 属于保留范围；默认文字和普通图标不得新增 Accent 绑定。

## Risks / Trade-offs

- [风险] 深色 `AccentSoftBrush` 经透明协调器提高 Alpha 后可能显得过重 → 使用低饱和石墨蓝 RGB，并在真实透明窗口中检查导航选中态。
- [风险] Accent 色值变化会同步影响插件 Accent tone 和大量聚焦边框 → 保留语义映射不变，运行现有插件 UI、设置页和窗口外观相关测试。
- [风险] 只审查侧栏图标可能遗漏其他无语义的 Accent 直用 → 使用搜索清单逐项确认直接引用；只对确认是默认内容的引用做最小修正。
- [权衡] 不新增通用渐变资源，界面动态感主要来自单色层级与现有品牌图标 → 降低模板和对比度风险，后续如有明确品牌动效需求再单独设计。

## Migration Plan

1. 先增加主题令牌与侧栏图标契约测试并确认按预期失败。
2. 更新 Light/Dark 视觉令牌和侧栏图标状态样式，使定向测试通过。
3. 运行窗口外观、设置主题、插件 UI 及相关 View 契约测试，再构建完整解决方案。
4. 启动桌面端，分别检查浅色与深色主题及透明开关，保存只包含应用窗口的验收截图。
5. 若视觉验收发现层级过重，优先回调 Surface/Soft/Border RGB，不改变已确认的主强调色和图标语义；必要时可通过回退本次令牌与选择器改动恢复旧主题。
