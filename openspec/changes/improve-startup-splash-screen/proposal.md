# Proposal

## Why

当前桌面启动没有真正的闪屏，只是在主窗口里显示一行占位文案；同时启动链路存在重复读库和过早拉起插件运行时的问题，导致用户在看似“卡住”的界面上等待很久。需要把启动过程改成可感知、可解释、可验证的专用闪屏体验，并顺手消除最明显的重复初始化开销。

## What Changes

- 新增桌面启动闪屏能力：应用启动时显示独立的启动闪屏窗口，主窗口在其完成后才进入可用状态。
- 闪屏展示真实的启动阶段与整体进度，至少覆盖：初始化配置数据库、编译插件、加载插件、启动网关、准备主界面。
- 新增启动进度模型，负责驱动阶段状态与进度条，而不是在 UI 层写死假进度。
- 优化启动链路，减少同一配置库的重复初始化与重复读取，并避免在主窗口构造阶段过早触发插件运行时。
- 替换当前“此页面已经完成导航接入...”这类占位文案，让启动阶段和主窗口职责分离。
- 保持现有本地化、主题与可访问性约定。

## Capabilities

### New Capabilities
- `desktop-startup-splash`: 定义桌面启动闪屏、启动进度、阶段可见性和启动完成切换行为。

### Modified Capabilities
- 无。现有 `desktop-startup-lifecycle` 的网关意图恢复与开机自启动语义不变，本变更只调整其呈现与初始化编排，不改变已有需求。

## Impact

- `LoomX/App.axaml.cs`
- `LoomX/MainWindowViewModel.cs`
- `LoomX/Services/*`
- `LoomX/Views/*`
- `LoomX/Resources/Strings*.resx`
- `LoomX.Tests/*`
- `LoomX.PluginHost/*`

不涉及数据库结构变更，不改变插件包布局，不改变用户数据或配置接口。
