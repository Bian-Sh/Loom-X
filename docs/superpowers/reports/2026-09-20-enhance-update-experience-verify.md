# enhance-update-experience 集成验证报告

- 计划日期：2026-09-20
- 本机时钟记录：初始验证 2026-09-21 07:09-07:51 +08:00；Fix Round 1 2026-09-21 08:08-08:42 +08:00
- 基线：`a081a665e8bc73252f52c55fe68af49506e2db96`
- 分支：`codex/merge-structured-config-assistant-decisions`
- 最终发布目录：`outputs/20260921-083304-enhance-update-experience/`
- 最终有效补证目录：`.superpowers/sdd/2026-09-20-enhance-update-experience/task-10-evidence/fix-round-1-20260921-080800/`

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

首次发布前命令：

```powershell
dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s
```

首次发布前完整测试真实结果：1156 个测试中 1155 PASS、1 FAIL，约 1 分 52 秒。唯一失败：

- `GatewayViewModelDeletionTests.ComboDeletePreservesBoundComboAsSelectedMissingOption`
- 异常：`InvalidOperationException: Collection was modified; enumeration operation may not execute.`
- 位置：`GatewayViewModelDeletionTests.cs:158` 的等待条件枚举。
- 该类不属于更新体验改动文件；独立运行代表类：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~GatewayViewModelDeletionTests"
```

结果：8/8 PASS，约 13 秒。未修改无关测试基础设施，也未用反复重跑掩盖完整测试真实结果。

补充历史证据：在标题栏最终修复前的一次完整测试曾 1156/1156 PASS（约 1 分 57 秒）；首次发布前记录保留上述 1155/1156；后续结果按各阶段分别记录。

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
| `06-dark-transparency-off.png` | **无效历史证据**：与旧 `05-light-transparency-off.png` 字节数及 SHA-256 完全相同，不再支持深色结论；文件保留未删除。 |
| `07-history-page1-badges.png` | 首次页、最新/当前徽标、默认选中最新、共享 Markdown。 |
| `08-history-load-more-selection.png` | 加载更多后仍选中 v0.12.6，正文未丢失；未因切换版本重复请求。 |
| `09-toast-no-overlap.png` | 历史截图未同时显示 Toast 与更新浮窗，不能证明二者不重叠；文件保留但不作为 Finding 2 证据。 |
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

- 原始旧 `06` 证据无效；Fix Round 1 已用新 `15`/`16` 在关闭透明效果后分别确认浅色与真实深色，且 SHA-256 不同。
- 透明主题只作为辅助观察，未据此单独判断真实配色。
- 标题栏默认宽度 32px；Hover 宽度 252px；系统窗口按钮固定在独立 42px 列，位置未被挤压。

## 6. Task 10 首轮发布与进程路径（Fix Round 1 后已被替代）

第一次发布目录 `outputs/20260921-072800-enhance-update-experience/` 在后续修复前生成，已按要求保留且未覆盖/删除，不作为最终产物引用。

首轮第二次发布命令：

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = "outputs/$stamp-enhance-update-experience"
if (Test-Path -LiteralPath $outputDir) { throw "发布目录已存在：$outputDir" }
./scripts/publish-desktop.ps1 -Configuration Release -OutputDirectory $outputDir
```

首轮第二次发布结果：

- 当时目录：`D:\AppData\Github\Loom-X - Copy\outputs\20260921-073944-enhance-update-experience`（Fix Round 1 后已陈旧，保留但不作为最终包）
- 唯一 exe：`LoomX.exe`（exe 数量 1）。
- `publish.log`：保留。
- 发布输出警告：`NU1903`、`CS8618`、`CA2024`；0 error。
- Release 预览分支扫描：`False`。

当时启动命令使用 PowerShell `Start-Process -FilePath <绝对 LoomX.exe>`；为与无关 Session 的单实例共存，仅设置项目既有 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1`，未设置 `LOOMX_UPDATE_PREVIEW`。

- 当时进程 PID：32612（验证后已关闭）。
- 当时期望路径：`D:\AppData\Github\Loom-X - Copy\outputs\20260921-073944-enhance-update-experience\LoomX.exe`
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
- 历史风险：首次发布前完整测试出现 1 个无关并发枚举偶发失败；独立代表类 8/8 PASS。提交后最终复核为 1156/1156，Fix Round 1 最终源码复核为 1157/1157；三次真实结果均保留。
- 既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 未在本 Task 扩大范围处理。


## 11. 提交后最终复核（2026-09-21 07:48-07:51 +08:00）

提交后最终复核重新执行同一份已提交源码，未修改生产代码、测试或发布包：

- 服务/ViewModel 定向组：50/50 PASS；`--blame-hang-timeout 60s`；出现既有 `NU1903`、`CS8618`、`CA2024`、`CS8602`。
- UI/契约/本地化定向组：47/47 PASS；`--blame-hang-timeout 60s`；出现既有 `NU1903`。
- 设置页生命周期与标题栏 Hover 代表测试组合：2/2 PASS；`--blame-hang-timeout 60s`；出现既有 `NU1903`。
- 完整测试：1156/1156 PASS，持续 1 分 51 秒；`--blame-hang-timeout 60s`。这次复核通过不删除上文首次发布前曾出现的 1155/1156 偶发失败记录，二者共同证明该既有并发枚举失败具有非稳定性。
- Release build：0 error、2 个 `NU1903`。
- OpenSpec strict：`Change 'enhance-update-experience' is valid`。
- `git diff --check a081a665e8bc73252f52c55fe68af49506e2db96..HEAD`：无错误。
- 当次提交后复核未修改源码，当时发布目录为 `outputs/20260921-073944-enhance-update-experience/`；Fix Round 1 后该包与 `072800` 均为保留的陈旧包，最终引用 `083304`。


## 12. Fix Round 1/5（2026-09-21 08:08-08:42 +08:00）

### 12.1 Finding 1：真实深色 + 关闭透明

- 旧证据核验：
  - `05-light-transparency-off.png`：56,351 字节，SHA-256 `48B9197354584C0AAB0074CAEA7C28175C15CC3FA83FA56C4DDDE8CD640A500C`。
  - `06-dark-transparency-off.png`：56,351 字节，SHA-256 同为 `48B9197354584C0AAB0074CAEA7C28175C15CC3FA83FA56C4DDDE8CD640A500C`。
  - 结论：旧 `06` 为无效历史证据，保留但不再支持 OpenSpec 6.3/6.5。
- 根因：`SettingsViewModel.SelectedTheme` 只保存配置，没有应用 Avalonia `RequestedThemeVariant`；`VisualTokens.axaml` 也只有固定浅色资源。
- TDD：主题契约先因缺少 `applyTheme: mainWindow.ApplyTheme` 1 FAIL；最小接线后，主题 + Toast 聚焦组合 2/2 PASS。
- 修复：主题选择即时应用 `ThemeVariant.Light/Dark/Default`，配置外部刷新同步应用；视觉令牌拆分 Light/Dark ThemeDictionary；切换主题时刷新透明外观资源缓存。
- 新 CUA 证据（action 前后均 `get_window_state`，单窗口截图）：
  - `15-valid-light-after-theme-fix.png/json`：浅色、透明关闭；82,845 字节；SHA-256 `DE8262498254D53CD04FEB75FCF5D9EF0CFFDF9423E57AB30152635BBF58265D`。
  - `16-valid-dark-transparency-off.png/json`：深色、透明关闭；83,100 字节；SHA-256 `B052CDEAEA6FB37F2689A497C3FF0483C0E1514E3BCAAB2B2C51A3615EDDEF5A`。
  - 新浅色/深色 SHA-256 不同，UIA ComboBox 值与视觉结果均确认主题已经改变。透明主题截图仍只作辅助，不单独判定真实配色。

### 12.2 Finding 2：Toast 显式高于 Overlay 且不覆盖主体

- RED：先修改 `UpdateExperienceContractTests`，删除“Toast 位于浮窗后方”的错误断言，显式要求 Toast ZIndex=2、Overlay ZIndex=1、Toast 位于 228px 侧栏且最大宽度 196px；旧实现 1 FAIL，缺少 Toast `Panel.ZIndex`。
- GREEN：`MainWindow.axaml` 使用 Style Setter 明确 `Panel.ZIndex`；Toast 左下侧栏显示并允许换行，更新浮窗主体放在第二列。直接在元素上设置附加属性曾触发 Avalonia `AVLN3000`，因此采用仓库可编译的 Style Setter 模式。
- CUA：`19-valid-dark-toast-dialog-no-overlap.png/json` 同时显示 Toast“发现新版本 v9.9.0”与更新浮窗，Toast 在左下侧栏、浮窗主体在右侧内容列，空间不重叠；133,856 字节；SHA-256 `1283A930A53E9B371A43387EAC904F8443773C2C8669560FA6CADFB8669E66CC`。

### 12.3 Finding 3：完整测试阶段名称

- **首次发布前完整测试**：1155/1156；唯一失败为无关 `GatewayViewModelDeletionTests` 并发枚举偶发异常，独立代表类 8/8 PASS。
- **提交后最终复核**：1156/1156 PASS。
- **Fix Round 1 最终源码复核**：1157/1157 PASS，1 分 54 秒。三次结果均保留，不再把前两次同时称为“最终”。

### 12.4 Fix Round 1 命令与结果

- 聚焦主题 + Toast：2/2 PASS；`--blame-hang-timeout 60s`；2 个既有 `NU1903`。
- UI/契约/本地化：48/48 PASS；`--blame-hang-timeout 60s`；2 个既有 `NU1903`。
- 服务/更新 ViewModel：50/50 PASS；`--blame-hang-timeout 60s`；2 个既有 `NU1903`。
- `WindowAppearanceCoordinatorTests`：6/6 PASS；`AssistantDecisionLifecycleTests` 独立：2/2 PASS。
- 挂起证据：命令 `dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~SettingsViewModel|FullyQualifiedName~MainWindowViewModel|FullyQualifiedName~WindowAppearanceCoordinatorTests"` 在运行 1 个测试后触发 60 秒 blame hang；当时运行 `AssistantDecisionLifecycleTests.MainWindowViewModel_Dispose幂等释放Assistant并收敛已Claim请求`。未反复重跑大组合，拆分后代表类 2/2 PASS。
- 完整测试：`dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s`，1157/1157 PASS，1 分 54 秒。
- Release build：`dotnet build LoomX.slnx -c Release --no-restore`，0 error、2 warning，均为 `NU1903`。发布阶段另有既有 `CS8618`、`CA2024`；定向编译曾出现既有 `CS8602`，未宣称无警告。

### 12.5 最新发布、进程与日志

- 发布命令：`scripts/publish-desktop.ps1`，全新目录 `D:\AppData\Github\Loom-X - Copy\outputs\20260921-083304-enhance-update-experience`。旧 `072800`、`073944` 目录均保留且未覆盖/删除。
- 完整性：exe 数量 1，唯一入口 `LoomX.exe`；`publish.log` 存在；发布文件扫描 `LOOMX_UPDATE_PREVIEW` 命中 0。
- 按约定通过 PowerShell `Start-Process -FilePath <绝对路径>` 启动 PID 46716；`Win32_Process.ExecutablePath` 与 `D:\AppData\Github\Loom-X - Copy\outputs\20260921-083304-enhance-update-experience\LoomX.exe` 精确相等（`match=True`）；验证后只关闭本轮 PID 46716。
- `20-final-release-smoke.png/json`：最新 Release 包实际启动单窗口证据；`release-process.txt` 保存路径核验。
- 日志：扫描 `C:\Users\BianShanghai\AppData\Local\LoomX\logs\loomx-20260921_005.log`，预览正文、`LOOMX_UPDATE_PREVIEW`、Debug 原始异常、`Authorization:`、`Bearer `、`sk-` 均 0 命中；结果见 `sensitive-log-scan.txt`。
- Context7：MCP resources 与 resource templates 均为空，无法调用 `resolve-library-id` / `query-docs`；降级查阅 Avalonia 官方主题文档，确认 `RequestedThemeVariant` 和 Light/Dark ThemeDictionaries 用法。

### 12.6 OpenSpec 与边界

- 旧深色/Toast 证据被判无效时，6.3/6.5 不作为完成证据；新 `15`/`16`/`19`、最新发布和路径核验齐全后重新确认 6.3/6.5 为完成。
- 未修改、添加、提交、删除、移动或清理用户列出的 Comet 激活未跟踪目录；未 push；未运行 Comet build guard、verify 或 archive。


## 13. Final Review Fix Wave（2026-09-21 09:18-09:31 +08:00）

### 13.1 两个 Important 的最小修复

1. **Ready 后安装器身份复验**：`PreparedUpdate` 现在携带准备阶段确认的 SHA-256 与安装器长度。`UpdateService.LaunchInstaller` 在调用 launcher 前重新打开文件，先核对长度，再将十六进制摘要解析为字节并使用 `CryptographicOperations.FixedTimeEquals` 比较；摘要大小写不会影响比较。复验失败会删除当前安装器与配套 `.sha256` 缓存，抛出不含路径、摘要或文件内容的 `InvalidPreparedUpdateException`。`UpdateCoordinator` 同时在安装前核对 `PreparedUpdate.Version` 与当前 `Release.Version`；版本不一致或准备产物失效时清空 `PreparedUpdate`，进入 `Prepare` 错误态，必须重新 Prepare，不能在同一个失效对象上反复点击。
2. **安装一次性闩锁**：安装流程拆分为“启动安装器”和“请求应用退出”两个异常边界。只有 `LaunchInstaller` 自身失败时清除 `installStarted`；普通 launcher 启动失败仍可安全重试，失效准备产物则要求重新 Prepare。一旦 launcher 成功，闩锁在协调器生命周期内永久保持；即使 `requestApplicationExit` 抛异常，也只进入不可再次安装的 `Exit` 错误态，`CanInstall` 与安装命令保持关闭。新增 `update.error.exit` 四语言资源，提示用户安装器已启动并需手动关闭应用。日志只记录版本、异常类型、HResult、HTTP 状态和阶段等安全摘要。

### 13.2 RED → GREEN 证据

- Service RED：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests.LaunchInstaller_准备后安装器被替换时拒绝启动并清理当前缓存" --blame-hang-timeout 60s`。第一次与协调器 RED 并行启动时发生既有输出文件竞争，`LoomX.Harness.dll` 被另一 `VBCSCompiler` 占用（CS2012），该次不作为行为 RED；串行重跑得到 0/1，通过数 0，失败原因为旧实现未抛异常并实际调用 launcher（`No exception was thrown`）。
- Coordinator RED：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateCoordinatorTests.退出回调失败后不会再次启动安装器且安装命令保持关闭|FullyQualifiedName~UpdateCoordinatorTests.准备版本与当前发布版本不一致时拒绝启动并要求重新准备" --blame-hang-timeout 60s`。结果 0/2；退出失败被旧 catch 回滚到 Ready，版本不一致仍进入 Installing，两个用例均等待目标 Error 状态超时。
- Service GREEN：同一聚焦回归用例 1/1 PASS；launcher 调用为 0，安装器与校验缓存均被清理。
- Coordinator GREEN：同一双用例 2/2 PASS；退出回调失败后 `LaunchCalls == 1`、`CanInstall == false`、`CanRetry == false`，版本不一致时 `LaunchCalls == 0` 且清空准备产物。
- 正常路径保持：既有“安装器启动失败不会退出并恢复就绪”用例扩展为清除 launcher 异常后再次执行，确认第二次 launcher 成功且只请求一次退出。

### 13.3 最终自动化验证

- 聚焦组 1：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests" --blame-hang-timeout 60s` → 12/12 PASS。
- 聚焦组 2：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateCoordinatorTests" --blame-hang-timeout 60s` → 13/13 PASS。
- 更新域联合：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --blame-hang-timeout 60s --filter "FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~UpdateCoordinatorTests|FullyQualifiedName~ReleaseHistoryViewModelTests|FullyQualifiedName~ReleaseNotesContentViewModelTests"` → 53/53 PASS。
- 完整测试首次尝试：1159/1160 PASS；唯一失败为 `LocalizationNoCjkTest.Phase2LocalizedViewsAndViewModelsContainNoHardcodedCjk`，根因是新增日志参数续行上的中文回退值 `"无"` 未位于 logger 调用行。改为安全英文摘要 `"none"` 后，聚焦本地化契约 1/1 PASS。
- 完整测试最终：`dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s` → 1160/1160 PASS，持续 1 分 52 秒。
- Release build：`dotnet build LoomX.slnx -c Release --no-restore` → 0 error、2 个 `NU1903`。
- OpenSpec：`openspec validate enhance-update-experience --strict` → `Change 'enhance-update-experience' is valid`。
- change scoped diff check：`git diff --check fd182a9467dbdcefcbc216e8cccc0e85455d453c --` → 无输出、退出码 0。

### 13.4 发布与进程路径核验

- 新发布目录：`outputs/20260921-092945-enhance-update-experience/`，未覆盖或删除任何旧输出。
- 发布目录递归检查只有一个 exe：`LoomX.exe`；`publish.log` 存在；对发布产物扫描 `LOOMX_UPDATE_PREVIEW` 命中 0。
- 使用 PowerShell `Start-Process -FilePath <绝对 LoomX.exe> -WindowStyle Hidden -PassThru` 启动，设置项目既有 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1`，显式清除本轮进程环境中的 `LOOMX_UPDATE_PREVIEW`。
- 本轮 PID 42840；`Win32_Process.ExecutablePath` 精确等于 `D:\AppData\Github\Loom-X - Copy\outputs\20260921-092945-enhance-update-experience\LoomX.exe`。验证后仅终止 PID 42840，未操作受保护的其他 Session PID 30928。

### 13.5 边界、警告与剩余风险

- 未修改 UI 布局或主题；用户可见变化仅为 `update.error.exit` 资源键及四语言文案契约，因此未重复整套 CUA 截图。
- 未添加、修改、删除、移动或清理用户要求保留的 Comet 激活未跟踪目录；未提交 `outputs/` 或证据目录；未 push；未运行 Comet guard/verify/archive。
- 既有警告如实保留：`NU1903`（SQLitePCLRaw 已知高严重性漏洞）、`CS8618`（SettingsViewModel.status）、`CA2024`（AnthropicResponseMapper）、`CS8602`（AnthropicRequestFactoryTests）。最终增量 Release build 只打印 2 个 `NU1903`；聚焦测试重新编译与发布阶段仍可见其余既有警告。
- 剩余固有风险：路径式 `Process.Start` 无法提供从哈希完成到操作系统打开可执行文件之间的完全原子绑定；当前实现通过独占写/删除共享限制下读取文件、紧邻启动前固定时间摘要复验及失败后清缓存，将可控窗口压缩到最小。

## 14. Comet Full Verify（2026-09-21 09:59 +08:00）

### 14.1 验证记分卡

| 检查项 | 结果 | 证据 |
|---|---|---|
| 1. `tasks.md` 全部完成 | PASS | 22/22，OpenSpec apply instructions 返回 `all_done` |
| 2. OpenSpec `design.md` 高层决策 | PASS | 自动准备与用户确认启动边界、共享协调器和 Release History 分离均已实现 |
| 3. Superpowers Design Doc 一致性 | **FAIL（IMPORTANT）** | 设计要求“安装器已经启动但退出失败时不重复启动安装器”；成功日志仍位于可释放 `installStarted` 的通用异常边界内 |
| 4. 能力规格与场景覆盖 | PASS（含下述实现边界缺陷） | Release 分页、Markdown、进度、Ready 确认、设置页历史与代理/校验场景均有实现和测试；安装一次性安全不变量仍有 1 项 Important |
| 5. `proposal.md` 目标 | PASS | 低打扰入口、浮窗直显 Release Note、自动准备但确认后安装、设置页多版本浏览均已交付 |
| 6. Delta spec 与 Design Doc 无矛盾 | PASS | 未发现文档间漂移；失败来自实现异常边界未完全满足设计 |
| 7. 关联设计文档可定位 | PASS | `docs/superpowers/specs/2026-09-20-enhance-update-experience-design.md` 存在且已核对 |

### 14.2 独立运行证据

- `comet check run enhance-update-experience verify --local -- dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s`：1160/1160 PASS，0 skipped，持续 1 分 51 秒；日志 `openspec/changes/enhance-update-experience/.comet/checks/88842210-fcbc-4ca6-bcc1-ab489de3b794.log`。
- `comet classic openspec -- validate enhance-update-experience --strict`：`Change 'enhance-update-experience' is valid`。
- Build 阶段独立 Runtime build：0 error；保留既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 警告，不宣称零警告。

### 14.3 IMPORTANT：launcher 成功后的日志异常仍可能释放闩锁

- `UpdateService.LaunchInstaller` 在 `installerLauncher.Launch(...)` 成功返回后继续记录成功日志；该日志异常会向调用方传播。
- `UpdateCoordinator.InstallAndRestartAsync` 将 `updateService.LaunchInstaller(target)` 与协调器成功日志放在同一个通用 `try/catch` 中；任一成功后日志异常都会进入“启动失败”分支，执行 `installStarted = 0` 并恢复 `Ready`。
- 结果是安装器可能已经启动，但安装按钮重新开放，违反 Design Doc 的一次性安装命令与“不重复启动安装器”要求。
- 现有 1160 个测试仅覆盖 launcher 直接失败和退出回调失败，未覆盖“launcher 已成功、后续日志抛异常”。

**推荐处理：** 回到 Build，以 TDD 增加抛异常 Logger 场景，并将 launcher 成功边界与非关键成功日志彻底分离；只有 launcher 本身失败时才允许释放闩锁。完成后重跑协调器/服务聚焦测试、更新域联合测试、完整测试、Release build 和 fresh scoped review。

### 14.4 本轮结论

**FAIL：1 个不可豁免的 IMPORTANT。** 自动化测试与 OpenSpec strict 均通过，但实现尚未完全满足安装一次性安全边界；本轮不得执行 verify guard、不得进入 archive。

## 15. Verify Failure Fix：安装器启动后日志异常边界（2026-09-21 10:10 +08:00）

### 15.1 RED 证据

- Service 聚焦用例：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests.LaunchInstaller_安装器启动后的成功日志失败不影响启动结果" --logger "console;verbosity=minimal"` → 0/1，`UpdateService.LaunchInstaller` 在 launcher 已成功后把 `ILogger.LogInformation` 的 `InvalidOperationException` 向上抛出。
- Coordinator 聚焦用例：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateCoordinatorTests.安装器启动后的成功日志失败仍请求退出且不会重新开放安装命令" --logger "console;verbosity=minimal"` → 0/1，第二次执行安装命令后 `LaunchCalls` 实际为 2，证明旧异常边界会释放一次性闩锁。

### 15.2 最小实现

- `UpdateService.LaunchInstaller` 保留启动前长度与 SHA-256 身份复验；只有 `installerLauncher.Launch(...)` 成功返回后，成功日志才进入独立 best-effort 边界，日志异常不再向上改变启动结果。
- `UpdateCoordinator.InstallAndRestartAsync` 的 launcher 异常边界只包围 `updateService.LaunchInstaller(target)`；成功日志移到边界外并按 best-effort 处理，随后仍请求应用退出。普通 launcher 失败仍释放闩锁并回到 `Ready`，`InvalidPreparedUpdateException`、退出回调失败后的永久闩锁保持原行为。

### 15.3 GREEN 与回归

- 两个新增聚焦用例串行重跑：各 1/1 PASS。第一次并行 GREEN 尝试发生 `LoomX.dll` 输出文件被另一 `VBCSCompiler` 占用的 `CS2012` 竞争；协调器用例通过，Service 用例随后串行独立重跑通过，该竞争不属于产品行为失败。
- `UpdateServiceTests`：13/13 PASS。
- `UpdateCoordinatorTests`：14/14 PASS。
- 更新域联合：`UpdateServiceTests|UpdateCoordinatorTests|ReleaseHistoryViewModelTests|ReleaseNotesContentViewModelTests` → 55/55 PASS。
- 完整测试：`dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s` → 1162/1162 PASS，0 skipped，持续 1 分 54 秒。
- Release build：`dotnet build LoomX.slnx -c Release --no-restore --nologo` → 0 error、2 个 `NU1903`。

### 15.4 已知警告与结论

- 最终 Release build 仍保留既有 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 存在已知高严重性漏洞；本次修复不扩大依赖范围，也不宣称零警告。
- 聚焦测试触发重新编译时仍可见既有 `CS8618`、`CA2024`、`CS8602`；本次未修改对应代码。
- 本次只修复 final scoped re-review 指出的日志异常安全边界，未修改 UI、OpenSpec tasks/spec/design、Comet 状态、发布输出或证据 ledger。

## 16. Comet Full Verify 最终复核（2026-09-21 10:19-10:21 +08:00）

### 16.1 先前 IMPORTANT 的关闭证据

- 修复提交：`fd8f8e6`（修复安装器启动后的日志异常边界）。
- Fresh scoped re-review：原开放 Important 在 UpdateService 与 UpdateCoordinator 两层均为 ADDRESSED；独立聚焦 6/6 PASS，`git diff --check 681ac9f..fd8f8e6` PASS；无新增 Critical/Important。
- 可接受 Minor：成功后的 best-effort 日志若失败，不会留下第二条诊断日志；作用域仅限 launcher 已成功后的非关键成功日志，不会吞掉校验、launcher 或退出回调异常。

### 16.2 最终 Runtime 验证

- Build：`comet check run enhance-update-experience build --local -- dotnet build LoomX.slnx -c Release --no-restore` → 0 error；Runtime 日志 `openspec/changes/enhance-update-experience/.comet/checks/6032b4e9-5837-431c-ab8d-e4530367cd49.log`。
- Verify：`comet check run enhance-update-experience verify --local -- dotnet test LoomX.slnx -c Release --no-restore --blame-hang-timeout 60s` → 1162/1162 PASS，0 skipped；Runtime 日志 `openspec/changes/enhance-update-experience/.comet/checks/fa80d487-2372-4656-82d7-a53e46690687.log`。
- OpenSpec strict：最终重新执行 `comet classic openspec -- validate enhance-update-experience --strict`。
- 已知警告继续如实保留：`NU1903`；重新编译/发布时可见既有 `CS8618`、`CA2024`，测试编译还可能出现既有 `CS8602`。

### 16.3 最终发布与进程路径

- 新发布目录：`outputs/20260921-101946-enhance-update-experience/`；未覆盖或删除旧输出。
- 递归检查唯一 exe 为 `LoomX.exe`，`publish.log` 存在，发布产物扫描 `LOOMX_UPDATE_PREVIEW` 为 0 命中。
- 使用 `Start-Process -FilePath <最新发布 LoomX.exe> -WindowStyle Hidden -PassThru` 启动 PID 49588；`Win32_Process.ExecutablePath` 与最新发布 exe 精确一致。验证后只终止 PID 49588。

### 16.4 最终结论

**PASS。** 22/22 任务完成；proposal、delta spec、OpenSpec design 与 Superpowers Design Doc 均可定位且实现一致；原不可豁免 Important 已由 TDD 修复并经 fresh scoped re-review 关闭；完整测试、Release build、OpenSpec strict、发布完整性及进程路径均通过。可以进入 Archive 确认阶段。

## 17. 更新说明与安装确认补充验收（2026-09-21 17:45-18:28 +08:00）

### 17.1 实现结论

- Release Notes 遮罩固定为纯黑 `#A6000000`，不受透明主题协调器修改；弹窗相对完整主窗口居中，并使用独立 `ReleaseNotesAcrylicMaterial`。
- 弹窗调整为 `680 × 540` 的紧凑单栏布局，移除大图标与多余留白；顶部保留版本、发布日期、状态和“前往发布页”，底部按下载、校验、Ready、失败状态显示紧凑操作区。
- `LiveMarkdown.Avalonia 1.12.2` 不支持 HTML，因此不能直接渲染 GitHub `<details open>`；安全策略也会移除 HTML。三个模块以原生 `Expander` 承载折叠行为，但 Header 和正文均由 `MarkdownRenderer` 渲染，默认全部展开、折叠状态相互独立，旧格式正文继续使用单 Markdown 回退。
- Ready 逻辑保持原行为：若 Release Notes 正在展示，准备完成后弹窗继续显示并出现“重启并安装”；点击“稍后”后标题栏入口显示“安装”；点击该入口直接进入通用应用内安装风险确认，不重新打开 Release Notes。未增加超时、自动收起或“跳过版本”状态。
- 通用 `AppModalHost` 使用固定 65% 黑色遮罩、完整窗口居中和 FIFO 请求队列；安装确认明确告知应用关闭重启、路由服务短暂中断及进行中请求可能失败。取消后保持 Ready 且不启动安装器。
- Inno Setup 桌面快捷方式改为无条件创建 `{autodesktop}\LoomX`。

### 17.2 TDD 与自动化验证

- Release Notes 分段、默认展开、独立折叠、Markdown 标题及旧正文回退均有 ViewModel/视图契约测试。
- Ready 标题栏入口直接确认、取消保持 Ready、重复确认闩锁、应用内模态队列和安装器脚本均有自动化测试。
- 完整测试：`dotnet test LoomX.slnx -c Release --no-restore` → **1181/1181 PASS**，0 skipped。
- Release 构建：`dotnet build LoomX.slnx -c Release --no-restore` → **0 error**；保留既有 `NU1903`。Debug/发布过程中仍可能显示既有 `CS8618`、`CA2024` 和测试项目 `CS8602`。

### 17.3 CUA 实机验收

- 非透明模式：后方 UI 在 `#A6000000` 遮罩下仍可辨认；弹窗位于整个 APP 中央。
- 透明模式：独立磨砂内容区可读；透明主题截图仅作辅助，没有据此判断真实主题色。
- 三个 Markdown 标题 Foldout 初始均展开；折叠第一个模块后，其余两个保持展开，折叠标题仍横向铺满。
- Ready 预览中 Release Notes 未自动关闭，底部同时显示“稍后”和“重启并安装”。
- 点击“稍后”后标题栏显示“安装”；再次点击只显示安装风险确认，正文包含应用关闭重启、路由服务短暂中断和进行中请求可能失败；点击“暂不安装”后确认框关闭、Ready 入口仍在。
- 截图：`release-notes-compact-downloading.png`、`release-notes-compact-transparent.png`、`release-notes-compact-ready.png`。

### 17.4 发布与安装器

- 发布目录：`outputs/20260921-174531-enhance-update-experience-followup/publish/`，407 个文件，`LoomX.exe` 存在。
- 安装器：`outputs/20260921-174531-enhance-update-experience-followup/installer/LoomX-0.12.7-setup.exe`，大小 49,212,277 字节。
- 发布包使用 `Start-Process` 启动后，进程实际路径与上述 `publish/LoomX.exe` 精确一致；验证完成后仅终止本次启动的 PID。
- `installer/LoomX.iss` 已通过契约测试和脚本检查，桌面快捷方式不再依赖默认未勾选任务。

### 17.5 结论

**PASS。** 补充需求实现、自动化测试、透明/非透明 CUA 验收、发布目录、安装器生成和发布包进程路径均已验证。


## 18. 合并远端后的最终验证（2026-09-21 18:45 +08:00）

### 18.1 OpenSpec 完整性与一致性

| 维度 | 结果 |
|---|---|
| 完整性 | 31/31 tasks 完成；9/9 requirements 有实现与测试证据 |
| 正确性 | 24/24 scenarios 已由自动化测试、契约测试或 CUA 验收覆盖 |
| 一致性 | 实现符合 OpenSpec design 与 Superpowers Design Doc；未发现新的 Critical、Warning 或规格漂移 |

- `comet state check enhance-update-experience verify --json`：4/4 入口检查通过，`verify_mode=full`。
- `comet classic openspec -- validate enhance-update-experience --strict`：PASS。
- 远端 `origin/master` 的提交 `5dce5ff` 已通过普通 merge 纳入当前分支；该提交只调整助手会话 JSONL 中文保存及其测试，与更新体验实现无冲突。

### 18.2 合并后 Runtime 证据

- Comet Verify：`comet check run enhance-update-experience verify --local --json -- dotnet test LoomX.slnx -c Release --no-restore` → **1182/1182 PASS**，0 skipped；证据日志：`openspec/changes/enhance-update-experience/.comet/checks/9c8f869b-c1ca-4404-8333-23424c85af92.log`。
- Release build：`dotnet build LoomX.slnx -c Release --no-restore` → **0 error**，2 个既有 `NU1903`。
- 测试重新编译仍可见既有 `CS8618`、`CA2024`、`CS8602`；未发现本次改动新增的编译错误。

### 18.3 用户设置与进程边界

- 使用本次发布包以 `--allow-multiple-instances` 启动 PID 29044，进程实际路径与 `outputs/20260921-174531-enhance-update-experience-followup/publish/LoomX.exe` 精确一致。
- 通过 CUA 将用户原有“透明窗口”设置恢复为 Off，并在重快照中确认 toggle 的 `selected=false`。
- 仅关闭本次启动的 PID 29044；其他 LoomX 进程 PID 35328 保持运行，未操作其他会话进程。

### 18.4 最终结论

**PASS。** 合并远端后完整测试、Release build、OpenSpec strict、规格映射与用户设置恢复均已重新验证；当前 change 可推进到 Archive 确认阶段。


## 19. Release Notes 视觉返工（2026-09-21 19:26 +08:00）

### 19.1 根因与调整

- 原实现把 Markdown 二级标题直接放入 Fluent 默认 Expander Header，但只清理了 Expander 根属性，没有覆盖模板内部 `ExpanderHeader`、`ToggleButtonBackground` 和 `ExpanderContent`，导致标题白条、卡片边框和正文底色叠加。
- 原 `FontSize2Xl` 与 Markdown Heading2 默认间距造成标题过大；固定 `680 × 540` 又把少量正文强行拉成安装向导式大空白。
- 本轮为共享 Markdown 视图增加局部紧凑排版资源和模板内样式：标题 16px、正文 13px、连续分隔线、透明内容面、轻量 Hover；Header 和正文仍由 `MarkdownRenderer` 渲染，未增加第二套 Foldout 组件。
- 更新浮窗改为 `660` 宽、仅设置 `MaxHeight=560`，正文区域 `150–350` 高度内自适应；“前往发布页”和“稍后”改为低视觉权重按钮，进度条降为 4px。
- Acrylic 从 `TintOpacity=0.90 / MaterialOpacity=0.96` 调整为 `0.56 / 0.78`，并保留高不透明度实色回退。Debug Release 正文不再显示 `LOOMX_UPDATE_PREVIEW` 测试标记。

### 19.2 TDD 与自动化证据

- RED：Release Notes/更新体验契约测试新增紧凑 Header、模板内部样式、自适应高度、轻量材质和测试标记不可见断言；首次运行 11 个聚焦测试时 3 个按预期失败。
- GREEN：实现后同一组聚焦测试 **11/11 PASS**。
- 完整测试：`dotnet test LoomX.slnx -c Release --no-restore` → **1182/1182 PASS**，0 skipped。
- Release publish：407 个文件，`LoomX.exe` 存在；发布包启动 PID 29164 后 `ExecutablePath` 与输出目录精确一致，随后仅关闭该 PID。
- Inno Setup 6.7.3 编译成功，安装器大小 49,227,875 字节。

### 19.3 CUA 视觉验收

- 透明模式截图：`outputs/20260921-192320-release-notes-visual-polish/verification/release-notes-transparent.png`。
- 非透明模式截图：`outputs/20260921-192320-release-notes-visual-polish/verification/release-notes-nontransparent.png`。
- 三个模块不再显示为独立白色卡片；标题、正文和 Chevron 形成连续文档结构。
- 折叠首个模块后正文从 UIA 树消失，另外两个模块保持展开；重新展开后继续显示。
- `LOOMX_UPDATE_PREVIEW` 不再出现在可见 UIA 文本中。
- 验收结束后配置库 `TransparencyEnabled=0`，保持用户透明窗口设置为 Off；只关闭本轮 PID 27044 和发布冒烟 PID 29164，未操作其他 LoomX 进程。

### 19.4 产物

- 发布目录：`outputs/20260921-192320-release-notes-visual-polish/publish/`。
- 安装器：`outputs/20260921-192320-release-notes-visual-polish/installer/LoomX-0.12.7-setup.exe`。
