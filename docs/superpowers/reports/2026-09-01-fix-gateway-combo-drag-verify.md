## 验证报告：fix-gateway-combo-drag

### 总结

| 维度 | 结果 |
| --- | --- |
| 完整性 | 3/3 任务完成；无增量规范 |
| 正确性 | 拖拽源已从 `Button` 改为非交互 `Border`，保留 `DragOver`、`Drop` 和 `MoveRouteAsync` 链路；契约回归测试通过 |
| 一致性 | 与设计中的“非按钮抓手 + 现有重排持久化”决策一致 |

### 验证项

- `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --no-restore`：通过，92/92。
- `dotnet build OllamaHub.Desktop/OllamaHub.Desktop.csproj --no-restore`：通过，0 错误。
- `scripts/publish-desktop.ps1 -Configuration Release`：通过，发布目录为 `outputs/20260901-115024`，包含 `OllamaHub.Desktop.exe`。
- 根因检查：`RouteHandle_OnPointerPressed` 仅接收 `Border` 抓手，不再绑定可点击 `Button`；目标行仍绑定 `DragOver`/`Drop`。
- 安全检查：未新增密钥、请求正文或不安全操作。

### 备注

构建输出包含既有 SQLite 漏洞提示和分析器警告，不影响本次编译、测试或发布结果。
