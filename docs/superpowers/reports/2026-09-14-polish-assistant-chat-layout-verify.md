# AI 助手聊天布局验证报告

## 结论

| 维度 | 状态 |
| --- | --- |
| 完整性 | PASS：6/6 个任务完成，4/4 个 requirements 已实现 |
| 正确性 | PASS：8/8 个场景有实现和自动化或实机证据 |
| 一致性 | PASS：实现符合 `design.md` 的 6 项决策，未发现规格漂移 |

未发现 CRITICAL、WARNING 或 SUGGESTION 级问题，可以进入归档前确认。

## 自动化证据

- `dotnet test LoomX.slnx --no-restore --nologo`：611/611 通过，0 失败，0 跳过。
- `dotnet build LoomX.slnx --configuration Release --no-restore --nologo`：0 错误，6 个既有警告。
- 弹层宽度契约 RED：`Expected: 256 / Actual: 296`，1 条失败、9 条通过；实现后 GREEN：10/10 通过。
- 搜索框透明背景与长模型名展示契约 RED：2 条失败、10 条通过；实现后 GREEN：12/12 通过。
- `openspec validate polish-assistant-chat-layout --strict --json`：1/1 change 校验通过，0 issue。
- `git diff --check ce6ff7a4bbf7f2ece489a75d9edd109156859f72...HEAD`：通过。

既有警告包括 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903、`SettingsViewModel.cs` 的 CS8618、`AnthropicResponseMapper.cs` 的 CA2024，以及两处测试 CS8602；本次改动未新增这些警告。

## Requirement 与场景映射

1. 标题操作与消息滚动条对齐
   - 实现：`LoomX/Views/AssistantView.axaml:405` 给新会话按钮增加 `TranslateTransform X="3"`。
   - 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs:20`。
   - 实机：新会话按钮中心 `x=1218`；消息区右边缘 `x=1226`，16px 滚动条中心同为 `x=1218`。
2. 消息滚动条避让对话内容
   - 实现：`LoomX/Views/AssistantView.axaml:379` 将局部 Thumb 的 `RenderTransformOrigin` 设为 `0%,50%`，悬停从左侧基准向右展开。
   - 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs:65` 读取真实模板 Thumb 并验证缩放原点。
3. 输入框按内容自动增高
   - 实现：`LoomX/Views/AssistantView.axaml:660` 启用多行、换行和内部纵向滚动；`LoomX/Views/AssistantView.axaml.cs:107` 按顶层客户区高度的 32% 更新 `MaxHeight`；`LoomX/Views/AssistantView.axaml.cs:116` 区分 Enter 与 Shift+Enter。
   - 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs:88` 和 `:102`。
   - 实机：760px 客户区下理论上限 243.2px，UIA 取整为 244px；超过上限后出现 `16 x 224px` 内部滚动条。
4. 模型选择弹层保持紧凑比例
   - 实现：`LoomX/Views/AssistantView.axaml:742` 使用 256px 外宽、360px 最大高度和 6px 间距；Provider/模型行分别为 32px/30px。
   - 透明主题：`LoomX/Views/AssistantView.axaml:353` 至 `:363` 让搜索框在普通、悬停和聚焦状态均保持透明；`LoomX.Tests/Views/AssistantViewStyleTests.cs:65` 通过真实 Avalonia 控件验证最终背景。
   - 长名称：`LoomX/Views/AssistantView.axaml:767` 至 `:769` 使用剩余宽度列、`CharacterEllipsis` 和标准 ToolTip；`LoomX.Tests/Views/AssistantViewStyleTests.cs:79` 锁定布局与提示契约。
   - 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs:20`、`:41`、`:65` 和 `:79`。
   - 实机：搜索框 `242 x 32px`，Provider 行 `242 x 32px`，模型行 `224 x 30px`，列表溢出时出现纵向滚动条，思考等级区域保留。
   - UIA：长名称按钮的 `help` 保留完整名称，例如 `claude-3-5-haiku-20241022` 和 `deepseek-v4-flash-vision-exp`，证明悬停气泡绑定未被省略显示截断。

## 桌面验证产物

- 最新发布目录：`outputs/LoomX-win-x64-2026-09-14-model-popup-theme-tooltip-verify/`
- 输入框截图：`outputs/LoomX-win-x64-2026-09-14-compact-popup-verify/assistant-composer-max-height.png`
- 弹层 UIA 几何与完整模型名：`outputs/LoomX-win-x64-2026-09-14-model-popup-theme-tooltip-verify/assistant-model-popup-uia.json`
- 输入框 UIA 几何：`outputs/LoomX-win-x64-2026-09-14-compact-popup-verify/assistant-composer-uia.json`

当前启用透明主题时，Avalonia `Popup` 使用独立合成层，CUA 的主窗口位图不会包含弹层本体；UIA 树仍完整暴露弹层控件、边界和滚动条。本报告以 UIA 几何、真实控件测试和 XAML 契约三者交叉验证弹层尺寸，不把主窗口截图缺少 Popup 合成层视为功能失败。

## 分支处理

按用户已明确的选择保留本地分支 `codex/polish-assistant-chat-layout`，不推送远端。用户已确认现有中文提交信息合理，无需压缩或合并提交。
