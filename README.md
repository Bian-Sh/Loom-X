# Loom-x

Loom-x 是一个本地 HTTP 代理服务，对外提供 Ollama 与 OpenAI 兼容入口，并将请求转发到可配置的 Provider/Model。

## 当前能力

- Provider 与 Model 通过 Avalonia 桌面控制中心增删改。
- 正常运行数据唯一存储在 `%LOCALAPPDATA%\LoomX`：配置库为 `LoomX.db`，活动库为 `LoomX.Activity.db`，日志位于 `logs` 子目录。
- API Key 使用 Windows DPAPI 保护后写入数据库，界面和管理 API 不回显旧值。
- 日志使用 Serilog，按日期和 10 MB 大小滚动，保留最近 30 个文件。
- 支持 `openai`、`anthropic`、`ollama` 协议模式。
- 支持 Provider/Model 级 Headers、模型能力、上下文长度、最大输出和采样参数。

## 配置与数据

应用首次运行会在 `%LOCALAPPDATA%\LoomX\` 创建数据库，并建立默认网关监听地址 `http://127.0.0.1:11434`。如果检测到旧 `%LOCALAPPDATA%\OllamaHub\` 目录，Loom-x 会在首次创建新数据库连接前使用 SQLite `VACUUM INTO` 迁移 `OllamaHub.db` 和 `Activity.db`，完成完整性检查后原子提交；旧目录和旧日志始终保留，不会被删除或覆盖。

新库优先于旧库。迁移期间如果旧版仍占用数据库、源库损坏、权限不足或校验失败，Loom-x 会阻止启动，不会静默创建空配置库；修复占用或权限后可再次启动重试。

数据库主要包含：

- `GatewayConfigurations`：网关监听地址。
- `Providers`：Provider 身份、Base URL、协议、OpenAI 请求格式、启用状态、受保护 API Key 和 Headers。
- `Models`：模型信息、Provider 关系、协议覆盖、能力、参数、排序和模型级密钥。

Provider/Model 的保存会立即刷新运行时内存快照；监听地址的变更需要重启网关进程后生效。

## 日志

日志写入 `%LOCALAPPDATA%\LoomX\logs\`：

- 文件名格式：`loomx-YYYYMMDD.log`。
- 单文件超过 10 MB 时自动切分序号文件。
- 最多保留 30 个日志文件，超期文件自动删除。
- 日志不写入 SQLite，API Key 和授权头不得写入日志内容。

## 启动

开发运行桌面控制中心：

`dotnet run --project LoomX\LoomX.csproj`

启动桌面端后进入 **Provider** 页面：

1. 新增 Provider，填写显示名称、业务 ID、Base URL 和协议。
2. 输入 API Key 后保存；留空表示保留已有密钥。
3. 保存 Provider 后新增模型，填写模型 ID、显示名称和 Family。
4. 保存模型后，可从 `/api/tags` 查看对外暴露的模型。

## HTTP 接口

兼容接口：

- `GET /`
- `GET /api/version`
- `GET /api/tags`
- `GET /api/ps`
- `POST /api/show`
- `POST /v1/chat/completions`
- `POST /openai/v1/chat/completions`

本机管理接口：

- `GET /api/admin/providers`
- `POST /api/admin/providers`
- `PUT /api/admin/providers/{id}`
- `DELETE /api/admin/providers/{id}`
- `POST /api/admin/providers/{providerId}/models`
- `PUT /api/admin/models/{id}`
- `DELETE /api/admin/models/{id}`

管理 API 只返回密钥是否已配置，不返回密钥内容。

## 构建与测试

构建解决方案：

`dotnet build LoomX.slnx`

运行测试：

`dotnet test LoomX.Tests\LoomX.Tests.csproj`

发布桌面端：

`pwsh -File scripts\publish-desktop.ps1 -Configuration Release`

发布目录位于 `outputs\<时间戳>\`，只包含一个应用入口 `LoomX.exe`；网关在桌面进程内运行，不会生成或启动独立的 `LoomX.Desktop.exe` 或旧名称入口。

## Visual Studio Copilot Chat BYOM

将 Loom-x 作为本地 Ollama 服务使用：

1. 启动网关并确认监听 `http://127.0.0.1:11434`。
2. 在桌面控制中心配置 Provider 和 Model。
3. 在 Visual Studio Copilot Chat BYOM 中选择本地 Ollama。
4. 将地址指向 Loom-x 的监听地址。
