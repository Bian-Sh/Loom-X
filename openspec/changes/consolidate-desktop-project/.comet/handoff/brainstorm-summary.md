# Brainstorm Summary

- Change: consolidate-desktop-project
- Date: 2026-09-04

## 确认的技术方案

- 产品只保留 Avalonia 桌面端；不保留 `OllamaHub.exe`、`SetApiKey` 或旧 CLI 参数启动方式。
- 桌面端仍在同一进程调用 `OllamaHubHost.CreateAsync` 托管本地网关。
- 设置数据库仍唯一使用 `%LOCALAPPDATA%\OllamaHub\OllamaHub.db`。
- 有效核心源码继续使用 `OllamaHub.*` 命名空间；不修改历史 `docs/superpowers` 记录。

将 25 个有效核心源文件物理迁入 `OllamaHub.Desktop` 的对应逻辑目录，排除 CLI 专用 `Program.cs`、`WindowsConsoleManager.cs` 和重复程序集属性。桌面工程保留现有 SDK 与 `Microsoft.AspNetCore.App`，新增 Serilog 直接包引用；测试只引用桌面工程并更新硬编码路径；解决方案与 README 移除旧工程和 CLI 说明；以完整测试、构建和桌面端发布验证。

## 关键取舍与风险

- 源码漏迁或重复编译：按旧项目清单迁移，并在删除旧目录后构建。
- 缺少直接依赖：补齐 Serilog 包并进行还原、测试和构建。
- 静态契约测试路径失效：更新其指向桌面目录。
- 发布行为回归：发布到时间命名的 `outputs` 目录，确认没有 `OllamaHub.exe`。

## 测试策略

- 运行 `dotnet test OllamaHub.slnx` 验证网关、配置与桌面契约测试。
- 运行 `dotnet build OllamaHub.slnx` 验证仅剩桌面工程和测试工程的解决方案构成。
- 发布桌面工程到时间命名的 `outputs` 目录，检查发布物只包含 `OllamaHub.Desktop.exe`。

## Spec Patch

无。
