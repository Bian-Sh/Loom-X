# 磨砂算法视觉差异修复验证

日期：2026-09-03

## 变更范围

- `MainWindow.BuildTransparencyLevels` 将 Acrylic、Blur、Mica 分别限制为“所选材质 -> Transparent”，避免 Blur 在不支持 Gaussian Blur 的平台上静默使用 AcrylicBlur。
- `MainWindow.ApplyAppearance` 按材质调整窗口根背景遮罩，Mica 和 Blur 使用更轻的遮罩以提高可见差异。
- 设置页契约测试覆盖独立回退列表和材质遮罩因子。

## 验证结果

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 相关测试 | 通过 | `dotnet test OllamaHub.Tests\\OllamaHub.Tests.csproj --no-restore --filter FullyQualifiedName~SettingsViewContractTests`，13/13 |
| 完整测试 | 通过 | `dotnet test OllamaHub.Tests\\OllamaHub.Tests.csproj --no-restore`，123/123 |
| Release 构建 | 通过 | `dotnet build OllamaHub.Desktop\\OllamaHub.Desktop.csproj -c Release --no-restore`，0 错误 |
| 发布打包 | 通过 | `outputs/20260903-064632/OllamaHub.Desktop.exe` 已生成 |
| Diff 检查 | 通过 | `git diff --check` 无输出 |
| 运行中截图 | 受限 | CUA 对当前 Avalonia/WebView 窗口 UIA 树在 4 秒内超时，未将超时结果当作视觉通过证据；源码级回归、构建和发布均已完成。 |

构建期间仅保留仓库已有的 SQLite 安全公告和 `EndOfStream` 分析警告，未新增编译错误。
