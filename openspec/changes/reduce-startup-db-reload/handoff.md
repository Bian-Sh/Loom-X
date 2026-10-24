# HANDOFF — reduce-startup-db-reload

> 给接手本 worktree 的新会话：这是一个已立项、待实施的 openspec change。先读完本文件与 `openspec/changes/reduce-startup-db-reload/` 下四份 artifact 再动手。

## 背景一句话

启动闪屏重构后，Database 阶段的启动快照只读一次库，但 Shell 阶段（主窗口初始化，闪屏还开着）又对配置库做了两次完整读取，违反 `desktop-startup-splash` 的「后续启动阶段复用该快照」场景，白白多花约 1 秒。

## 实测证据（2026-10-24 发布版，`%LOCALAPPDATA%\LoomX\logs\loomx-20261024.log`）

```
00:22:51.103  启动快照读取完成 8 27 3 3            ← 唯一一次快照（1.75s）
00:23:07.589  Provider 页面刷新开始                 ← Shell 阶段开始
00:23:07.591  配置快照同步读取开始                  ← 重复读库 #1（全量重新读）
00:23:08.211  Provider 列表数据库初始化完成          ← 重复读库 #2（助手模型摘要）
```

调用链（已在 master @ ab8ca28 上核实）：

1. `MainWindowViewModel.InitializeAfterStartupAsync` → `ApplyLoadedConfiguration` → Providers 页面 `OnConfigurationChanged`/首刷 → `RefreshCoreAsync`（`MainWindowViewModel.cs:1162`）→ `dataStore.InitializeAsync()`（已初始化仍会重读全库，走 `ConfigSnapshotService.Load()` 同步快照）。
2. `AssistantViewModel.InitializeAfterStartup` → `RefreshModelSummary`（`AssistantViewModel.cs:1092`）→ `service.ListAvailableModelsAsync()` → `ConfigSnapshotService.ListProvidersAsync`（`ConfigSnapshotService.cs:86`）。

## 实施要点

- 方案与备选取舍见 `design.md` 的 Decisions 1-3；核心是拆分「初始化（一次）」与「显式刷新（按需）」语义，不要给 RefreshAsync 加 skipRead 参数。
- 测试策略（tasks 2.1-2.4）：先写计数测试复现两次重复读库（红），改造后转绿；必须有「显式刷新仍读库」的反向用例。
- 归档顺序：本 change 的 spec delta 是对 `desktop-startup-splash` 的 MODIFIED，而该 spec 尚未归档（`improve-startup-splash-screen` 代码已进 master 但 change 未归档）。归档本 change 前先归档 `improve-startup-splash-screen`（其实测遗留任务已于 2026-10-24 完成：4.3 全量回归 1941 通过、4.2 启动耗时已实测、2.1 能力已实现）。

## 验收标准

1. `dotnet test`（LoomX.Tests）全量通过，含新增的「启动链路读库一次」集成测试。
2. 发布版实测启动日志中，从进程开始到主窗口可交互，「配置数据库初始化完成」类条目只出现一次。
3. Provider 编辑保存、配置变更事件等显式刷新行为与改造前一致。

## 常用命令

```bash
dotnet build LoomX.slnx -c Debug
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Debug --no-build
dotnet.exe 注意：Git Bash 里直接 `dotnet` 会命中无扩展名 ELF 报 Exec format error，用 `dotnet.exe`。
打包验收：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1
日志：C:\Users\BianShanghai\AppData\Local\LoomX\logs\
```

## 分支

`feature/20261024/reduce-startup-db-reload`（基于 master ab8ca28，含本 change 的完整 planning artifacts）
