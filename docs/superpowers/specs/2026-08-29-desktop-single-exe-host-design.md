# 桌面端单入口发布设计

## 目标

发布目录保留一个应用入口 `OllamaHub.Desktop.exe`，允许携带必要 DLL 和本地运行时文件；不生成 `OllamaHub.exe`，桌面端启动时不创建网关子进程，也不显示控制台窗口。

## 方案

将 OllamaHub 网关从顶层程序改为可复用的 `OllamaHubHost`。它负责创建并配置 `WebApplication`，桌面端生命周期服务直接调用 `StartAsync` 和 `StopAsync`，网关与 Avalonia UI 在同一进程内运行。网关项目保留 `dotnet run` 入口，但关闭 apphost 生成，作为桌面项目引用时只提供 DLL。

桌面项目使用 Windows GUI 子系统（`WinExe`）；发布脚本使用 `win-x64`、`self-contained true`，保留 DLL 形式的依赖，不启用 single-file，避免把用户明确不需要的单文件打包策略引入当前交付物。

## 验证

- 解决方案和测试通过。
- 发布目录中 `.exe` 文件数量为 1，且名称为 `OllamaHub.Desktop.exe`。
- 启动桌面端时网关健康检查通过，进程列表中不出现独立 OllamaHub 子进程。
