# Proposal

## Why

透明外观的应用存在窗口间不一致与时序突变：实测（2026-10-24 闪屏验收截屏）发现启动闪屏以不透明底色显示，待 Shell 阶段主窗口应用外观时，`WindowAppearanceCoordinator` 替换应用级画刷（`WindowBackgroundBrush`/`GlassBrush`）波及闪屏，闪屏在「准备主界面 → 启动完成」之间突然从纯白变成亚克力半透明——用户可感知的视觉跳变。当前 `WindowAppearanceCoordinator` 为主窗口私有（`MainWindow.axaml.cs:80`），`TransparencyLevelHint` 仅设在主窗口，其余顶层窗口（`SplashWindow`、`GlassDialogWindow`）只被动跟随应用级画刷变化，材质语义（启用/不启用、强度）没有统一的所有者。产品期望：透明是应用级属性——如果开了透明，所有窗口从第一帧就透明；不开则全部不透明，任何窗口不得中途换肤。

## What Changes

- 新增应用级窗口材质能力：以现有 `WindowAppearanceCoordinator` 为基础升级为全局唯一的窗口材质协调点，主窗口、闪屏、对话框窗口及未来新增顶层窗口在创建/显示时统一从协调器取得材质状态（透明开关、不透明度、模糊、主题画刷）并订阅后续变更。
- 启动期外观初值不读配置库：新增轻量外观缓存文件（`%LOCALAPPDATA%\LoomX\appearance.json`，设置保存成功时原子写入），App 在显示闪屏前读取它决定第一帧材质；数据库加载完成后对账，不一致仅允许一次性切换并记录 Warning。MUST NOT 为获取外观初值增加配置库读取（与 `reduce-startup-db-reload` 的启动单次读库约束协同）。
- 消除中途材质突变：外观参数（含深浅主题）在窗口存活期内 SHALL NOT 发生无用户操作驱动的变化；启动链路内应用外观的动作改为幂等收敛（第一帧已正确的窗口零切换）。
- 透明关闭路径同等一致：关闭透明后所有顶层窗口 SHALL 一致回到不透明材质。

## Capabilities

### New Capabilities

- `app-window-material`：定义应用级窗口材质一致性——统一协调点、启动期第一帧材质、无中途突变、透明关闭的一致回退。

### Modified Capabilities

- `desktop-startup-splash`：强化「启动闪屏必须遵守本地化与主题约定」中的主题一致场景——闪屏自第一帧即呈现与设置一致的最终材质，启动流程内 MUST NOT 发生材质或主题突变。归档顺序依赖同 `reduce-startup-db-reload`（需先归档 `improve-startup-splash-screen`）。

## Impact

- `LoomX/Services/WindowAppearanceCoordinator.cs`（升级为应用级协调点：多窗口订阅、初值注入）
- `LoomX/MainWindow.axaml.cs`（ApplyTheme/ApplyAppearance 迁移/委托到协调器）
- `LoomX/App.axaml.cs`（闪屏显示前读取外观缓存并注入协调器）
- `LoomX/Views/SplashWindow.axaml(.cs)`、`LoomX/Views/GlassDialogWindow.axaml.cs`（接入协调器）
- `LoomX/Services/AppDataStore.cs` 或配置保存链路（设置保存成功后原子更新外观缓存文件）
- `LoomX.Tests/*`（外观缓存读写、启动第一帧材质、无中途突变契约测试）

不涉及数据库结构变更；不改变透明参数的取值范围与设置页交互；不新增配置库读取。
