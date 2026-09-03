# 概览拓扑与 AppDataStore 验证报告

## 结果

验证通过。实现复用 `AppDataStore`，Web 拓扑包含 Endpoint、Combo、Provider、Model 四层，实时消息通过 C# `InvokeScript` 发送。

## 自动化验证

- `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --filter FullyQualifiedName~OverviewGraphContractTests`：5/5 通过。
- `dotnet test OllamaHub.slnx`：146/146 通过。
- 模块脚本语法检查：提取 `index.html` module script 后执行 `node --input-type=module --check`，通过。
- standalone 发布：`outputs/20260903-165359/`，发布脚本成功且仅保留 `OllamaHub.Desktop.exe` 主程序及资源。

## Shell 启动实证

按 `docs/superpowers/reports/2026-08-31-provider-launch-data-discrepancy.md`，使用 `explorer.exe` 启动 `outputs/20260903-165359/OllamaHub.Desktop.exe`。日志记录真实 UI 进程 33336：

- Provider 4、原始模型 33、快照模型 12、Endpoint 3。
- 拓扑发送后 Web 诊断：`endpoints=3`、`combos=5`、`providers=4`、`models=12`、`edges=24`。
- `moduleLoaded=true`、`rendererReady=true`、`topologyApplied=1`、`metricsApplied=1`、`lastError=null`。
- 数据库路径为 `%LOCALAPPDATA%/OllamaHub/OllamaHub.db`，未创建第二份设置数据库。

## 已知警告

构建保留既有 `SQLitePCLRaw.lib.e_sqlite3` NU1903 漏洞警告及 Anthropic 流读取 CA2024、测试 CS8602 警告；本变更未引入新的失败。
