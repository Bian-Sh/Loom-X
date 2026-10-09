# enhance-service-autostart 迁移梳理（待确认）

> 日期：2026-10-10　基线：master `ff8733c`　来源分支：`comet/enhance-service-autostart` `b65512c`
> 状态：**只梳理，未动手**。`split-divider-magnetic-interaction` 按你的要求搁置不动。

## 1. 这件需求到底要什么

一句话：**Windows 登录后可自动启动 Loom-X，并且用户在概览页对网关做出的"开/停"选择能跨次重启被记住。**

来源规格：`openspec/changes/enhance-service-autostart/specs/desktop-startup-lifecycle/spec.md`（4 条 Requirement / 11 个 Scenario）。

| # | Requirement | 预期行为 |
|---|---|---|
| R1 | 用户可配置开机自启动 | 设置页新增开关"开机时启动 Loom-X"；开启→写 `HKCU\...\Run` 值 `LoomX`，关闭→删除该值；不需要管理员权限 |
| R2 | 概览页操作持久化网关运行意图 | 持久化布尔 `GatewayRunning`；**只有**概览页"启动/停止网关"这个用户动作能改它；健康检查、启动失败、自动恢复、APP 退出都不许碰它 |
| R3 | APP 启动时恢复网关运行意图 | 初始化完成后读 `GatewayRunning`：为 `true` 自动启动网关；为 `false` 保持停止；自动恢复本身**不得再写回**该值；启动失败保持 `true`，下次再试 |
| R4 | APP 退出不覆盖意图 | 退出时照旧停止并释放网关资源，但**不调用** `SetGatewayRunningAsync(false)` |

验收场景清单（照着验即可）：

- R1：启用 → 下次登录自动启动 / 关闭 → 移除注册项 / 注册失败 → APP 继续运行、Toast + 结构化日志报错、**不得报成功**
- R2：点启动 → 写 `true` 再启动 / 点停止 → 写 `false` 再停止 / 网关自行挂掉 → `GatewayRunning` 不动
- R3：`true` → 自动启动且不改值 / `false` → 不自动启动 / 自动恢复失败 → APP 继续、展示失败态、值仍为 `true`
- R4：`true` 时退出 → 资源释放、值仍 `true` / `false` 时退出 → 值仍 `false`

### 边界（设计明确排除）

- 不新增后台服务 / 托盘进程 / Windows Service；不做"APP 启动后自动启动网关"的第二个开关
- 不引入第二套配置来源，一切走 `%LOCALAPPDATA%\LoomX\LoomX.db`
- 不改网关端口、协议、健康检查规则

## 2. 现有成果：已经做完了，而且质量不错

`comet/enhance-service-autostart` 上 4 个 commit，`tasks.md` 9/9 全部 `[x]`，验证报告（随分支保存）给出：相关测试 88 通过 0 失败、Release 构建 0 错误、全量测试 1276 跑 1274 通过（2 个失败是与本 change 无关的基线契约），并做过 CUA 实机验收（含重启恢复、端口释放、配置/注册表回滚）。

实现要点：

| 关注点 | 落地方式 |
|---|---|
| 自启动 | `LoomX/Services/WindowsStartupService.cs`（新）：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 值 `LoomX`，命令为带引号的 `Environment.ProcessPath`；注册表读写抽成 `IStartupRegistryStore` 便于测试；非 Windows 平台抛 `PlatformNotSupportedException` |
| 启动编排 | `LoomX/Services/ApplicationStartupCoordinator.cs`（新）：先按 `StartWithWindows` 校准注册项，再按 `GatewayRunning` 决定是否调 `StartAsync`；任一步失败都吞掉并记录结构化日志，不中断初始化 |
| 配置字段 | `AppSettings` 新增 `StartWithWindows`、`GatewayRunning`，均在新参**末尾且带默认值** → 现有所有构造点不破坏；`GatewayRunning` 走独立单字段接口 `SetGatewayRunningAsync` `AppSettingsInput` 里**不存在**，普通设置保存无法覆盖它 |
| 意图写入 | `OverviewViewModel` 的 Start/Stop/Toggle 三处，**先写意图再操作网关**；写入失败则不执行网关操作（避免本次与下次行为不一致） |
| 变更通知 | `ConfigurationChangeFields.SettingsStartup`（`1 << 28`） |
| UI / i18n | SettingsView 常规设置区第一排加 ToggleSwitch；四套 resx 新增 `settings.startup.windows.label / .hint / .sync.failed` |

## 3. 为什么现在说"没同步"——问题只在基线上

该分支的 merge-base 是 `ef5d3c0`，而 master 已经往前走了 **27 个 commit / 102 个文件**（AskUser 自由输入、插件声明式 UI Contribution、全量测试线程隔离修复、契约测试修正等）。分支本身远落后，从没被 merge 过。

好消息：**冲突面几乎为零**。我用只读的 `git merge-tree --write-tree --messages` 做了 dry-run——

```
Auto-merging LoomX/Resources/Strings.en-US.resx
Auto-merging LoomX/Resources/Strings.ja-JP.resx
Auto-merging LoomX/Resources/Strings.resx
Auto-merging LoomX/Resources/Strings.zh-TW.resx
（输出了合并成功的 tree OID，无 Conflicts 段）
```

即：除 4 个 resx 需要三方合并（且自动合并已成功）外，**无一处冲突**。原因是分支动的那批文件（MainWindowViewModel / SettingsViewModel / App.axaml.cs / ConfigurationDbContext / ConfigurationManagementService / AppDataStore / ConfigSnapshotService / ConfigurationChange / SettingsView.axaml）master 侧一条都没碰过。

## 4. 建议的执行方案

新会话、新 worktree、扁平分支名（本环境 git 不能创建带斜杠的 ref）：

```bash
git worktree add -b service-autostart .worktrees/service-autostart master
```
> 分支名和路径都刻意不带 `/`，避开本仓库已知的嵌套 ref 静默丢失问题。

然后按顺序：

| 步骤 | 内容 | 判据 |
|---|---|---|
| 1 | `git merge comet/enhance-service-autostart` | dry-run 已无冲突；resx 确认四语言都含 3 个新 key |
| 2 | `dotnet build -c Release`（需补 3 个 env 变量） | 0 错误，无新增警告 |
| 3 | 跑相关测试：WindowsStartupService / ApplicationStartupCoordinator / AppDataStore / ConfigurationManagementService / ConfigurationDatabaseMigration / SettingsViewContract / OverviewGraphContract / AppStartupAndProviderRefreshContract / UpdateExperienceContract + LocalizationResourceParity + LocalizationNoCjk | 全绿 |
| 4 | 全量测试 | 与 master 基线比对，出现的新失败必须归零 |
| 5 | 清理一处无意义 diff：分支把 `App.axaml.cs` 首行的 UTF-8 BOM 删掉了，建议确认编译/风格无碍或直接恢复 BOM | — |
| 6 | 打包 `outputs/<时间戳>/`（单文件自包含 win-x64） | 产物 `LoomX.exe` 可启动 |
| 7 | 合并回 master + push | — |

**注意**：rebase master 期间新增了全量测试线程隔离修复与若干契约测试修正，所以必须实测，不能因为"dry-run 无冲突"就认为直接可用。特别是 Avalonia 契约测试和 i18n parity 测试最容易反悔。

## 5. 需要你拍板的几个决策点

1. **迁移方式**：merge（保留 4 个原始 commit 历史，推荐）　还是 cherry-pick 到一条新 commit？
2. **自启动语义**：保持原设计——"自启动拉起 APP，但网关是否起来由 `GatewayRunning` 决定"？还是你想要"开机自启动 = APP + 网关都起来"？
3. **实机验收**：要不要跑 CUA 实机验收？它会真实写你本机的 `HKCU Run\LoomX` 和 `%LOCALAPPDATA%\LoomX\LoomX.db`（做完回滚，旧 change 已经这么干过一次）。还是只到"全量测试通过 + 打包"为止，实机由你自己点？
4. **openspec 归档**：仓库有 `openspec/changes/archive/` 惯例。合入 master 后是否一并把 `enhance-service-autostart` 归档（change 目录移到 archive、spec 落到 `openspec/specs/desktop-startup-lifecycle/`）？还是保持现状不归档？

## 6. 风险与取舍

| 风险 | 影响 | 处置 |
|---|---|---|
| 全量测试线程隔离规则变化（master `30417dc`） | 新增/修改的契约测试可能不合规 | 步骤 3-4 实测，逐个修 |
| `ConfigurationDatabase.InitializeAsync` 有进程内完成缓存 | 新增两列的迁移测试若不 reset 缓存会假通过 | 必要时 `ResetInitializationCache()`，参考现有 `ConfigurationDatabaseMigrationTests` |
| 本机环境被改 | Run 注册表值、`LoomX.db` 增加两列 | 实机验收前备份注册表值与数据库，验收后恢复 |
| 打包方式与旧 change 不一致 | 旧产物是 446 文件目录式 | 按当前仓库惯例走**单文件自包含**（约 143MB，构建 12-16s） |
| `outputs/` 下有 3 个悬空 junction | 与本次无关 | 不修、不抄 |
