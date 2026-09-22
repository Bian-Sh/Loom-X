# LoomX 项目更名与数据迁移验证报告

日期：2026-09-05

## 结果

本次变更已完成构建阶段验收。产品显示名为 `Loom-x`，技术标识、项目、程序集和发布入口统一为 `LoomX`；正常运行时数据使用 `%LOCALAPPDATA%\\LoomX`，旧 `%LOCALAPPDATA%\\OllamaHub` 目录保留为迁移源和回滚备份。

## 自动化验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 定向路由与宿主契约测试 | 通过 | `20/20` |
| 完整测试 | 通过 | `dotnet test LoomX.slnx --no-restore --nologo`，`182/182` |
| 解决方案构建 | 通过 | `dotnet build LoomX.slnx --no-restore --nologo`，0 错误 |
| 发布脚本 | 通过 | `outputs/20260905-155924`，唯一 exe 为 `LoomX.exe` |
| 旧名称扫描 | 通过 | 活动源码、脚本、测试和用户文档中的命中仅为迁移源常量、迁移测试、AGENTS 规则、README 迁移说明和升级说明 |

构建和测试仍有仓库已有的 SQLitePCLRaw 安全公告与 nullable 警告，但没有编译错误或测试失败。

## 桌面与网关验证

- 使用最新发布包 `outputs/20260905-155924/LoomX.exe` 启动，窗口标题为 `Loom-x 控制中心`。
- 概览页和设置页显示 `Loom-x` 品牌，未显示旧产品名。
- 从旧 `%LOCALAPPDATA%\\OllamaHub` 启动后的新目录包含 `LoomX.db` 和 `LoomX.Activity.db`；旧目录及源数据库仍保留。
- 启动内嵌网关后，`GET /`、`GET /api/tags`、`GET /v1/models` 均返回 HTTP 200；根响应 `name` 为 `Loom-x`，OpenAI 模型列表条目的 `owned_by` 为 `loomx`。
- 停止网关后 `11434` 端口释放，关闭窗口后 LoomX 进程退出。

## 流程备注

`review_mode` 为 `standard`，但当前环境没有 `requesting-code-review` 技能，因此按 Comet 要求记录 `<!-- review skipped: skill unavailable -->`，未伪造审查结果。既有未跟踪流程产物 `LoomX.Tests/TempDbProbeTests.cs` 与 `LoomX/graphify-out/` 保留未删除。
