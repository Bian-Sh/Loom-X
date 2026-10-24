# Design

## Context

透明外观的现状链路（master @ ab8ca28）：

- `WindowAppearanceCoordinator`（`LoomX/Services/WindowAppearanceCoordinator.cs`）由主窗口持有（`MainWindow.axaml.cs:80`），`Apply(enabled, opacity, blur, algorithm)` 替换**应用级**画刷资源（`WindowBackgroundBrush`/`GlassBrush` 等），`ApplyTheme` 另行切换 `RequestedThemeVariant` 并 `RefreshThemeResources`。
- `TransparencyLevelHint` 只设在主窗口（`MainWindow.axaml.cs:485`）；`SplashWindow` 与 `GlassDialogWindow` 仅通过 `{DynamicResource WindowBackgroundBrush}` 被动跟随画刷变化。
- 启动顺序：闪屏先显示 → 编排器 Database 阶段读库 → Shell 阶段 `ApplyLoadedConfiguration` 才调用 `applyTheme`/`applyAppearance`。因此闪屏第一帧必然使用默认画刷（不透明），待 Shell 阶段应用级资源被替换后突变为透明材质——2026-10-24 验收截屏第 29→31 帧记录了该跳变。
- 外观设置（`TransparencyEnabled/TransparencyOpacity/BlurAmount/Theme`）只存在于配置库，启动时在 Database 阶段才可读。

约束：

- `reduce-startup-db-reload`（并行 change）要求启动流程内配置库恰好初始化/读取一次——外观初值 MUST NOT 通过提前开库获取（鸡生蛋：闪屏显示早于读库）。
- AGENTS.md「主题相关」：透明主题下截图颜色非真实配色，验收时以窗口实际材质链路为准，不依赖截图颜色判断。
- 透明算法已固定为 Acrylic（`ApplyAppearance` 内 algorithm 被覆写，参数仅兼容旧数据）。

## Goals / Non-Goals

**Goals:**
- 透明是应用级属性：所有顶层窗口（现有三种 + 未来窗口）的材质由唯一协调点管理，开启即全透明、关闭即全不透明。
- 闪屏第一帧即最终材质：启动流程内任何窗口不发生材质/主题突变（正常路径零切换）。
- 外观初值获取零数据库读取。

**Non-Goals:**
- 不改变设置页交互、参数取值范围与 Acrylic 算法固定现状。
- 不做插件窗口（`IPluginUI` 宿主内容）的材质改造——插件 UI 遵循宿主容器材质，不在本 change 范围。
- 不处理多显示器/DWM 特例调优。

## Decisions

1. **协调器升级为应用级单例**：`WindowAppearanceCoordinator` 增加多窗口注册/注销（弱引用，防泄漏）与「当前外观快照」，新窗口注册时立即应用当前快照（幂等）。主窗口的 `ApplyTheme`/`ApplyAppearance` 保留公开签名（兼容现有调用与测试契约），内部委托协调器并作用于全部注册窗口。备选「每窗口各自监听设置事件」被否：多所有者正是现状不一致的根源。
2. **外观缓存 sidecar（`%LOCALAPPDATA%\LoomX\appearance.json`）**：字段 `enabled/opacity/blur/theme`，配置保存成功路径内原子写入（临时文件 + FileMove.Replace，遵循库文件既有写法）；App 在 `OnFrameworkInitializationCompleted` 显示闪屏前读取，缺失/损坏 → 默认不透明 + Debug 日志。启动对账（DB 加载后）仅允许在缓存与 DB 不一致时做一次性切换并记录 Warning（正常保存路径保证两者同步，漂移说明缓存写入失败，属异常信号）。备选「读设置专用库连接」被否：违反单次读库约束且引入启动期第二连接。
3. **窗口接入方式**：新增轻量基类或附加属性（实现会话按代码现状二选一，倾向附加属性 + 全局样式以避免 `GlassDialogWindow` 的继承树调整）；`SplashWindow` 在 `App` 显示前完成注册（构造后、Show 前），保证第一帧即目标材质。
4. **幂等收敛**：协调器应用外观前比较当前生效状态，相同则跳过（杜绝 Shell 阶段的资源重换引发的视觉跳动）；不同则一次性应用并记录 Information（含窗口数与变化项）。

## Risks / Trade-offs

- sidecar 与 DB 可能漂移（保存中途崩溃、文件被杀软拦截）：对账路径兜底 + Warning 可观测；漂移窗口最多一次启动。
- Avalonia `TransparencyLevelHint` 在窗口显示后修改在某些 Windows 版本表现不佳（不生效或闪烁）：实现时必须验证「先设 Hint 再 Show」的顺序契约，写进测试。
- 深浅主题切换（用户运行时改主题）会触发 `RefreshThemeResources` 全局资源重换，属用户显式操作，允许变化；与「无用户操作不突变」不矛盾，但需保证所有窗口同步切换而非逐帧跳变。

## Open Questions

- `GlassDialogWindow` 是否已通过应用级样式获得 AcrylicBlur（其 axaml 待实现会话确认）；若对话框窗口从未设过 `TransparencyLevelHint`，接入后其视觉变化需在验收截屏中专门对照。
