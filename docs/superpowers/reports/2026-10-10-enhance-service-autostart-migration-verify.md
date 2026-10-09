# 服务自启动迁移合入验证报告

日期：2026-10-10　分支：`service-autostart`（worktree `.worktrees/service-autostart`）
源：`comet/enhance-service-autostart` `b65512c` → 基线：master `ff8733c`

## 结论

迁移合入成功，构建、相关测试、全量测试、CUA 实机验收全部通过，环境已回滚。可并入 master 并推送远端。

## 迁移方式

- `git merge --no-ff comet/enhance-service-autostart`：**零冲突**。与 master 演进（27 commits / 102 文件）的唯一交集是 4 个 resx，三方合并自动成功，四语言均含 3 个新 key。
- 追加提交：恢复 `App.axaml.cs` 首行 UTF-8 BOM（分支曾无意删除）+ openspec 归档（change → `archive/2026-10-10-enhance-service-autostart/`，spec 提升 → `openspec/specs/desktop-startup-lifecycle/spec.md`）。

## 自动化验证

| 项目 | 结果 |
|---|---|
| Release 构建 | 0 错误；仅既有警告 CS8618 ×1、CA2024 ×2 |
| 相关测试（WindowsStartup / ApplicationStartup / AppDataStore / Configuration* / 契约 / 本地化） | **119 通过 0 失败** |
| 全量测试 | **1320 通过 0 失败**（旧基线的 2 个契约失败已被 master 修复） |

## CUA 实机验收（发布包 `outputs/20261010-031040/`）

初始状态：`(StartWithWindows, GatewayRunning) = (0, 0)`，`Run\LoomX` 不存在，11434 未占用。

| # | 场景 | 结果 |
|---|---|---|
| 1 | 设置页"开机时启动 Loom-X"位于通用 tab 第一排 | ✅ 截图 `settings-01-before-toggle.png` |
| 2 | 开启开关 | ✅ `Run\LoomX = "<发布目录>\LoomX.exe"`（带引号绝对路径），DB 变 `(1, 0)` |
| 3 | 概览页"启动网关" | ✅ UI"运行中/接口在线"，DB 变 `(1, 1)`，11434 LISTENING |
| 4 | 直接关闭 APP | ✅ 进程退出、11434 释放，DB 仍 `(1, 1)`（退出不覆盖意图） |
| 5 | 重启 APP 不做任何操作 | ✅ 网关自动恢复"运行中"，DB 仍 `(1, 1)`（自动恢复不改写意图），截图 `gateway-restored-after-restart.png` |
| 6 | 关闭自启动开关 | ✅ `Run\LoomX` 移除，DB 变 `(0, 1)`（设置页保存不触碰 GatewayRunning） |
| 7 | 环境回滚 | ✅ 恢复 DB 备份至 `(0, 0)`，Run 无残留，无遗留进程；备份存于 `outputs/20261010-031040/cua-backup/` |

验收过程备注：期间控制台日志出现一次设置页加载后的自动保存（`Windows 开机自启动已更新 False`，03:16:04），为导航点击误触发的 UI 事件，值无变化、不影响验收结论；重新规划点击路径后全部场景按预期复现。

## 交付物

- 发布包：`outputs/20261010-031040/`（LoomX.exe 单文件自包含 + Skills + BrowserExtension + plugins）
- 验收截图与备份：`outputs/20261010-031040/`（`settings-01-before-toggle.png`、`settings-02-toggle-on.png`、`gateway-restored-after-restart.png`、`cua-backup/`）
