# Comet Design Handoff

- Change: refresh-photon-blue-theme
- Phase: design
- Mode: compact
- Context hash: b450e94c6488773d3947a5bc32c8d2c6830e2d75a12531948561139bcd6c9cba

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/refresh-photon-blue-theme/proposal.md

- Source: openspec/changes/refresh-photon-blue-theme/proposal.md
- Lines: 1-30
- SHA256: 3cf60e97c18576eda5be0b03f7e097b01bbdc4bed3ffda4bfa725ea415255360

```md
# Proposal

## Why

Loom-X 当前以青绿色 `#287C83` / `#53A7AD` 承担主强调色，并让侧栏普通图标等非重点内容长期使用同一色相，导致视觉层级扁平、品牌辨识度偏弱，也与现有蓝紫应用图标缺少统一的科技感。现在需要将颜色职责重新分层，而不是把整套界面简单替换成另一种单色。

## What Changes

- 将桌面端主强调色更新为 Photon Blue：浅色主题使用 `#2563EB`，深色主题使用 `#60A5FA`。
- 引入少量电光青 `#38BDF8` 作为辅助高亮，仅用于受控的品牌或进度装饰，不替代成功、警告、危险等语义状态色。
- 重新校准浅色与深色主题的文字、边框、表面和软强调画刷，使普通内容保持冷中性层级，避免界面再次被单一品牌色覆盖。
- 将侧栏未选中图标改为次要文字色，仅让当前选中项使用主强调色；保留现有悬停、选中轮廓和透明外观行为。
- 保留现有蓝紫应用图标，不在本次改造中重绘品牌资产。
- 增加主题令牌与侧栏图标语义的契约测试，并通过深浅主题、透明开关和真实桌面窗口截图完成视觉验收。

## Capabilities

### New Capabilities

- `desktop-visual-theme`: 定义 Loom-X 桌面端 Photon Blue 品牌色、冷中性色层级、图标强调规则以及深浅/透明主题下的一致视觉行为。

### Modified Capabilities

- 无。

## Impact

- 主要影响 `LoomX/Styles/VisualTokens.axaml`、`LoomX/MainWindow.axaml` 及对应 `LoomX.Tests/Views` 契约测试。
- 插件声明式 UI、按钮、输入控件和页面级强调状态继续通过现有动态资源消费主题，不修改插件协议、公共 API、数据库 schema 或配置结构。
- 透明外观协调器继续只调整既有 `SolidColorBrush` 的 Alpha；本次不会把受其管理的资源替换为不兼容的画刷类型。

```

## openspec/changes/refresh-photon-blue-theme/design.md

- Source: openspec/changes/refresh-photon-blue-theme/design.md
- Lines: 1-99
- SHA256: 74e22d17f242c0150cc27cd5a21bf937f58e7abbc44df4acebd6d281e767dbdb

[TRUNCATED]

```md
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

```

Full source: openspec/changes/refresh-photon-blue-theme/design.md

## openspec/changes/refresh-photon-blue-theme/tasks.md

- Source: openspec/changes/refresh-photon-blue-theme/tasks.md
- Lines: 1-26
- SHA256: b14bd2e497731c9530ded020c3268c3cb87824c8ac404d780a505eea8b3745b1

```md
# Tasks

## 1. Photon Blue 主题令牌

- [ ] 1.1 在现有 View 契约测试中新增 Light/Dark 主题令牌断言，覆盖主强调色、辅助高亮、关键冷中性文字/表面色及受透明协调器管理资源仍为 `SolidColorBrush`；运行定向测试并确认它因旧青绿色令牌而失败。
- [ ] 1.2 最小修改 `LoomX/Styles/VisualTokens.axaml` 的 Light/Dark 现有资源，使主强调色分别为 `#2563EB` 与 `#60A5FA`、辅助高亮为 `#38BDF8`，并完成冷中性色校准；运行新增令牌测试以及窗口外观、设置页和插件 UI 相关测试并确认通过。

## 2. 侧栏图标状态语义

- [ ] 2.1 在 `MainWindowNavigationContractTests` 中新增侧栏图标契约，要求 `Path.nav-icon` 默认使用 `TextSecondaryBrush`、当前导航项内图标使用 `AccentBrush`；运行定向测试并确认它因现有无条件 Accent 规则而失败。
- [ ] 2.2 最小修改 `LoomX/MainWindow.axaml` 的导航图标样式并审查直接 `AccentBrush` 引用，只修复无强调语义的默认内容；运行导航契约测试并确认未选中与选中状态规则通过。

## 3. 集成验证

- [ ] 3.1 运行 `LoomX.Tests` 中主题、导航、设置、插件呈现和 Provider 视图相关测试，确认 Photon Blue 资源未破坏主题切换、插件语义映射或现有控件契约。
- [ ] 3.2 构建完整解决方案并检查 Git diff，确认没有修改 Success/Warning/Danger 语义、应用图标、数据库/API/插件协议或任务范围外文件。

## 4. 打包与桌面视觉验收

- [ ] 4.1 使用项目现有发布方式重新打包桌面应用到 `outputs/<可读时间>/`，并验证发布目录包含可启动的 Loom-X 可执行文件及所需插件负载。
- [ ] 4.2 使用 `Start-Process -FilePath <绝对 exe 路径>` 隐藏启动发布包并校验进程路径；通过 CUA 仅截取应用窗口，验收浅色、深色和至少一种透明开关组合下的文字层级、普通/选中侧栏图标、主强调色与辅助高亮，且不把透明背景混色当作精确色值证据。

## Workflow follow-up

- 在 Comet Verify 阶段记录自动化测试、构建、打包与 GUI 验收结果。
- 满足项目评审要求后归档该 change，并验证归档结果。

```

## openspec/changes/refresh-photon-blue-theme/.openspec.yaml

- Source: openspec/changes/refresh-photon-blue-theme/.openspec.yaml
- Lines: 1-2
- SHA256: 3ae9e70fc0101b2a700cedadfa7b02caa5799fdfd1db033999469c527448c6b2

```md
schema: spec-driven
created: 2026-10-11

```

## openspec/changes/refresh-photon-blue-theme/specs/desktop-visual-theme/spec.md

- Source: openspec/changes/refresh-photon-blue-theme/specs/desktop-visual-theme/spec.md
- Lines: 1-76
- SHA256: 188f395488aa882cba1e6d8252f5e7cd96aa1ceec3bcc74198137aabc6f5a416

```md
# Spec Delta

## Purpose

定义 Loom-X 桌面端品牌配色与内容层级，使深浅主题、普通图标、选中状态和透明材质在统一的 Photon Blue 视觉体系下保持清晰、克制且可验证。

## ADDED Requirements

### Requirement: 桌面主题使用 Photon Blue 品牌强调色
桌面端 SHALL 在浅色主题使用 `#2563EB` 作为主强调色，在深色主题使用 `#60A5FA` 作为主强调色，并让按钮、聚焦边框、选中轮廓和其他主强调语义随当前主题切换。

#### Scenario: 应用使用浅色主题
- **WHEN** 用户将应用主题切换为浅色
- **THEN** 主强调语义显示为 `#2563EB`
- **AND** 主强调内容在浅色表面上保持清晰可辨

#### Scenario: 应用使用深色主题
- **WHEN** 用户将应用主题切换为深色
- **THEN** 主强调语义显示为 `#60A5FA`
- **AND** 主强调内容在深色表面上保持清晰可辨

### Requirement: 默认文字和普通图标保持冷中性色层级
桌面端 MUST 让正文、次要文字、弱化文字和普通图标使用对应的冷中性色角色，不得让未选中或无强调语义的内容默认使用主强调色。

#### Scenario: 侧栏项目未选中
- **WHEN** 侧栏导航项目不是当前页面
- **THEN** 该项目的图标与文字使用次要内容色
- **AND** 不显示 Photon Blue 主强调色

#### Scenario: 侧栏项目成为当前页面
- **WHEN** 侧栏导航项目成为当前页面
- **THEN** 该项目图标使用当前主题的 Photon Blue 主强调色
- **AND** 文字、软背景与轮廓共同表达选中状态

#### Scenario: 普通图标按钮未处于强调状态
- **WHEN** 图标按钮没有成功、警告、危险或当前选中等语义
- **THEN** 图标使用默认或次要内容色

### Requirement: 辅助高亮不得取代语义状态色
桌面端 MAY 使用 `#38BDF8` 表达少量电光辅助高亮，但 MUST 保留成功、警告和危险状态各自独立的语义颜色，不得使用辅助高亮冒充这些状态。

#### Scenario: 展示辅助品牌高亮
- **WHEN** 界面展示受控的品牌装饰、活动进度末端或更新活动提示
- **THEN** 可以使用 `#38BDF8` 作为辅助高亮
- **AND** 同一普通内容区域不得因此整体染成辅助高亮色

#### Scenario: 展示业务状态
- **WHEN** 界面展示成功、警告或危险状态
- **THEN** 继续使用对应的语义状态色
- **AND** 不使用 Photon Blue 或电光青替代状态含义

### Requirement: Photon Blue 主题兼容透明与非透明外观
桌面端 SHALL 在透明外观开启、关闭以及模糊强度变化时保持主题层级、选中状态和文字可读性，并继续支持运行时调整受管理表面画刷的不透明度。

#### Scenario: 开启透明外观
- **WHEN** 用户开启透明外观并调整窗口不透明度或模糊强度
- **THEN** 窗口、玻璃、表面、软强调和弹窗资源继续响应透明度协调
- **AND** Photon Blue 选中状态与冷中性文字仍可辨识

#### Scenario: 关闭透明外观
- **WHEN** 用户关闭透明外观
- **THEN** 同一主题资源使用不透明回退显示
- **AND** 深浅主题的品牌色与内容层级不发生语义变化

### Requirement: 现有品牌资产与主题切换行为保持兼容
桌面端 MUST 保留现有蓝紫应用图标，并保持主题即时切换、插件语义色映射和现有控件交互行为不因本次配色更新而退化。

#### Scenario: 用户切换主题
- **WHEN** 用户在浅色、深色或跟随系统之间切换
- **THEN** 桌面端立即应用对应 Photon Blue 主题令牌
- **AND** 无需重启应用

#### Scenario: 插件使用宿主语义色
- **WHEN** 插件声明默认、强调、成功、警告或危险视觉角色
- **THEN** 宿主继续通过当前主题资源解析这些角色
- **AND** 插件协议与负载布局保持不变

```
