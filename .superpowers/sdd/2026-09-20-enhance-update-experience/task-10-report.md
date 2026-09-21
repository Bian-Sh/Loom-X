# Task 10 实施报告

## 状态

已完成源码、失败优先测试、定向/完整验证、CUA、OpenSpec strict validate、最终时间命名发布、进程路径核验与报告。详细证据见 `docs/superpowers/reports/2026-09-20-enhance-update-experience-verify.md`。

## 实施改动

- 新增 `LoomX/Services/DebugUpdatePreviewService.cs`：仅 `#if DEBUG`，仅支持 downloading、verifying、ready、error、history-empty。
- 修改 `MainWindowViewModel`：Debug 环境变量有效时注入预览服务并主动启动一次检查；Release 无分支。
- 修复 `SettingsViewModel.SelectedTabIndex`：更新历史在索引 2 首次加载，而不是代理页索引 1。
- 修复标题栏入口：默认 32px，Hover/Focus 252px，展开文案且不挤压窗口按钮。
- 补充/更新相应契约与生命周期测试。

## RED / GREEN

1. 预览接线 RED：缺少 `DebugUpdatePreviewService.cs`；GREEN：契约测试 1/1。
2. 更新 Tab RED：代理页触发 1 次历史请求（期望 0）；GREEN：生命周期测试 1/1。
3. Hover RED：缺少按钮宽度契约且 CUA Hover 后仍 32px；GREEN：契约测试 1/1，CUA 32px → 252px。

## 自动化结果

- 定向服务/ViewModel：50/50 PASS。
- 定向 UI/契约/本地化：47/47 PASS。
- 首次发布前完整测试真实结果：1155/1156；唯一失败为无关 `GatewayViewModelDeletionTests` 并发枚举偶发异常，独立类 8/8 PASS。
- Release build：0 error、2 个 `NU1903`；发布阶段另如实出现既有 `CS8618`、`CA2024`。
- OpenSpec strict：valid。
- `git diff --check`：通过。

## 最终发布

- 最新目录：`outputs/20260921-083304-enhance-update-experience/`
- 唯一入口：`LoomX.exe`；保留 `publish.log`。
- Release 扫描不包含 `LOOMX_UPDATE_PREVIEW`。
- 最终进程实际路径：`D:\AppData\Github\Loom-X - Copy\outputs\20260921-083304-enhance-update-experience\LoomX.exe`。
- 旧 `outputs/20260921-072800-enhance-update-experience/` 与 `outputs/20260921-073944-enhance-update-experience/` 均保留，不覆盖、不删除，且不作为最终产物。

## CUA

原始证据目录保留；Fix Round 1 有效补证目录：`.superpowers/sdd/2026-09-20-enhance-update-experience/task-10-evidence/fix-round-1-20260921-080800/`。旧 `06-dark-transparency-off.png` 与旧浅色截图字节及 SHA-256 完全相同，已标记为无效历史证据；新 `16` 为真实深色 + 关闭透明，新 `19` 同时显示 Toast 与更新浮窗且不重叠。

## 边界

未修改、添加、提交、删除、移动或清理用户列出的 Comet 激活未跟踪目录；未 push；未运行 Comet build guard、verify 或 archive。


## 提交后复核

- 2026-09-21 07:48-07:51 +08:00 再次执行定向测试（50/50、47/47、代表组合 2/2）、完整测试（1156/1156）、Release build（0 error、2 个 `NU1903`）、OpenSpec strict 与 base..HEAD `git diff --check`，均完成。
- 保留首次发布前完整测试 1155/1156 及独立代表类 8/8 的真实记录；提交后最终复核 1156/1156 进一步表明该无关并发枚举失败为非稳定风险。
- 当次提交后复核未修改源码，当时发布目录为 `outputs/20260921-073944-enhance-update-experience/`；Fix Round 1 后该包已陈旧但继续保留。


## Fix Round 1/5（2026-09-21 08:08-08:42 +08:00）

### Reviewer Finding 修复

1. **深色证据**：CUA 复查发现旧 `05-light-transparency-off.png` 与旧 `06-dark-transparency-off.png` 均为 56,351 字节，SHA-256 同为 `48B9197354584C0AAB0074CAEA7C28175C15CC3FA83FA56C4DDDE8CD640A500C`，因此旧 `06` 仅保留并标记为无效历史证据。根因是设置只保存主题值，未应用 Avalonia `RequestedThemeVariant`，且视觉令牌没有 Light/Dark ThemeDictionary。按失败优先契约补齐最小主题接线后，CUA 在关闭透明效果下验证真实浅色/深色。
2. **Toast 层级与布局**：失败优先契约先要求 Toast 显式高于 Overlay，且位于 228px 侧栏区域、不覆盖浮窗主体；旧实现因缺少 `Panel.ZIndex` 红灯。最小 AXAML 修复使用可维护的 Style Setter：Overlay ZIndex=1，Toast ZIndex=2；Toast 左侧栏最大宽度 196px、允许换行，浮窗主体位于第二列。
3. **测试结果措辞**：`1155/1156` 改称“首次发布前完整测试”，`1156/1156` 改称“提交后最终复核”；Fix Round 1 最终源码完整测试为 `1157/1157`。

### RED / GREEN

- Toast RED：聚焦契约 1 FAIL，旧 AXAML 缺少 Toast `Panel.ZIndex=2`；GREEN：聚焦 Toast 契约 PASS。
- 主题 RED：聚焦契约 1 FAIL，缺少 `applyTheme: mainWindow.ApplyTheme`；GREEN：主题 + Toast 聚焦组合 2/2 PASS。
- UI/契约/本地化：48/48 PASS；服务/更新 ViewModel：50/50 PASS；`WindowAppearanceCoordinatorTests`：6/6 PASS。
- 一次包含 `SettingsViewModel|MainWindowViewModel|WindowAppearanceCoordinatorTests` 的组合进程在运行 1 个测试后触发 60 秒 blame hang，正在运行的代表用例为 `AssistantDecisionLifecycleTests.MainWindowViewModel_Dispose幂等释放Assistant并收敛已Claim请求`；未反复重跑该大组合，独立代表类 2/2 PASS。
- 最终完整测试：1157/1157 PASS，1 分 54 秒；Release build：0 error、2 个 `NU1903`。发布阶段仍如实记录既有 `CS8618`、`CA2024`，定向编译曾出现既有 `CS8602`。

### CUA 与发布

- 有效证据目录：`.superpowers/sdd/2026-09-20-enhance-update-experience/task-10-evidence/fix-round-1-20260921-080800/`。所有操作前后均执行 `get_window_state`，截图只包含 Loom-X 单窗口。
- `15-valid-light-after-theme-fix.png`：82,845 字节，SHA-256 `DE8262498254D53CD04FEB75FCF5D9EF0CFFDF9423E57AB30152635BBF58265D`。
- `16-valid-dark-transparency-off.png`：83,100 字节，SHA-256 `B052CDEAEA6FB37F2689A497C3FF0483C0E1514E3BCAAB2B2C51A3615EDDEF5A`。
- `19-valid-dark-toast-dialog-no-overlap.png`：133,856 字节，SHA-256 `1283A930A53E9B371A43387EAC904F8443773C2C8669560FA6CADFB8669E66CC`；Toast“发现新版本 v9.9.0”与更新浮窗同时可见，空间不重叠。
- 最新发布目录：`outputs/20260921-083304-enhance-update-experience/`；唯一 exe 为 `LoomX.exe`，保留 `publish.log`，发布扫描中 `LOOMX_UPDATE_PREVIEW` 命中 0。
- PowerShell `Start-Process -FilePath` 启动 PID 46716；`Win32_Process.ExecutablePath` 精确等于 `D:\AppData\Github\Loom-X - Copy\outputs\20260921-083304-enhance-update-experience\LoomX.exe`；验证后只关闭本轮 PID 46716。
- 最新 Loom-X 日志 `loomx-20260921_005.log` 对预览正文、环境变量、Debug 原始异常、`Authorization:`、`Bearer `、`sk-` 的扫描均为 0。
- Context7 resources/templates 均为空，无法调用 `resolve-library-id` / `query-docs`；降级参考 Avalonia 官方主题文档，确认使用 `RequestedThemeVariant` 与 Light/Dark ThemeDictionaries。

OpenSpec 6.3/6.5 仅在上述新证据齐全后重新确认完成；未 push，未运行 Comet build guard、verify 或 archive。
