# OllamaHub

OllamaHub 是一个本地 HTTP 代理服务，对外提供 Ollama 与 OpenAI 兼容入口，并将请求转发到可配置的 Provider/Model。

## 当前能力

- Provider 与 Model 通过 Avalonia 桌面控制中心增删改。
- 配置唯一存储在应用目录的 `OllamaHub.db`，使用 Entity Framework Core + SQLite。
- API Key 使用 Windows DPAPI 保护后写入数据库，界面和管理 API 不回显旧值。
- 日志使用 Serilog，按日期和 10 MB 大小滚动，保留最近 30 个文件。
- 支持 `openai`、`anthropic`、`ollama` 协议模式。
- 支持 Provider/Model 级 Headers、模型能力、上下文长度、最大输出和采样参数。

## 配置与数据

运行时不读取、不导入、不生成 `settings.json`。应用首次运行会在可执行文件同级创建 `OllamaHub.db`，并建立默认网关监听地址 `http://127.0.0.1:11434`。

数据库主要包含：

- `GatewayConfigurations`：网关监听地址。
- `Providers`：Provider 身份、Base URL、协议、启用状态、受保护 API Key 和 Headers。
- `Models`：模型信息、Provider 关系、协议覆盖、能力、参数、排序和模型级密钥。

Provider/Model 的保存会立即刷新运行时内存快照；监听地址的变更需要重启网关进程后生效。

## 日志

日志写入应用目录下的 `logs/`：

- 文件名格式：`ollamahub-YYYYMMDD.log`。
- 单文件超过 10 MB 时自动切分序号文件。
- 最多保留 30 个日志文件，超期文件自动删除。
- 日志不写入 SQLite，API Key 和授权头不得写入日志内容。

## 启动

开发运行网关：

`dotnet run --project OllamaHub`

开发运行桌面控制中心：

`dotnet run --project OllamaHub.Desktop`

启动桌面端后进入 **Provider** 页面：

1. 新增 Provider，填写显示名称、业务 ID、Base URL 和协议。
2. 输入 API Key 后保存；留空表示保留已有密钥。
3. 保存 Provider 后新增模型，填写模型 ID、显示名称和 Family。
4. 保存模型后，可从 `/api/tags` 查看对外暴露的模型。

命令行也支持写入受保护 API Key：

`dotnet run --project OllamaHub -- SetApiKey <providerOrModelId> <apiKey>`

该命令直接更新 `OllamaHub.db`，不依赖 JSON 配置文件。

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

`dotnet build OllamaHub.slnx`

运行测试：

`dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj`

发布桌面端：

`dotnet publish OllamaHub.Desktop -c Release -r win-x64 --self-contained false -o <output-directory>`

## Visual Studio Copilot Chat BYOM

将 OllamaHub 作为本地 Ollama 服务使用：

1. 启动网关并确认监听 `http://127.0.0.1:11434`。
2. 在桌面控制中心配置 Provider 和 Model。
3. 在 Visual Studio Copilot Chat BYOM 中选择本地 Ollama。
4. 将地址指向 OllamaHub 的监听地址。
