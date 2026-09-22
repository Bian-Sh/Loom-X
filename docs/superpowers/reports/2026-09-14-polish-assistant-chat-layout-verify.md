# AI 助手聊天布局验证报告

## 结论

| 维度 | 状态 |
| --- | --- |
| 完整性 | PASS：9/9 个任务完成，5/5 个 requirements 已实现 |
| 正确性 | PASS：10/10 个场景有实现和自动化或实机证据 |
| 一致性 | PASS：实现符合 `design.md` 的 7 项决策，未发现规格漂移 |

未发现 CRITICAL、WARNING 或 SUGGESTION 级问题，可以进入归档前确认。

## 自动化证据

- `dotnet test LoomX.slnx -c Release --no-restore --nologo`：623/623 通过，0 失败，0 跳过。
- `dotnet build LoomX.slnx --configuration Release --no-restore --nologo`：0 错误，6 个既有警告。
- 弹层宽度契约 RED：`Expected: 256 / Actual: 296`，1 条失败、9 条通过；实现后 GREEN：10/10 通过。
- 搜索框透明背景与长模型名展示契约 RED：2 条失败、10 条通过；实现后 GREEN：12/12 通过。
- 搜索框聚焦模板契约 RED：`PART_BorderElement: White`，1 条失败；覆盖模板背景后 GREEN：1/1 通过，助手视图定向测试保持 12/12 通过。
- `openspec validate polish-assistant-chat-layout --strict --json`：1/1 change 校验通过，0 issue。
- `git diff --check ce6ff7a4bbf7f2ece489a75d9edd109156859f72...HEAD`：通过。

既有警告包括 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903、`SettingsViewModel.cs` 的 CS8618、`AnthropicResponseMapper.cs` 的 CA2024，以及两处测试 CS8602；本次改动未新增这些警告。

## Requirement 与场景映射

1. 标题操作与消息滚动条对齐
   - 实现：`LoomX/Views/AssistantView.axaml:405` 给新会话按钮增加 `TranslateTransform X="3"`。
   - 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs:20`。
   - 实机：新会话按钮中心 `x=1218`；消息区右边缘 `x=1226`，16px 滚动条中心同为 `x=1218`。
2. 消息滚动条避让对话内容
   - 实现：`MessageScroll` 隐藏内置纵向滚动条，`MessageScrollBar` 位于 `ChatRegion` 的独立右侧列；代码后置同步 `Extent`、`Viewport`、`Offset` 和拖动值。
   - 测试：验证内外控件位于同一 Grid 的不同列、关闭自动隐藏，并在 40 条状态消息下验证可见性、范围和双向偏移同步。
   - 实机：CUA/UIA 识别独立 `MessageScrollBar`，截图中轨道与 Thumb 位于消息内容右侧，不再覆盖正文。
3. 输入框按内容自动增高
   - 实现：输入框继续启用多行、换行和内部纵向滚动；`ShouldSendMessage` 仅让无 Shift/Ctrl 修饰的 Enter 发送。
   - 测试：覆盖 760px/600px 高度上限，以及 Enter、Shift+Enter、Ctrl+Enter、Shift+Ctrl+Enter。
   - 实机：760px 客户区下理论上限 243.2px，UIA 取整为 244px；超过上限后出现内部滚动条。
4. 状态消息与输入区保持稳定布局
   - 实现：状态行从居中改为左对齐，消息列表横向拉伸；根 Grid 取消统一 `RowSpacing`，聊天区显式使用 2px 底部间距。
   - 测试：不同长度状态消息的可视左边沿一致；`inputCard.Bounds.Top - ChatRegion.Bounds.Bottom == 2`。
5. 模型选择弹层保持紧凑比例
   - 实现：`LoomX/Views/AssistantView.axaml:751` 使用 256px 外宽、360px 最大高度和 6px 间距；Provider/模型行分别为 32px/30px。
   - 透明主题：`LoomX/Views/AssistantView.axaml:353` 至 `:372` 同时覆盖控件属性和 Fluent 模板的 `PART_BorderElement`，让搜索框在普通、悬停和聚焦状态均保持透明；`LoomX.Tests/Views/AssistantViewStyleTests.cs:65` 聚焦真实 Avalonia 控件并验证模板不存在可见背景。
   - 长名称：`LoomX/Views/AssistantView.axaml:776` 至 `:778` 使用剩余宽度列、`CharacterEllipsis` 和标准 ToolTip；`LoomX.Tests/Views/AssistantViewStyleTests.cs:100` 锁定布局与提示契约。
   - 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs:20`、`:41`、`:65` 和 `:100`。
   - 实机：搜索框 `242 x 32px`，Provider 行 `242 x 32px`，模型行 `224 x 30px`，列表溢出时出现纵向滚动条，思考等级区域保留。
   - UIA：长名称按钮的 `help` 保留完整名称，例如 `claude-3-5-haiku-20241022` 和 `deepseek-v4-flash-vision-exp`，证明悬停气泡绑定未被省略显示截断。

## 桌面验证产物

- 最新发布目录：`outputs/LoomX-win-x64-2026-09-14-assistant-chat-layout-feedback/`
- 输入框截图：`outputs/LoomX-win-x64-2026-09-14-compact-popup-verify/assistant-composer-max-height.png`
- 弹层 UIA 几何与完整模型名：`outputs/LoomX-win-x64-2026-09-14-model-popup-theme-tooltip-verify/assistant-model-popup-uia.json`
- 输入框 UIA 几何：`outputs/LoomX-win-x64-2026-09-14-compact-popup-verify/assistant-composer-uia.json`

当前启用透明主题时，Avalonia `Popup` 使用独立合成层，CUA 的主窗口位图不会包含弹层本体；UIA 树仍完整暴露弹层控件、边界和滚动条。本报告以 UIA 几何、真实控件测试和 XAML 契约三者交叉验证弹层尺寸，不把主窗口截图缺少 Popup 合成层视为功能失败。

用户已在新发布包中实机确认模型搜索框聚焦态透明效果符合预期。

## 实机反馈复验

- 根因确认：失败信息属于 `ChatEntryKind.Status`，原 `HorizontalAlignment="Center"` 会按每条文本的自身宽度居中，因此长短不同的多行错误从不同横坐标开始，形成截图中的左右漂移；现已统一左对齐并让消息列表横向拉伸。
- 滚动条：消息 `ScrollViewer` 内置滚动条改为 `Hidden`，右侧独立 `ScrollBar` 关闭自动隐藏并同步 `Extent`、`Viewport`、`Offset`；CUA/UIA 已识别独立 `MessageScrollBar`，截图可见其位于消息内容外侧。
- 键盘：定向测试覆盖 Enter 发送、Shift+Enter、Ctrl+Enter、Shift+Ctrl+Enter 不发送；助手视图定向测试 18/18 通过。
- 间距：根 Grid 取消统一 `RowSpacing`，聊天区显式设置底部 2px；几何测试直接断言 `inputCard.Bounds.Top - ChatRegion.Bounds.Bottom == 2`。
- 桌面截图：`outputs/LoomX-win-x64-2026-09-14-assistant-chat-layout-feedback/assistant-chat-loaded-session.png`。
- 新发布目录：`outputs/LoomX-win-x64-2026-09-14-assistant-chat-layout-feedback/`，共 406 个文件，主程序为 `LoomX.exe`。

## 分支处理

用户已明确要求将当前分支合并到 `master` 并推送远端；现有中文提交保持不变，不执行 squash。
