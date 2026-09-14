# 应用输入聚焦材质统一验证报告

## 结论

| 维度 | 状态 |
| --- | --- |
| 完整性 | PASS：7/7 个任务完成，3/3 个 requirements 已实现 |
| 正确性 | PASS：标准输入、特殊输入语义和助手复合输入均有自动化与桌面证据 |
| 一致性 | PASS：实现符合 OpenSpec design 与技术设计，未改变业务绑定或输入行为 |

## 自动化证据

- TDD RED：聚焦 `TextBox` 与 `NumericUpDown` 时均检测到 `Border#PART_BorderElement: White`；`input-transparent` 与 `input-embedded` 尚未定义时背景仍为 `SurfaceSubtleBrush`。
- 全局模板 GREEN：`AppInputMaterialStyleTests` 5/5 通过，覆盖 TextBox、ComboBox、NumericUpDown、透明语义、嵌入语义和局部背景保留。
- 页面迁移 GREEN：Assistant 与应用输入材质定向测试 18/18 通过；全部 Views 测试 109/109 通过。
- 助手复合输入 GREEN：移除 `PlainTextBox` ControlTheme 后，Assistant 定向测试 13/13 通过，覆盖共享 class、模板背景、内部零边框、滚动、光标、选区和外层聚焦 class。
- 完整测试：`dotnet test LoomX.slnx --configuration Release --no-restore --nologo` 618/618 通过，0 跳过。
- Release 构建：`dotnet build LoomX.slnx --configuration Release --no-restore --nologo` 0 错误；仅有 2 条既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` NU1903 警告。
- `openspec validate unify-input-focus-material --strict --json`：1/1 change 通过，0 issue。
- `git diff --check`：通过。

## 桌面验证

- 发布目录：`outputs/LoomX-win-x64-2026-09-14-193936-input-material-verify/`，共 406 个文件，唯一 exe 为 `LoomX.exe`。
- 新发布包以 `--allow-multiple-instances` 启动，PID `29684`；进程 `Path` 与发布目录完全一致，验证后已停止。
- AI 助手输入框 UIA 高度由 40px 自动增长到 244px，与 760px 客户区的 32% 上限取整一致；视觉树出现 `PART_VerticalScrollBar`，消息区域同步缩小。
- 代理设置页聚焦普通 TextBox 后只显示品牌色边框，内容背景继续透出同一窗口材质；NumericUpDown 编辑区同样未切换为纯白背景。
- 透明主题截图仅用于检查结构、边框和材质连续性，不把 CUA 合成后的绝对色值作为主题 token 依据。

## 验证产物

- `outputs/LoomX-win-x64-2026-09-14-193936-input-material-verify/assistant-composer-expanded.png`
- `outputs/LoomX-win-x64-2026-09-14-193936-input-material-verify/settings-inputs.png`
- `outputs/LoomX-win-x64-2026-09-14-193936-input-material-verify/settings-proxy-inputs.png`
- `outputs/LoomX-win-x64-2026-09-14-193936-input-material-verify/settings-proxy-focused.png`

## 分支处理

按用户要求保留专用本地分支 `codex/unify-input-focus-material` 和独立工作区，不推送远端，等待最终拍板。
