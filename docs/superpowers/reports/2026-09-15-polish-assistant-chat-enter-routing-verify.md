# AI 助手多行输入框 Enter 路由复验报告

## 结论

PASS。普通 Enter 已在 Avalonia 多行 `TextBox` 默认换行处理之前被拦截并触发发送；Shift+Enter 与 Ctrl+Enter 仍由多行输入框插入换行，不触发发送。

## 根因与修复

- 根因：`AcceptsReturn=True` 使输入框成为多行 `TextBox`。原实现通过 XAML 普通 `KeyDown` 冒泡处理器接收 Enter，但 `TextBox` 自身会先消费该按键并插入换行，导致发送处理器没有机会执行。
- 修复：删除输入框上的 XAML `KeyDown` 绑定，在 `AssistantView` 父级注册 `InputElement.KeyDownEvent` 的 `RoutingStrategies.Tunnel` 处理器，并仅处理事件源为 `inputTextBox`、且不含 Shift/Ctrl 的 Enter。
- 边界：普通 Enter 即使命令当前不可执行也会被消费，不再向多行输入框残留空换行；带 Shift/Ctrl 的 Enter 不被父级处理，继续使用 `TextBox` 默认多行行为。

## TDD 证据

1. 新增真实路由测试 `PlainEnterIsInterceptedBeforeMultilineTextBoxHandlesIt`。修复前单独运行时失败，`sent` 为 `false`，复现用户报告的“Enter 仍然换行但未发送”。
2. 将处理器移至父级 Tunnel 路由后，同一测试通过，且断言事件已处理、输入文本未追加换行。
3. 新增 `ModifiedEnterStillCreatesLineBreak`，分别覆盖 Shift+Enter 和 Ctrl+Enter 的真实 `TextBox` 路由，确认两者插入 Windows 换行。

## 自动化验证

- `dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --nologo --filter "FullyQualifiedName~AssistantViewStyleTests"`：21/21 通过。
- `dotnet test LoomX.slnx -c Release --no-restore --nologo`：626/626 通过。
- `dotnet build LoomX/LoomX.csproj -c Release --no-restore --nologo`：0 error；仅保留已知 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` NU1903 警告。
- `openspec validate polish-assistant-chat-layout --strict`：通过。

## 桌面实机验证

- 使用本次新发布包启动 Loom-X，并通过 CUA 对真实输入框发送按键。
- 普通 Enter：输入值 `enter-hotfix-check-20260915` 被清空，消息列表出现对应用户消息；随后 Provider 返回既有内容拦截错误，不影响 Enter 已触发发送的判断。
- Shift+Enter：输入框高度从 40px 增至 54px，UIA 值包含 `\r\nshift-line`，消息未发送。
- Ctrl+Enter：输入框高度增至 54px，UIA 值包含 `ct\r\nrl-line`，消息未发送。

## 发布与证据文件

- 发布目录：`outputs/LoomX-win-x64-2026-09-15-assistant-enter-routing-fix/`（406 个文件）。
- 普通 Enter 截图：`assistant-after-enter.png`。
- Shift+Enter 截图：`assistant-after-shift-enter.png`。
- Ctrl+Enter 截图：`assistant-after-ctrl-enter.png`。
- 对应 UIA 快照：`assistant-after-enter-uia.json`、`assistant-after-shift-enter-uia.json`、`assistant-after-ctrl-enter-uia.json`。

## 分支处理

当前工作位于 `master`，上一轮已按用户明确要求合并并推送；本轮修复继续提交到同一分支，`branch_status` 保持 `handled`。
