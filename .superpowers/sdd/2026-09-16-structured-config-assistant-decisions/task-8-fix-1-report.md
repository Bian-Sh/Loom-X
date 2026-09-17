# Task 8 修复轮 1 实现报告

日期：2026-09-17
分支：`codex/structured-config-assistant-decisions`
范围：仅处理首轮审查 I1，并完成 M1 的代码侧确认与发布验证；代码修复由协调者提交至 HEAD `164b1e94f0f337fb9a2ad69f09a248c312cb43ec` 后，本轮已从该干净 HEAD 完成 standalone 发布与启动核验，未 commit、未 push。

## 结论

- I1 已通过 TDD 修复：历史会话继续发送前，会把持久化的旧 System 策略规范化为当前 `AssistantService.SystemPrompt`，实际 `ModelRequest.Messages` 中只保留一条当前 LoomX 策略。
- M1 已确认：`--allow-multiple-instances` 与 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1` 都会令 `allowMultipleInstances=true`；该判断发生在 shell bootstrap 与单实例 mutex 之前，因此会同时绕过 bootstrap 父子进程切换和已有实例误判。对应单元测试 3/3 通过。
- 已从协调者提交后的干净 HEAD `164b1e94f0f337fb9a2ad69f09a248c312cb43ec` 生成新 standalone 产物；`LoomX.dll` 的 `ProductVersion` 包含完整 HEAD，并完成指定参数启动、PID/Path/日志与安全停止验证。

## 根因与最小修复

### 根因

1. `AssistantSessionStore.LoadAsync` 按原样恢复持久化消息，其中包括升级前的旧 System 消息。
2. `AssistantService.LoadSessionAsync` 原先直接把恢复会话设为 `CurrentSession`，没有应用当前服务策略。
3. `AgentLoop` 直接把 `session.Messages` 传入 `ModelRequest`，因此旧 System 消息会成为继续发送时的实际模型策略。

### 修复位置与行为

选择在 `AssistantService.LoadSessionAsync` 完成恢复后、暴露为当前会话前调用 `AgentSession.ApplySystemPrompt`：

- 删除恢复到内存中的全部 System 消息；
- 把当前 `AssistantService.SystemPrompt` 作为唯一 System 消息插到消息首位；
- 若历史会话已有 System 消息，复用第一条历史 System 消息的 `Id` 与 `Timestamp`，使追加式 `AssistantSessionStore.SaveAsync` 将其视为已知消息，不再追加第二条 LoomX 策略；
- 同步更新 `AgentSession.Options.SystemPrompt`，但回归测试的核心断言落在实际 `ModelRequest.Messages`，不依赖 Options 属性证明修复。

此方案不让持久化层认识具体产品策略，避免把 `AssistantService` 的当前提示常量下沉到通用存储层；旧 jsonl 中原有行不做破坏性重写，但每次经 `AssistantService` 恢复时都会在运行时规范化，旧策略不会进入实际模型请求，也不会因保存而追加重复策略消息。

## TDD 证据

### RED

先只新增回归测试和测试注入点，未修改生产代码，执行：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantServiceTests.LoadSession_继续发送时使用当前系统策略且不重复旧策略"`

- 退出码：1。
- 失败：1，通过：0，跳过：0。
- 准确失败：`Assert.True() Failure; Expected: True; Actual: False`，位于资料通道顺序断言；实际请求仍携带 fixture 中的旧提示“只使用 Browser Bridge，并允许自动绕过 JS challenge”，没有当前“原生/官方 → Browser Bridge → assistant.ask_user”顺序。

### GREEN

完成最小生产修复后重跑同一命令：

- 退出码：0。
- 失败：0，通过：1，跳过：0。
- 测试构造旧提示会话、从磁盘加载、继续发送，并直接检查 `ScriptedModelClient.Requests` 捕获的 `ModelRequest.Messages`：当前通道顺序、Cloudflare/JS challenge 交还与禁止绕过规则均存在；System 消息恰好一条；旧提示不存在。

## 验证结果

### AssistantServiceTests 全组

`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantServiceTests"`

- 退出码：0。
- 失败：0，通过：23，跳过：0。

### Task 8 定向过滤集合

`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~Toml|FullyQualifiedName~UserDecision|FullyQualifiedName~AssistantTools|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~SkillStoreTests"`

- 退出码：0。
- 失败：0，通过：224，跳过：0（原 223 项加本轮 1 项回归测试）。

### 多实例启动策略

`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~InstanceLaunchPolicyTests"`

- 退出码：0。
- 失败：0，通过：3，跳过：0。
- 代码路径核对：`App.OnFrameworkInitializationCompleted` 在 bootstrap 分支和单实例 mutex 分支之前计算 `allowMultipleInstances`；值为 true 时跳过两个分支，并记录“调试启动已允许多个桌面实例”后继续记录“桌面应用启动”。

### 已知既有警告

测试仍报告既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903；首次编译还可见既有 CS8618、CA2024、测试 CS8602。本轮未扩大范围处理。

## 修改文件

- `LoomX.Harness/AgentSession.cs`：新增当前系统策略规范化方法，并保持 Options 与运行时消息一致。
- `LoomX/Assistant/AssistantService.cs`：历史会话加载后应用当前系统策略。
- `LoomX.Tests/Assistant/AssistantServiceTests.cs`：新增从旧持久化会话继续发送的实际请求回归测试；允许测试注入指定 `AssistantSessionStore`。
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-8-fix-1-report.md`：本报告。

未修改 `AssistantSessionStore.cs`、OpenSpec tasks、计划、Comet 状态、Task 8 首轮审查报告或其他无关文件。

## M1 standalone 发布与启动验证（已完成）

验证时间：2026-09-17 16:01（Asia/Shanghai）
代码 HEAD：`164b1e94f0f337fb9a2ad69f09a248c312cb43ec`

### 1. 发布与版本追溯

发布前确认：

- `git status --short --branch` 仅显示分支跟踪信息，没有工作区修改；
- `git rev-parse HEAD` 为 `164b1e94f0f337fb9a2ad69f09a248c312cb43ec`；
- 目标目录此前不存在，没有覆盖、删除或移动任何既有 `outputs` 内容。

实际执行：

```powershell
$head = '164b1e94f0f337fb9a2ad69f09a248c312cb43ec'
$out = 'D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1601-structured-config-assistant-decisions'
dotnet publish LoomX/LoomX.csproj -c Release -r win-x64 --self-contained true -p:SourceRevisionId=$head -o $out
```

结果：

- publish 退出码：`0`；
- 输出目录：`D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1601-structured-config-assistant-decisions`；
- `LoomX.dll ProductVersion`：`0.12.6+164b1e94f0f337fb9a2ad69f09a248c312cb43ec`；
- ProductVersion 包含指定完整 HEAD，程序集可追溯；
- publish 仅出现仓库既有 NU1903、CS8618、CA2024 警告，没有发布错误。

### 2. 启动前 PID 清单

启动前仅存在一个其他 LoomX 实例：

| PID | Path | StartTime |
|---:|---|---|
| 35476 | `D:\AppData\Github\Loom-X\outputs\LoomX-win-x64-2026-09-16-activity-scrollbar-right\LoomX.exe` | 2026-09-17 03:02:58 |

该实例不是本轮产物，验证全过程未停止或修改它。

### 3. 指定参数启动与进程核对

实际执行：

```powershell
$process = Start-Process -FilePath 'D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1601-structured-config-assistant-decisions\LoomX.exe' -ArgumentList '--allow-multiple-instances' -WorkingDirectory 'D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1601-structured-config-assistant-decisions' -WindowStyle Hidden -PassThru
```

等待 8 秒后的结果：

- 本轮 PID：`43440`；
- PID 仍存活；
- `Process.Path`：`D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1601-structured-config-assistant-decisions\LoomX.exe`；
- Path 与启动的绝对 exe 路径完全一致；
- StartTime：`2026-09-17T16:01:53.8300250+08:00`；
- 启动后 PID 清单包含原有 `35476` 和本轮 `43440`，证明没有把 bootstrap 父进程误当成实际应用 PID。

### 4. 日志核对

日志文件：`C:\Users\BianShanghai\AppData\Local\LoomX\logs\loomx-20260917.log`

同一 PID `43440` 的关键日志：

- 第 8558 行：`2026-09-17 16:01:54.906 +08:00 [WRN] LoomX.App 调试启动已允许多个桌面实例，进程 43440`；
- 第 8559 行：`2026-09-17 16:01:54.938 +08:00 [INF] LoomX.App 桌面应用启动，进程 43440`；该行记录的进程路径、基目录、启动工作目录和规范化工作目录均指向本轮输出目录；
- 后续同一 PID 还有配置服务创建、Provider 页面刷新和概览刷新完成日志，证明应用已继续完成初始化，而不是短生命周期 bootstrap 进程。

匹配统计：

- “调试启动已允许多个桌面实例”：`1` 条；
- “桌面应用启动”：`1` 条；
- 同一 PID 的“检测到已有 LoomX 桌面实例”、自启动子进程失败或 bootstrap 失败信息：`0` 条。

### 5. 安全停止与最终 PID 清单

仅执行：

```powershell
Stop-Process -Id 43440 -ErrorAction Stop
```

结果：

- 本轮 PID `43440` 已停止；最终延迟 2 秒复核 `Get-Process -Id 43440` 返回不存在；
- 原有 PID `35476` 仍存活，Path 保持不变；
- 最终 LoomX PID 清单仅剩 `35476`；
- 未停止或影响其他 LoomX 进程。

M1 发布验证已完成，不再存在“待协调者提交后执行”的步骤。
