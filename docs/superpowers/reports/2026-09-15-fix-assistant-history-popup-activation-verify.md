# 验证报告：fix-assistant-history-popup-activation

## 摘要

| 维度 | 状态 |
| --- | --- |
| 完整性 | PASS：3/3 任务完成，1/1 requirement 已实现 |
| 正确性 | PASS：原生 Popup 契约、两次连续 cell 选择和重命名取消均通过 |
| 前台稳定性 | PASS：两次 20 秒、约 1 ms 间隔采样均只出现 LoomX 主窗口 HWND |

## 检查结果

1. 历史会话浮层使用 Avalonia 原生 `Popup`，`ShouldUseOverlayLayer=False`，不再创建普通顶层 `Window`。
2. 会话 cell 只将 `IsHistoryOpen` 设为 `false` 后执行载入命令，不调用 `Activate()`、`SetForegroundWindow` 或临时 Topmost 补偿。
3. Popup 原生 HWND 的 owner 为 LoomX 主窗口：主窗口 `0x70F74`，首次 Popup `0x540CD8`，`Owner=0x70F74`。
4. Popup 范围为 `1016,471,1336,757`，主窗口范围为 `63,319,1243,1079`；Popup 右侧越过主窗口边界 93 px，平台浮动宿主行为符合设计。
5. 第一次真实前台 cell 点击关闭 Popup 并载入会话；20 秒前台采样只记录 LoomX PID `31600`、HWND `0x70F74`。
6. 关闭后首次点击历史按钮即重新打开 Popup，新 HWND 为 `0x580E74` 且 owner 仍为 `0x70F74`；第二次 cell 点击载入另一会话并关闭 Popup，第二次 20 秒采样仍只记录 `0x70F74`。
7. 重命名按钮进入 TextBox 后编辑框获得键盘焦点；按 `Escape` 后编辑态退出，原标题保持不变。
8. 定向测试通过：1/1，0 失败。
9. 完整测试通过：601/601，0 失败，0 跳过。
10. Release 构建通过：0 错误。
11. `openspec validate fix-assistant-history-popup-activation --strict` 通过。
12. `git diff --check` 通过。
13. 已重新发布 `win-x64`：`outputs/LoomX-win-x64-2026-09-15-003341/`。
14. `review_mode: off`，按 hotfix 配置跳过自动代码审查；构建、测试、安全和真实桌面验证未跳过。

## 产物一致性

- `proposal.md` 描述的外部应用闪帧由移除普通顶层历史窗口消除，不依赖事后重新激活 LoomX。
- `design.md` 的原生 Popup、平台浮动宿主、锚点定位和禁止激活补偿决策均已实现。
- delta spec 的“整个载入过程持续保持 LoomX 前台”场景有两次连续 Win32 前台序列证据。
- Hotfix 预设不创建单独的 Superpowers Design Doc；OpenSpec `design.md` 已覆盖修复设计，`design_doc: null` 与流程一致。

## 问题分级

- CRITICAL：无。
- IMPORTANT：无。
- WARNING：仓库既有依赖 `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 仍报告高严重性漏洞告警；本次窗口修复未引入或扩大该问题。
- SUGGESTION：无。

## 结论

根因修复及完整验证通过，可以进入归档前确认。
