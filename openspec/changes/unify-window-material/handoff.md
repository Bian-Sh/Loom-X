# HANDOFF — unify-window-material

> 给接手本 worktree 的新会话：这是一个已立项、待实施的 openspec change。先读完本文件与 `openspec/changes/unify-window-material/` 下四份 artifact 再动手。

## 需求一句话（用户原话决策）

透明是应用级属性——「如果都是透明的，那就全都透明」：透明开启时所有顶层窗口（含启动闪屏）自第一帧即透明，关闭时全部不透明；任何窗口不得因内部流程时序中途换肤。

## 现状机制（已在 master @ ab8ca28 核实）

- `WindowAppearanceCoordinator`（`LoomX/Services/WindowAppearanceCoordinator.cs`）由主窗口私有持有（`MainWindow.axaml.cs:80`），`Apply()` 替换**应用级**画刷资源（`WindowBackgroundBrush`/`GlassBrush`）；`ApplyAppearance` 只把 `TransparencyLevelHint` 设在主窗口（`MainWindow.axaml.cs:485`，算法已固定 Acrylic）。
- `SplashWindow.axaml:14` 的 `TransparencyBackgroundFallback` 与 Border 背景都绑 `{DynamicResource WindowBackgroundBrush}`——Shell 阶段协调器换应用级画刷时，闪屏被动跟着变：这就是 2026-10-24 验收截屏第 29→31 帧记录的「纯白 → 亚克力」中途突变。
- 启动顺序：闪屏 Show（此时外观未知）→ Database 阶段读库 → Shell 阶段 `ApplyLoadedConfiguration` 才应用外观。要让闪屏第一帧就正确，必须在不读库的前提下提前拿到外观值——这是 design.md Decision 2 外观缓存 sidecar 的由来。

## 实施要点

- **硬约束（协同 change）**：`reduce-startup-db-reload` 要求启动内配置库恰好读一次，外观初值 MUST NOT 开库获取——用 `%LOCALAPPDATA%\LoomX\appearance.json`（保存设置时原子写入，启动对账兜底）。
- **闪屏顺序契约**：TransparencyLevelHint 必须在 Show 之前设置（Avalonia 显示后改 Hint 在部分 Windows 版本不生效/闪烁），写成测试。
- **幂等收敛**是消除突变的另一半：即使初值正确，Shell 阶段的重应用也必须被「状态相同即跳过」挡住。
- 主窗口 `ApplyTheme/ApplyAppearance` 的公开签名与既有更新体验契约测试（`UpdateExperienceContractTests` 断言 MainWindowViewModel/App 源码文本）如受影响需同步。
- 归档顺序依赖：本 change 含对 `desktop-startup-splash` 的 MODIFIED delta，需先归档 `improve-startup-splash-screen`（同 `reduce-startup-db-reload` 的注意项）。

## 验收标准

1. 全量 `dotnet test` 通过；外观缓存、协调器幂等、多窗口同步、闪屏顺序契约均有自动化覆盖。
2. 发布版实测（截屏 + 材质日志）：透明开启时闪屏第一帧即透明、全程无跳变；关闭时全程不透明；深浅主题各留档。对照基线：2026-10-24 验收截屏（第 29→31 帧突变）。
3. 注意 AGENTS.md「主题相关」：透明主题下 CUA 截图颜色非真实配色，判定以材质链路日志与窗口 `ActualTransparencyLevel` 实测值为准。

## 常用命令

```bash
dotnet build LoomX.slnx -c Debug        # Git Bash 下用 dotnet.exe
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Debug --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1
日志：C:\Users\BianShanghai\AppData\Local\LoomX\logs\
外观缓存落点：C:\Users\BianShanghai\AppData\Local\LoomX\appearance.json（新增）
```

## 分支

`feature/20261024/unify-window-material`（基于 master ab8ca28，含本 change 的完整 planning artifacts）

## 实施状态（2026-10-24 更新）

任务 1.1–3.3 已完成并推送，全量 `dotnet test` 1992/1992 通过。实现要点与 design 的差异：

- 协调器实例由 `App.AppearanceCoordinator` 持有；`MainWindow` 构造时从 `Application.Current as App` 取共享实例并 `ApplyTo(this)` 注册（无 App 宿主时回退独立实例）。资源解析顺序：主窗口资源 → 应用资源，兼容测试的窗口级字典注入。
- `SplashWindow` / `GlassDialogWindow` 均在自身构造函数末尾 `ApplyTo(this)` 注册（构造即注册，结构性保证「注册先于 Show」）；`ProvidersView` 的显式 `ApplyTo(dialog)` 保留（幂等去重）。
- 幂等收敛：`Apply` 与 `ApplyTheme` 对相同状态跳过（不换画刷、不触发 `AppearanceChanged`、记 Debug）；主题变体切换会重录基础颜色并对全部注册窗口同帧重放。
- 新增行为（spec 要求）：透明关闭时清空窗口级 `TransparencyLevelHint`（原实现禁用后仍残留 Acrylic 提示），既有断言已同步更新。
- 外观缓存写入挂在 `AppDataStore.UpdateSettingsAsync` / `SetGatewayRunningAsync` 成功路径，路径由 App 注入 `AppDataPaths.AppearanceCachePath`；测试与 CLI 默认不注入、不写缓存。
- 对账在 `viewModel.InitializeAfterStartupAsync()` 之后执行：一致记 Debug 零切换；漂移记 Warning 并自愈回写缓存。
- 归档注意：先归档 `improve-startup-splash-screen`，再归档本 change（`desktop-startup-splash` 的 MODIFIED delta 依赖）。

剩余：任务 3.4 发布版实测 + 截屏验收（需用户环境执行）。
