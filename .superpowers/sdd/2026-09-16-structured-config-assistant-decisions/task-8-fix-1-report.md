# Task 8 修复轮 1 实现报告

日期：2026-09-17  
分支：`codex/structured-config-assistant-decisions`  
范围：仅处理首轮审查 I1，并完成 M1 的代码侧确认与提交后发布验证方案；本轮未执行最终 publish、未提交、未 push。

## 结论

- I1 已通过 TDD 修复：历史会话继续发送前，会把持久化的旧 System 策略规范化为当前 `AssistantService.SystemPrompt`，实际 `ModelRequest.Messages` 中只保留一条当前 LoomX 策略。
- M1 已确认：`--allow-multiple-instances` 与 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1` 都会令 `allowMultipleInstances=true`；该判断发生在 shell bootstrap 与单实例 mutex 之前，因此会同时绕过 bootstrap 父子进程切换和已有实例误判。对应单元测试 3/3 通过。
- 按协调要求没有执行最终发布；后续必须从协调者提交后的 HEAD 生成新产物，以程序集版本关联提交。

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

## 协调者提交后执行的发布与启动验证

### 1. 必须从已提交 HEAD 发布

在协调者完成提交后，从仓库根目录执行：

```powershell
$repo = 'D:\AppData\Github\Loom-X - Copy'
Set-Location -LiteralPath $repo
$head = (git rev-parse HEAD).Trim()
$stamp = Get-Date -Format 'yyyy-MM-dd-HHmm'
$out = Join-Path $repo "outputs\$stamp-task-8-fix-1-$($head.Substring(0, 7))"
dotnet publish LoomX/LoomX.csproj -c Release -r win-x64 --self-contained true -p:SourceRevisionId=$head -o $out
$version = (Get-Item -LiteralPath (Join-Path $out 'LoomX.dll')).VersionInfo.ProductVersion
[pscustomobject]@{ Head = $head; Output = $out; ProductVersion = $version }
```

必须核对：

- publish 退出码为 0；
- 输出目录是本次新建、可读时间命名的 `outputs/...`，不得覆盖或清理其他 Session 产物；
- `ProductVersion` 包含本次已提交的完整 `$head`（预期形如 `0.12.6+<HEAD>`）。

### 2. 推荐使用命令行参数启动

```powershell
$exe = (Resolve-Path -LiteralPath (Join-Path $out 'LoomX.exe')).Path
$log = Join-Path $env:LOCALAPPDATA ("LoomX\logs\loomx-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
$process = Start-Process -FilePath $exe -ArgumentList '--allow-multiple-instances' -WorkingDirectory $out -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 5
$actual = Get-Process -Id $process.Id -ErrorAction Stop
if ($actual.Path -ne $exe) { throw "启动进程路径不匹配：$($actual.Path)" }
$actual | Select-Object Id, Path, StartTime
Get-Content -LiteralPath $log -Tail 500 | Select-String -Pattern "进程 $($process.Id)"
```

由于参数令 `allowMultipleInstances=true`，本次 `Start-Process -PassThru` 返回的 PID 应直接是完成初始化的发布应用 PID，而不是短生命周期 bootstrap 父进程。

### 3. 环境变量等价启动方式

如需验证环境变量入口，使用独立一次启动：

```powershell
$previous = $env:LOOMX_ALLOW_MULTIPLE_INSTANCES
try {
    $env:LOOMX_ALLOW_MULTIPLE_INSTANCES = '1'
    $process = Start-Process -FilePath $exe -WorkingDirectory $out -WindowStyle Hidden -PassThru
}
finally {
    if ($null -eq $previous) { Remove-Item Env:LOOMX_ALLOW_MULTIPLE_INSTANCES -ErrorAction SilentlyContinue }
    else { $env:LOOMX_ALLOW_MULTIPLE_INSTANCES = $previous }
}
Start-Sleep -Seconds 5
$actual = Get-Process -Id $process.Id -ErrorAction Stop
if ($actual.Path -ne $exe) { throw "启动进程路径不匹配：$($actual.Path)" }
$actual | Select-Object Id, Path, StartTime
Get-Content -LiteralPath $log -Tail 500 | Select-String -Pattern "进程 $($process.Id)"
```

### 4. PID、Path 与日志核对清单

只针对本轮 `$process.Id` 核对：

1. `Get-Process -Id $process.Id` 在等待后仍存在；
2. `Path` 与本轮新发布目录中的 `LoomX.exe` 完全相等；
3. 日志存在同一 PID 的“调试启动已允许多个桌面实例”；
4. 日志存在同一 PID 的“桌面应用启动”，且其中“进程路径”与 `$exe` 完全一致；
5. 同一 PID 不应出现“检测到已有 LoomX 桌面实例”或 bootstrap 失败/退出记录；
6. 记录启动前已有 LoomX PID，仅在验证结束时执行 `Stop-Process -Id $process.Id`，不得结束其他已有实例。

本轮未执行上述 publish/启动步骤，等待协调者提交代码后再从提交 HEAD 验证。
