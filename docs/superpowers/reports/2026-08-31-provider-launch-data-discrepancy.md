# Provider 数据随启动通道不一致：5W1H 复盘与解决方案

## 结论摘要

2026-08-31 发生的不是 Provider 页面渲染异常，也不是普通 SQLite 查询差异。已完成的成对实验证实：**同一 EXE、同一 Windows 用户、无并发 OllamaHub 进程时，从 Codex/PowerShell/CUA 启动与从 Windows Explorer 启动，进程所见的配置库内容不同。**

| 启动通道 | 所见主库 SHA-256 前缀 | 原始查询结果 |
|---|---:|---|
| Codex 终端、PowerShell、CUA `launch_app` | `BA12FB3A5A27F6F0` | Provider `0`，Model `0`，Endpoint `3` |
| Windows Explorer/Shell | `49913CFDC703BFE7` | Provider `2`，Model `10`，Endpoint `3` |

两个进程使用相同的数据库逻辑路径 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db`。额外硬链接路径 `%LOCALAPPDATA%\\Packages\\<CodexPackage>\\LocalCache\\Local\\OllamaHub\\OllamaHub.db` 与前者的文件 ID 相同，因此“存在两份普通数据库文件”已被排除。

最终根因是 **Codex 启动上下文与 Explorer 主机启动上下文具有不同的文件系统视图**；这与沙盒、打包容器或令牌关联的文件系统虚拟化相符。现有证据足以确认“视图不同”的行为，但不足以把底层机制精确归结为某一种 Windows 隔离技术。正确方案是把实际 UI 进程交给 Windows Shell 启动，而不是继续修改 SQL、页面或数据库内容。

## 5W1H

| 维度 | 事实 |
|---|---|
| What | 由 Codex 侧启动时，Provider 页面显示 `0 Provider / 0 Model / 3 Endpoint`；由 Explorer 启动时，显示 `2 Provider / 10 Model / 3 Endpoint`。原始 `Providers` 表、配置快照和页面结果在各自视图内一致。 |
| Who | 用户通过 Windows Explorer/Shell 启动；agent 通过 Codex 终端、PowerShell 或 CUA 启动。两侧均为同一 Windows 登录用户，运行同一发布包。 |
| When | 2026-08-31 的对照实验中，09:26 与 09:36 的 Codex 侧启动读取 `BA12...`；Explorer 启动读取 `49913...`。后续发布包继续验证了 Shell 启动链路。 |
| Where | 代码使用 `AppDataPaths.DatabasePath`，运行时路径为 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db`。不同之处不是代码拼出的路径，而是启动宿主所能看到的该路径内容。 |
| Why | Codex 进程上下文看到空 Provider 的数据库视图；Explorer 上下文看到用户实际 Provider 数据的数据库视图。并发、工作目录和读取时写库是需要防范的风险，但不是本次“启动通道不同、结果不同”的最终解释。 |
| How | 以数据库主库/WAL/SHM 指纹、原始行数、进程路径、PID、用户和启动通道构成证据组；再通过一次性 Windows Shell 启动桥让真正的 UI 进程进入 Explorer 对应的视图。 |

## 发现过程与误区

### 1. 从“活动页问题”误入“页面或查询问题”

现象最先表现为 Provider 页面为空。这个表象很容易让人检查 Avalonia 绑定、刷新时机、DTO 映射或 Provider 筛选。

**关键检查**：用 CUA 截图确认页面实际显示“暂无 Provider”，同时在 `ConfigSnapshotService` 记录原始 Provider 行数、配置快照数和 `ListProvidersAsync` 返回数。

**结果**：空页面时三层均为 0，正常页面时三层均为 `2 Provider / 10 Model`。这把问题从 UI 层排除，范围缩小为“进程读到了哪一份数据”。

### 2. 误区：过早把多实例当作根因

SQLite 与旧连接并存时，确实可能存在并发读写、WAL、迁移和回写风险。因此最初把多个 `OllamaHub.Desktop.exe` 作为主要嫌疑。

**为什么不成立为最终根因**：用户要求在无旧进程的条件下重测。随后在无并发时，Codex/PowerShell/CUA 仍稳定读取 `BA12...` 和 Provider 0；Explorer 启动同一 EXE 则读取 `49913...` 和 Provider 2。并发不能解释这个单进程的启动通道差异。

**保留的工程动作**：单实例互斥和写入锁依然有价值，防止真实并发扩大 SQLite 风险；但它们是防护，不是本次根因说明。

### 3. 误区：把工作目录当作根因

PowerShell、Codex 和双击 EXE 的工作目录常不同。应用把 `Environment.CurrentDirectory` 规范化为发布目录，并记录规范化前后路径，以消除相对路径变量。

**推翻证据**：项目数据库路径是绝对的 LocalAppData 路径。09:26 从仓库工作目录启动仍得到 `BA12...`/0；Explorer 启动时即使初始工作目录为 `C:\\Windows\\System32`，仍得到 `49913...`/2。工作目录不是决定变量。

### 4. 误区：认为一定有两份普通数据库文件

发现 Codex 包缓存目录下有一条额外路径后，曾怀疑 Codex 在读取一个独立副本：

```text
%LOCALAPPDATA%\OllamaHub\OllamaHub.db
%LOCALAPPDATA%\Packages\<CodexPackage>\LocalCache\Local\OllamaHub\OllamaHub.db
```

**验证**：比较路径哈希、`fsutil hardlink list` 和文件 ID。

**结果**：两个路径是同一文件的硬链接，不是两份普通文件。这个检查排除了“应用代码选择了另一条数据库路径”，但不能排除不同启动令牌或沙盒层看见不同的文件系统视图。

### 5. 决定性实验：只改变启动宿主

此前的日志还曾让问题看起来像“外部回写”：先看到 Explorer 进程中的 Provider 2，随后 PowerShell 新进程又看到 Provider 0。仅凭时间相关性，无法判断是文件被改写，还是不同宿主读取了不同视图。

因此将变量收敛为以下实验：终止全部 OllamaHub 进程，固定同一个发布 EXE 与同一用户，分别由 CUA/PowerShell 和 `explorer.exe` 启动，并记录数据库指纹、原始行数和页面状态。

**实验结果**：

```text
CUA/PowerShell 启动 -> BA12... -> Provider 0 -> 页面“暂无 Provider”
Explorer 启动       -> 49913... -> Provider 2 -> 页面显示 Deepseek、Sensenova
```

任务记录明确将该结果定位为“Codex 终端启动上下文与 Explorer 主机的文件系统视图不同”。这是比“数据库可能被改写”更强的、可重复的证据。

## 最终方案：Windows Shell 启动桥

### 设计

`OllamaHub.Desktop/App.axaml.cs` 在桌面生命周期最早阶段创建 `Local\\OllamaHub.Desktop.ShellBootstrap` Mutex：

1. 首个启动进程拥有该 Mutex 时，用 `explorer.exe "<当前 EXE>"` 启动同一个 EXE，并退出自身。
2. Explorer 拉起的子进程无法再次拥有 Bootstrap Mutex，因此不会循环桥接，继续正常初始化 UI。
3. `Local\\OllamaHub.Desktop` 单实例 Mutex 只允许一个真正桌面实例进入应用初始化，避免并发 SQLite 访问。

这不是复制、迁移、清空或修复数据库；它只改变**真正承载 UI 的进程由谁启动**。无论自动化工具怎样启动发布 EXE，最终界面进程都会落在与用户手动双击相同的 Windows Shell 上下文。

### 配套防护

以下改动不承担“解决不同文件系统视图”的职责，但防止将来出现真正的数据库写入竞争：

1. Provider、Model、Settings 普通读取采用 `SqliteOpenMode.ReadOnly`。
2. 正常读取不再无条件调用数据库初始化或保存。
3. 管理写操作使用配置库初始化锁。
4. 日志记录 PID、进程路径、工作目录、主库/WAL/SHM 指纹、`journal_mode`、`schema_version`、原始行数、快照数和页面结果。

## 解决后的验证

发布包 `outputs\\20260831-100000\\OllamaHub.Desktop.exe` 经过 CUA 启动时，首个桥接进程交由 Shell 重启；真正 UI 进程的日志记录：

```text
主库指纹: 49913CFDC703BFE7
Provider: 2
Model: 10
Endpoint: 3
Provider 页面: Deepseek、Sensenova
```

任务记录还确认：单实例为 PID `3824`，且完整自动化测试 `81/81` 通过。测试通过只证明既有行为无回归；启动通道问题是否解决，以 Shell 和 Codex 两侧最终 UI 进程均读取 `4991...`/`2/10/3` 为准。

## 面向程序员与 AI 的排查规则

### 最小证据组

每次比较必须同时记录：

```text
启动宿主 + EXE 完整路径 + PID + 用户/会话
+ 数据库绝对路径 + 主库/WAL/SHM 的哈希与修改时间
+ 原始 Provider/Model 行数 + 配置快照数 + 页面/API 返回数
```

SQLite WAL 模式下，主库、`-wal`、`-shm` 必须一起检查。只记录页面结果或只哈希主库，都无法可靠区分 UI 问题、数据问题、WAL 状态和文件视图问题。

### 禁止的推断

- 不得因“启动者不同”直接判定为多实例、工作目录或 SQL 查询问题。
- 不得因“页面为空”直接修改 UI；先确认原始表和快照。
- 不得因“同一路径”就假设两个进程看到同一文件内容；容器、包缓存、沙盒、令牌与虚拟化层都可能改变视图。
- 不得把一个合理风险假设写成根因，除非已用只改变单个变量的对照实验验证。

### 需要进一步取证时

若未来仍出现 Shell 与自动化通道差异，应保留数据库主库/WAL/SHM 副本和结构化日志，验证启动链条的父进程与令牌信息；必要时使用 Process Monitor、Sysmon 或 Windows 容器/包诊断工具确认具体的重定向/虚拟化机制。不要先覆盖、迁移或删除用户数据库。

## 相关实现与证据

- `OllamaHub.Desktop/App.axaml.cs`：Shell 启动桥与单实例互斥。
- `OllamaHub.Desktop/Services/ConfigSnapshotService.cs`：只读查询及数据库指纹、原始行数日志。
- `OllamaHub/Configuration/ConfigurationDbContext.cs`：配置库初始化锁。
- `OllamaHub/OllamaHubHost.cs`：服务端 SQLite 连接配置。
- `%LOCALAPPDATA%\\OllamaHub\\logs\\ollamahub-20260831.log`：本次运行证据。
