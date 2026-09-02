# 磨砂算法收敛为 Acrylic 验证

日期：2026-09-03

## 变更范围

- 设置页移除磨砂算法下拉框，只保留透明度和磨砂程度调节。
- `MainWindow.BuildTransparencyLevels` 固定为 `AcrylicBlur -> Transparent`；旧版调用传入的 Blur/Mica 会被忽略。
- 配置保存将旧的 Blur/Mica 值归一为 Acrylic，设置页预览和保存始终使用 Acrylic。
- 设置页契约测试覆盖控件移除、固定回退链和旧值兼容行为。

## 验证结果

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 相关测试 | 通过 | `dotnet test OllamaHub.Tests\\OllamaHub.Tests.csproj --no-restore --filter FullyQualifiedName~SettingsViewContractTests`，13/13 |
| 完整测试 | 通过 | `dotnet test OllamaHub.Tests\\OllamaHub.Tests.csproj --no-restore`，123/123 |
| Release 发布 | 通过 | `dotnet publish OllamaHub.Desktop\\OllamaHub.Desktop.csproj -c Release --no-restore -o outputs\\20260903-071053`，0 错误 |
| 发布包 | 通过 | `outputs/20260903-071053/OllamaHub.Desktop.exe` 已生成 |
| Diff 检查 | 通过 | `git diff --check` 无输出 |
| 运行中截图 | 通过 | CUA 窗口 `pid 7376 / window 1444668` 的 `runtime-final-settings.png` 显示只读 `Acrylic（亚克力）`，树中无算法 ComboBox；透明度 61%、磨砂程度 37。 |

构建期间仅保留仓库已有的 SQLite 安全公告和 `EndOfStream` 分析警告，未新增编译错误。
