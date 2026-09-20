# enhance-update-experience 集成验证报告

- 计划日期：2026-09-20
- 本机时钟记录：2026-09-21 07:09-07:41 +08:00（按任务要求用于实际证据目录与 `outputs/` 时间命名）
- 基线：`a081a665e8bc73252f52c55fe68af49506e2db96`
- 分支：`codex/merge-structured-config-assistant-decisions`
- 最终发布目录：`outputs/20260921-073944-enhance-update-experience/`
- 最终证据目录：`.superpowers/sdd/2026-09-20-enhance-update-experience/task-10-evidence/20260921-070917/`

## 1. 实施摘要

1. 新增仅在 `#if DEBUG` 编译的 `DebugUpdatePreviewService`，只接受 `LOOMX_UPDATE_PREVIEW=downloading|verifying|ready|error|history-empty`。
2. Debug 预览接线复用同一个 `IUpdateService` 实例给 `UpdateCoordinator` 与 `ReleaseHistoryViewModel`；有效预览值在配置就绪后主动触发一次检查。
3. Release 构建不包含预览类型/环境变量分支；最终发布包 `LoomX.dll` 的 UTF-8 字节扫描结果为 `RELEASE_CONTAINS_PREVIEW_ENV=False`。
4. CUA 暴露并修复了两个本 change 集成缺陷：
   - “更新”是第三个 Tab（索引 2），原实现错误地在索引 1（代理页）加载版本历史。
   - 标题栏入口只让内部文字宽度变化，实际按钮仍保持 32px，Hover 文案不可见；现显式从 32px 展开为 252px，窗口按钮仍在固定列中。

## 2. TDD 证据

### 2.1 Debug-only 预览接线

- RED：
  - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Debug更新预览仅支持固定场景且由编译条件隔离"`
  - 结果：1 个失败；`FileNotFoundException`，缺少 `LoomX/Services/DebugUpdatePreviewService.cs`。
- GREEN：同一命令通过 1/1。
- Debug 编译：`dotnet build LoomX/LoomX.csproj -c Debug --no-restore`，0 error；警告为既有 `NU1903`、`CS8618`、`CA2024`。

### 2.2 设置页更新 Tab 生命周期

- CUA RED：进入“更新”Tab 后列表为空，证据 `00-discovery-update-tab-before-fix.png`。
- 最小失败测试：把生命周期测试改为先进入索引 1，并断言历史请求数仍为 0；随后进入索引 2。
- RED 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~SettingsViewModelTabLifecycleTests"`
- RED 结果：期望 0、实际 1。
- GREEN：将 `SettingsViewModel.SelectedTabIndex` 的更新页条件从 `value != 1` 修正为 `value != 2`；同一测试通过 1/1。

### 2.3 标题栏 Hover 宽度

- CUA RED：真实指针悬停后入口 UIA 宽度仍为 32px，只有图标背景进入 Hover。
- 最小失败测试：要求 `Button.update-entry` 默认宽度 32px，`:pointerover` / `:focus` 宽度 252px。
- RED 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~MainWindowChromeContractTests.更新入口位于最小化按钮左侧并支持悬停与焦点展开"`
- RED 结果：缺少 `<Setter Property="Width" Value="32" />`。
- GREEN：增加按钮默认/悬停/焦点宽度；契约测试通过 1/1，CUA 实测 `BEFORE=32`、`AFTER=252`，见 `17-title-entry-hover-session.png`。

## 3. 测试进程挂起调查

首次按计划运行未带超时的大组合：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~UpdateCoordinatorTests|FullyQualifiedName~ReleaseHistoryViewModelTests|FullyQualifiedName~ReleaseNotesContentViewModelTests|FullyQualifiedName~ReleaseNotesViewContractTests|FullyQualifiedName~UpdateExperienceContractTests|FullyQualifiedName~MainWindowChromeContractTests|FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~Localization"
```

- PID 29740，子进程 36664（MSBuild）与 22508（vstest）约 5 分钟无进展。
- 主控制器在核对 CommandLine 后只终止上述三个 PID；未操作其他 dotnet/MSBuild 节点。
- 后续所有定向/完整测试均增加 `--blame-hang-timeout 60s`；定向测试拆为服务/ViewModel 与 UI/契约两个独立进程，不再启动无超时的大组合。

## 4. 自动化验证命令与准确结果

### 4.1 定向测试（最终代码）

1. 服务/ViewModel 组：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~UpdateCoordinatorTests|FullyQualifiedName~ReleaseHistoryViewModelTests|FullyQualifiedName~ReleaseNotesContentViewModelTests"
```

结果：50/50 PASS，约 3 秒；`NU1903`。

2. UI/契约/本地化组：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~ReleaseNotesViewContractTests|FullyQualifiedName~UpdateExperienceContractTests|FullyQualifiedName~MainWindowChromeContractTests|FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~Localization"
```

结果：47/47 PASS，约 1 秒；`NU1903`。并行启动时出现一次 `.msCoverageSourceRootsMapping_LoomX.Tests` 文件访问拒绝的自动重试提示，第二次尝试成功，测试进程最终成功。

3. 设置页生命周期代表类：1/1 PASS。
4. 标题栏 Hover 代表用例：1/1 PASS。

### 4.2 完整测试

最终命令：

```powershell
dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s
```

真实结果：1156 个测试中 1155 PASS、1 FAIL，约 1 分 52 秒。唯一失败：

- `GatewayViewModelDeletionTests.ComboDeletePreservesBoundComboAsSelectedMissingOption`
- 异常：`InvalidOperationException: Collection was modified; enumeration operation may not execute.`
- 位置：`GatewayViewModelDeletionTests.cs:158` 的等待条件枚举。
- 该类不属于更新体验改动文件；独立运行代表类：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~GatewayViewModelDeletionTests"
```

结果：8/8 PASS，约 13 秒。未修改无关测试基础设施，也未用反复重跑掩盖完整测试真实结果。

补充历史证据：在标题栏最终修复前的一次完整测试曾 1156/1156 PASS（约 1 分 57 秒）；最终报告仍以上述最终代码的 1155/1156 为准。

### 4.3 Release Build

```powershell
dotnet build LoomX.slnx -c Release --no-restore
```

结果：0 error、2 warning；两条均为 `NU1903`（`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 高严重性公告，分别来自 LoomX 与 LoomX.Tests）。

其他实际出现并保留记录的既有警告：

- `CS8618`：`SettingsViewModel.status` 构造结束时可能未初始化。
- `CA2024`：`AnthropicResponseMapper.cs` 两处异步方法使用 `reader.EndOfStream`。
- `CS8602`：`AnthropicRequestFactoryTests.cs` 两处可能空引用解引用。
- `NU1903`：上述 SQLite 原生包公告。

未宣称“无警告”。

### 4.4 OpenSpec 与 diff

```powershell
openspec validate enhance-update-experience --type change --strict --no-interactive
git diff --check
```

- OpenSpec：`Change 'enhance-update-experience' is valid`。
- `git diff --check`：无输出，退出码 0。

## 5. CUA 验证

- 工具：`cua-driver 0.28.2`，Windows UIA 正常。
- 所有截图来自 `get_window_state` 的 Loom-X 单窗口 PNG，不包含全屏。
- 每个点击、切换、选择、加载更多、重试、刷新与 Hover 操作前均重新 `get_window_state`，操作后再次 `get_window_state`。
- Debug 多实例使用项目既有 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1`，原因是无关 Session 的 Loom-X PID 30928 已持有 `Local\LoomX` 单实例互斥量；未终止该进程。
- 首次未设置多实例变量的预览 PID 5872 按单实例策略正常退出；该根因与处理记录在 `debug-processes.txt`。

### 5.1 证据索引

证据根目录：`.superpowers/sdd/2026-09-20-enhance-update-experience/task-10-evidence/20260921-070917/`

| 文件 | 结论 |
|---|---|
| `01-downloading-dialog.png` | 透明主题辅助截图；Markdown、54.0/128.0 MB、4.0 MB/秒、42%、稍后按钮可见。透明截图不用于单独判定配色。 |
| `05-light-transparency-off.png` | 浅色 + 关闭透明效果；下载浮窗文字、链接、进度与操作清晰。 |
| `06-dark-transparency-off.png` | 深色 + 关闭透明效果；内容与操作对比可读。 |
| `07-history-page1-badges.png` | 首次页、最新/当前徽标、默认选中最新、共享 Markdown。 |
| `08-history-load-more-selection.png` | 加载更多后仍选中 v0.12.6，正文未丢失；未因切换版本重复请求。 |
| `09-toast-no-overlap.png` | 普通 Toast“发现新版本 v9.9.0”位于底部，标题栏入口保留且未与更新浮窗重叠。 |
| `10-verifying-indeterminate.png` | 校验态保留 Markdown，显示非确定进度与“稍后”。 |
| `11-ready-actions.png` | Ready 仅显示“稍后 / 重启并安装”。未点击安装。 |
| `12-error-retry.png` | 更新准备错误摘要安全可读，提供“重试”。 |
| `13-history-error-no-cache.png` | 无缓存错误态安全可读并提供重试。 |
| `14-history-error-cached.png` | 刷新失败后版本列表与正文保留，同时显示安全错误摘要。 |
| `15-history-empty.png` | `history-empty` 显示“暂无正式版本”，刷新入口仍在。 |
| `17-title-entry-hover-session.png` | 浮窗关闭后入口仍在；真实 CUA 指针 Hover 后宽度从 32px 变为 252px，文案展开且未挤压最小化/最大化/关闭按钮。 |
| `18-final-release-smoke.png` | 最新发布包真实启动窗口；Release 正常运行并显示真实更新状态。 |
| `release-process.txt` | 最新发布进程 PID、期望路径与实际路径。 |
| `sensitive-log-scan.txt` | 敏感信息与预览正文扫描结果。 |

### 5.2 主题结论

- 浅色、深色均在关闭透明效果后截图确认，更新浮窗、标题栏入口、设置页和错误/空态可读。
- 透明主题只作为辅助观察，未据此单独判断真实配色。
- 标题栏默认宽度 32px；Hover 宽度 252px；系统窗口按钮固定在独立 42px 列，位置未被挤压。

## 6. 发布与进程路径

第一次发布目录 `outputs/20260921-072800-enhance-update-experience/` 在后续修复前生成，已按要求保留且未覆盖/删除，不作为最终产物引用。

最终发布命令：

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = "outputs/$stamp-enhance-update-experience"
if (Test-Path -LiteralPath $outputDir) { throw "发布目录已存在：$outputDir" }
./scripts/publish-desktop.ps1 -Configuration Release -OutputDirectory $outputDir
```

最终结果：

- 目录：`D:\AppData\Github\Loom-X - Copy\outputs\20260921-073944-enhance-update-experience`
- 唯一 exe：`LoomX.exe`（exe 数量 1）。
- `publish.log`：保留。
- 发布输出警告：`NU1903`、`CS8618`、`CA2024`；0 error。
- Release 预览分支扫描：`False`。

最终启动命令使用 PowerShell `Start-Process -FilePath <绝对 LoomX.exe>`；为与无关 Session 的单实例共存，仅设置项目既有 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1`，未设置 `LOOMX_UPDATE_PREVIEW`。

- 最终进程 PID：32612（验证后已关闭）。
- 期望路径：`D:\AppData\Github\Loom-X - Copy\outputs\20260921-073944-enhance-update-experience\LoomX.exe`
- `Win32_Process.ExecutablePath`：与期望路径完全相同。

## 7. 日志敏感信息检查

扫描本轮产生/更新的 3 个 Loom-X 日志文件，以下模式均为 0：

- 预览 Release Note 正文片段。
- `LOOMX_UPDATE_PREVIEW`。
- Debug 异常原始消息。
- `Authorization:`、`Bearer `、`sk-`。

业务日志只出现状态、版本、阶段和安全诊断摘要；没有记录请求/响应正文、用户 prompt、图片或工具参数。

## 8. Context7

按项目要求尝试列出 MCP resources 与 resource templates；当前会话两者均返回空列表，未配置可调用的 Context7 `resolve-library-id` / `query-docs` 工具。因此记录为工具不可用降级；本任务未依赖新的 Avalonia API，改动沿用仓库现有 AXAML、UIA 与测试模式。

## 9. Git 与多 Session 边界

- 保留且未修改/添加/删除/移动 `.agents/`、`.amazonq/`、`.claude/`、`.codebuddy/`、`.codex/`、`.devin/`、`.gemini/`、`.github/hooks/`、`.github/skills/`、`.kiro/`、`.qoder/`、`.qwen/`、`.trae-cn/`、`.trae/`。
- `outputs/` 与 `.superpowers/` 证据目录保持 ignored；旧输出目录均保留。
- 未 push。
- 最终 `git status --branch` 显示当前分支相对 `origin/master` 为 ahead 28、behind 1；本 Task 未执行 reset、clean、stash、强推或合并。

## 10. 结论与剩余风险

- 本 change 引入的预览接线、设置页 Tab 生命周期和标题栏 Hover 缺陷均已按 TDD 修复并有 CUA 证据。
- 定向测试、代表类、Release build、OpenSpec strict validate、diff check 与最终发布通过。
- 剩余风险：最终完整测试出现 1 个无关的并发枚举偶发失败；独立代表类 8/8 PASS，但完整测试的真实结果仍记录为 1155/1156，未掩盖。
- 既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 未在本 Task 扩大范围处理。


## 11. 提交后复核（2026-09-21 07:48-07:51 +08:00）

为最终回报重新执行同一份已提交源码，未修改生产代码、测试或发布包：

- 服务/ViewModel 定向组：50/50 PASS；`--blame-hang-timeout 60s`；出现既有 `NU1903`、`CS8618`、`CA2024`、`CS8602`。
- UI/契约/本地化定向组：47/47 PASS；`--blame-hang-timeout 60s`；出现既有 `NU1903`。
- 设置页生命周期与标题栏 Hover 代表测试组合：2/2 PASS；`--blame-hang-timeout 60s`；出现既有 `NU1903`。
- 完整测试：1156/1156 PASS，持续 1 分 51 秒；`--blame-hang-timeout 60s`。这次复核通过不删除上文最终验证曾出现的 1155/1156 偶发失败记录，二者共同证明该既有并发枚举失败具有非稳定性。
- Release build：0 error、2 个 `NU1903`。
- OpenSpec strict：`Change 'enhance-update-experience' is valid`。
- `git diff --check a081a665e8bc73252f52c55fe68af49506e2db96..HEAD`：无错误。
- 最终发布目录与实际进程路径保持 `outputs/20260921-073944-enhance-update-experience/`，因为本次仅复核且未修改源码；陈旧的 `outputs/20260921-072800-enhance-update-experience/` 仍保留。
