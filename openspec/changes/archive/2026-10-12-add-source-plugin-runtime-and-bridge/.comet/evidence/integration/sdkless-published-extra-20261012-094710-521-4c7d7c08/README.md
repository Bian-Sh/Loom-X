# 最新发布 DLL 的无 SDK 运行约束集成验收侧包

日期：2026-10-12。**侧包状态：DONE。发布状态：候选未放行。** 原 Runtime / Feedback 第二轮仍执行中；本侧包不作审查、不代替主会话全量测试、不代表 change 完成，不勾任务，不写流程状态，不提交 Git，不派下级。

## 对象、目录与结果

- 唯一工作树：`D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge`。
- 发布候选：`outputs/20261012-094447`，不覆盖发布目录，不修改产品 DLL。
- 新证据根：`D:\AppData\Github\Loom-X\.worktrees\add-source-plugin-runtime-and-bridge\openspec\changes\add-source-plugin-runtime-and-bridge\.comet\evidence\integration\sdkless-published-extra-20261012-094710-521-4c7d7c08`。
- 最终 attempt：`attempts/20261012-095051-201-5edbd57a`。
- 实际首进程 PID=30456，exit=0，**33 项断言通过**；独立 restart 进程 PID=33608，exit=0，**8 项断言通过**。计有效覆盖 **41 项**，不累计重复 attempt。
- 布局：**32 项通过，0 项失败**。宿主根 Microsoft.Data.Sqlite.dll 和 e_sqlite3.dll 分别恰好一份；两个发布插件目录递归无 Microsoft.Data.Sqlite / SQLitePCLRaw / e_sqlite3 负载；manifest 身份与目录一致，四语言 JSON 可解析，发布根无平铺 manifest / i18n，Roslyn、PluginHost/Storage、开发 Skill 与 self-contained runtime 齐备。
- 发布原件构建前、运行后和临时运行副本：**464/464 个文件 SHA256 一致**；这包含产品 DLL、全部插件负载、deps、runtimeconfig 和随附 runtime。后加的两份候选状态文档不算产品负载。
- 两进程真正加载的 8 个关键托管程序集和 4 个 native 模块：**24/24 条模块来源与 Hash 核对通过**。LoomX、Harness、PluginHost、Abstractions、Storage、SQLite、Roslyn 与 hostfxr/hostpolicy/coreclr/native SQLite 均来自发布副本，不是源码构建产物。
- `runtime-proof.json` 证明：从真实 LoomX.deps.json 仅添加 `PublishedProbe/1.0.0` 入口，移除此入口后其余对象完全一致；探针 runtimeconfig 与真实发布 runtimeconfig 的 Hash 相同。host trace 明确记录 `Executing as a self-contained app` 和 app-local CoreCLR 路径。

## 闭环顺序

create → validate → 给本次插件源文件追加真实语法错误 → compile 返回失败并给出 CS0116 / src/Plugin.cs / line=10 / column=1 → diagnostics 返回失败诊断 → 修复本次插件 → compile 成功 → test 默认未授权拒绝 → test 显式 trust_code=true 成功（含 Pipeline 样例和自测试）→ 未安装 reload 拒绝 → 手动镜像本次生成包到隔离安装根 → 真实 Runtime 首次加载版本 1 → reload 切到版本 2 → 旧租约仍执行版本 1、新请求执行版本 2 → 安装包追加语法错误后 reload 拒绝并保留当前实例 → 恢复本次有效包 → 同一路径 SQLite 成功加载计数为 2。

第二独立进程使用同一本次状态根与插件 id：真实 Runtime 加载版本 2 → compile 返回 cache_hit=true → SQLite 原数据保留且初始化后计数为 3。没有把同进程重复创建 Runtime 当作 restart。

## SDK 与数据隔离的实际边界

- 复用旧 `sdkless-published-20261012-0645` 的 Program.cs / PublishedProbe.csproj / README 方法，只修改本次探针夹具。项目 HintPath 固定到最新发布真实 DLL；无 ProjectReference，无生产源码 Compile 引入。最后显式 Compile 只包含本次 Program.cs。
- 本机 SDK 只用于构建探针，构建 exit=0、0 警告、0 错误。实际运行不是 `dotnet run`，而是隐藏启动的 `PublishedProbe.exe` apphost + 发布 runtime 副本。
- 运行 PATH 为空（Windows 可表现为未定义变量），DOTNET_ROOT 和 DOTNET_ROOT_X64 指向副本，DOTNET_MULTILEVEL_LOOKUP=0、DOTNET_ROLL_FORWARD=Disable、DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX=0；移除额外 deps、shared store、startup hooks、host path 继承项；副本递归不存在 sdk 目录。探针进程也对关键环境进行运行时约束检查。
- **构建机物理上仍安装 SDK，不能声称是物理无 SDK 设备验收。** 这里证明的是实际执行进程采用 app-local self-contained 发布 runtime 的隔离运行方式。
- 最终插件唯一 id：`sdkless-extra-5a1f11703e6e444b9722858d264c3554`。
- 最终临时根：`C:\Users\BianShanghai\AppData\Local\Temp\LoomX-published-sdkless-extra-4218b3771f2e407b97ac034828cb56a4`，development / installed / cache 仅使用其独立子目录；app 为完整发布副本。
- 唯一真实持久化数据：`C:\Users\BianShanghai\AppData\Local\LoomX\plugins\sdkless-extra-5a1f11703e6e444b9722858d264c3554\probe.db`，由真实 AppDataPaths.RootDirectory/plugins/<id>/probe.db 与 PluginSqliteStorageBridge 创建，最终文件长度 8192 字节。该本次数据及各 attempt 临时根全部保留。
- 没有连接 Provider，没有加载发布包里的生产 Credential 插件，没有读取生产 Vault；真实配置库与活动库只 stat 长度/修改时间，在两个进程内均保持不变，没有打开或写入其内容。这个元数据检查不等于数据库内容 Hash 证明，更不等于恶意插件安全沙箱证明。
- 日志包括探针真实 stdout/stderr、工具事件/编译位置/计数、host trace 和加载模块记录；使用旧探针的 NullLoggerFactory，不声称已验收完整桌面 Serilog 日志接线。
- **未启动完整桌面主程序、未做 UI/CUA 或完整安装/启用流程验收。** 手动镜像仅本次插件包；不是桌面安装器或安装工具验收。

## 可重放命令

在指定 worktree 的 PowerShell 执行（Run 脚本每次创建新 attempt、新临时根和新插件 id，保留所有旧证据）：

```powershell
$e = 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08'
& "$e/Run-PublishedProbe.ps1"
```

脚本内真实构建命令等价于：

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build "$e/PublishedProbe.csproj" --configuration Release --output '<新 attempt>/probe-build' --verbosity minimal '-p:BaseIntermediateOutputPath=<新 attempt>/obj/'
```

真实执行命令行（必须通过 Run-PublishedProbe.ps1 设置环境、合并 deps 并使用 ProcessStartInfo.CreateNoWindow，不直接用当前 shell 环境启动）：

```text
"C:/Users/BianShanghai/AppData/Local/Temp/LoomX-published-sdkless-extra-4218b3771f2e407b97ac034828cb56a4/app/PublishedProbe.exe" "C:/Users/BianShanghai/AppData/Local/Temp/LoomX-published-sdkless-extra-4218b3771f2e407b97ac034828cb56a4" sdkless-extra-5a1f11703e6e444b9722858d264c3554
"C:/Users/BianShanghai/AppData/Local/Temp/LoomX-published-sdkless-extra-4218b3771f2e407b97ac034828cb56a4/app/PublishedProbe.exe" "C:/Users/BianShanghai/AppData/Local/Temp/LoomX-published-sdkless-extra-4218b3771f2e407b97ac034828cb56a4" sdkless-extra-5a1f11703e6e444b9722858d264c3554 restart
```

布局检查的首次命令为 `& "$e/Validate-PublishedLayout.ps1"`。为保证不覆盖已有报告，重复布局检查先创建唯一子目录并复制本次脚本：

```powershell
$replay = Join-Path $e ('layout-replay/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $replay | Out-Null
Copy-Item -LiteralPath "$e/Validate-PublishedLayout.ps1" -Destination $replay
& "$replay/Validate-PublishedLayout.ps1"
```

不要直接重放原最终 apphost 的 restart 命令来期望 Count 仍等于 3：每次真实加载会递增。应重放完整 Run 脚本以得到新唯一测试实例。脚本没有删除或清理命令。

## 原始证据索引

- `Program.cs`、`PublishedProbe.csproj`、`Run-PublishedProbe.ps1`、`Validate-PublishedLayout.ps1`：可重放探针及布局脚本。
- `plugin-layout-validation.json`：32 项详细布局结果，状态候选未放行。
- 最终 attempt 的 `BUILD.log`、`CLOSED-LOOP.log`、`RESTART.log`、两份 `*-HOSTTRACE.log`、`run-info.json`：实际构建、PID、命令参数、环境与退出码。
- 最终 attempt 的 `published-source-baseline.json`、`published-hashes.json`：464 文件 SHA256 三方对照。
- 最终 attempt 的 `closed-loop-loaded.json` / `restart-loaded.json`、`loaded-provenance.json`、`runtime-proof.json`：真实加载模块、24 条来源对照与 deps/runtime 自包含证明。
- `探针夹具调查.md`、`PublishedProbe.before-explicit-compile.csproj.txt`：本次两种夹具问题调查和修复前项目文件。
- 首次 attempt `20261012-094853-451-ecd6d2ce`：两进程 41 项通过，但长 trace 路径打不开，trace 回落到原始 stderr；记录全部保留。
- 第二 attempt `20261012-095014-715-b749cfc7`：build exit=1、8 个 CS0579，默认源码 glob 纳入旧 attempt 自动生成文件，属于重放构建夹具错误，未运行产品闭环。BUILD.log / FAILURE.log / run-info.json 和临时根全部保留。
- 最终 attempt 修复后通过。以上夹具问题**不计产品 RED**，有意注入语法错误与授权拒绝属于预期负向验收，不是遗留故障。

最新发布目录只追加 `plugin-layout-validation.json` 与 `验收状态.md`，两者均标记候选未放行；不覆盖任何已有产物、不删除/移动旧产物，不碰原 Runtime/Feedback 审查与全量测试状态。