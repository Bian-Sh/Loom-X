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

## 实测状态（2026-10-24 12:50 更新）

- 修复：协调器实例最初未接 logger（材质链路日志不落盘），现于 App 注入 `ILogger<WindowAppearanceCoordinator>`；「启动外观对账一致，零切换」由 Debug 提升为 Information（Serilog 文件级别为 Information，Debug 不落盘）。
- **漂移路径已实证**（发布版 20261024-122823，12:28 运行，日志 loomx-20261024.log 12:29:03 WRN）：
  - `外观缓存与配置库不一致，已按配置库一次性切换 False/86/24/"system" -> True/66/35/"light"`
  - `appearance.json` 自愈写入 `{"enabled":true,"opacity":66,"blur":35,"theme":"light"}`，与配置库逐字段一致。
- **对齐路径待安静机器验证**：12:31 起另一 session（stabilize-plugin-compile-cache）的发布版实例在同一桌面/同一日志目录并发实测，单实例互斥与 Serilog buffered 双写者把当日日志文件滚动截断（loomx-20261024*.log 出现 KiB 边界截断、后缀递增），后续两次启动未能取得有效证据，已按协作约定停止并发桌面实测。
- 安静机器上的验证步骤（约 1 分钟）：确认无 LoomX 进程后启动本 worktree 最新发布版两次；第 2 次启动日志应出现且仅出现一次 `窗口材质应用完成 True 66 35 acrylic 1 initial`（窗口数 1=闪屏，先于 Show）+ `窗口主题应用完成 light`，随后 `启动外观对账一致，零切换 True 66 35 light`，且启动链路内不再有第二次材质/主题应用记录——即第 29→31 帧突变消除的日志证据。截屏仅留档（透明下颜色失真），判定以日志为准。
- `outputs/screenshots-untrusted/` 中的截图因并发实例无法归属，不作验收依据。
