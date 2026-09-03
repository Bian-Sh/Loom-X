# 概览拓扑与 AppDataStore 验证报告

## 结果

验证通过。实现复用 `AppDataStore`，Web 拓扑包含 Endpoint、Combo、Provider、Model 四层，实时消息通过 C# `InvokeScript` 发送。

## 自动化验证

- `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --filter FullyQualifiedName~OverviewGraphContractTests`：5/5 通过。
- `dotnet test OllamaHub.slnx`：146/146 通过。
- `dotnet build OllamaHub.slnx --no-restore`：成功，0 错误。
- 模块脚本语法检查：提取 `index.html` module script 后执行 `node --input-type=module --check`，通过。
- standalone 发布：`outputs/20260903-172442/`，发布脚本成功；目录包含 `OllamaHub.Desktop.exe` 和 `Assets/Overview/index.html`，且仅保留一个桌面 exe。

## Shell 启动实证

按 `docs/superpowers/reports/2026-08-31-provider-launch-data-discrepancy.md`，此前使用 `explorer.exe` 启动 `outputs/20260903-165359/OllamaHub.Desktop.exe`，日志记录真实 UI 进程 33336：

- 既有真实 Provider 数据验证记录：Provider 4、原始模型 33、快照模型 12、Endpoint 3。
- 拓扑发送后 Web 诊断：`endpoints=3`、`combos=5`、`providers=4`、`models=12`、`edges=24`。
- `moduleLoaded=true`、`rendererReady=true`、`topologyApplied=1`、`metricsApplied=1`、`lastError=null`。
- 数据库路径为 `%LOCALAPPDATA%/OllamaHub/OllamaHub.db`，未创建第二份设置数据库。

本次修复后的发布包以 `--allow-multiple-instances` 启动并按进程路径校验（进程 46300）。当前本机配置库当时 Provider 为 0，Web 仍完成 `moduleLoaded=true`、`rendererReady=true`、`topologyApplied=1`、`metricsApplied=1`，且 5 秒观察窗口仅出现两次概览刷新，无递归刷新；此前 Provider 4 的拓扑实证仍覆盖完整数据投影。

## 直连线与 Provider 容器调整

- 移除可见的 `Endpoint → Model` route 直连线；route 仅保留为遥测映射元数据。
- Provider 改为包围其 Model 的容器框，Provider 标题显示在容器边界上；无 Model Provider 仍保留空容器。
- 活动请求改为高亮 Endpoint → Combo → Provider 容器 → Model 结构链。
- 契约测试 5/5、全量测试 146/146、构建及模块脚本检查均通过。
- 最新 standalone 发布包：`outputs/20260903-174801/`；Shell 启动诊断 `moduleLoaded=true`、`rendererReady=true`、`topologyApplied=1`、`metricsApplied=1`、`lastError=null`。

## 已知警告

构建保留既有 `SQLitePCLRaw.lib.e_sqlite3` NU1903 漏洞警告及 Anthropic 流读取 CA2024、测试 CS8602 警告；本变更未引入新的失败。
