# Provider OpenAI 接口模式选择设计

## 1. 目标

在 Provider 编辑器中，当 Provider 类型为 `openai` 时显示接口模式选择，让用户在 `OpenAI-Completions` 和 `Responses API` 之间选择，并将选择保存到 SQLite。

本次变更只增加 Provider 配置能力，不改变现有请求转发、响应透传或对外 Endpoint 路由行为。

## 2. 概念边界

- Provider 是 OllamaHub 管理的连接对象，描述连接类型、Base URL、鉴权和模型配置。
- `apiMode` 表示 Provider 的连接协议类型，目前支持 `openai`、`anthropic`、`ollama`。
- `endpointFormat` 是 OpenAI Provider 的接口模式，取值为 `chat_completions` 或 `responses`。
- OllamaHub 对外暴露的 Endpoint 独立于 Provider。Azure 是 OllamaHub 的对外 Endpoint，不是 Provider 类型；本次不新增或修改 Azure Endpoint。

## 3. 界面交互

Provider “基础”页继续使用现有的显示名称、Provider ID、Provider 类型和 Base URL 布局。

当 Provider 类型为 `openai` 时，在基础配置区域显示“请求格式”下拉框：

| 显示名称 | 保存值 |
| --- | --- |
| OpenAI-Completions | `chat_completions` |
| Responses API | `responses` |

新建 Provider 默认类型为 `openai`，默认接口模式为 `responses`。切换到 `anthropic` 或 `ollama` 时隐藏下拉框，但保留已保存值；切回 `openai` 后恢复显示。字段变更沿用现有 Provider 自动保存机制。

Provider 摘要可以显示当前接口模式，便于用户确认当前选择。界面文案和字段命名参考 Kun provider 模块；不引入网关页面的 Endpoint 路由编辑交互。

## 4. SQLite 配置契约

`ProviderEntity` 新增非空文本字段 `EndpointFormat`，数据库列默认值为 `responses`。`ConfigurationDatabase.EnsureSchemaAsync` 增加幂等的列补充逻辑，以支持已有 SQLite 数据库启动后自动获得默认值。

以下对象携带同名字段：

- `ProviderInput`
- `ProviderResponse`
- `ProviderEditorViewModel`
- `ResolvedModelConfig`

创建和更新 Provider 时只接受 `chat_completions`、`responses`；缺省值归一化为 `responses`。非 OpenAI Provider 的字段仍可存储，但界面隐藏，现有请求逻辑不读取该字段。

配置仍只来自 `%LOCALAPPDATA%/OllamaHub/OllamaHub.db`。本项目不提供静态 JSON 配置入口，也不保留历史 JSON 配置模型、加载器、测试和示例文件；项目文件中对应的内容排除配置一并删除。

## 5. 运行行为

本次不在 `OllamaHubHost`、`ProtocolPassthroughClient` 或 Anthropic 转换链中增加分支。无论选择哪种接口模式，现有对外请求路径、上游请求体、响应体和 Header 行为保持不变。`ResolvedModelConfig.EndpointFormat` 仅作为运行配置快照的一部分，为后续网关能力保留明确契约。

Azure 对外 Endpoint 的路由和兼容语义不因该字段改变；Provider 的 OpenAI 类型只表示其连接配置采用 OpenAI 协议族。

## 6. 校验与错误处理

- 非法接口模式值在管理服务校验阶段拒绝，返回现有 Provider 保存错误反馈，数据库和运行快照均保持原值。
- 数据库列补充逻辑可重复执行，不因列已存在而失败。
- 自动保存失败时沿用现有状态提示和日志记录，不新增独立错误状态。
- API Key、Header、模型继承、连接测试和模型同步逻辑保持现状。

## 7. 测试与验收

自动测试覆盖：

1. 新建 Provider 的默认接口模式为 `responses`。
2. 更新为 `chat_completions` 后再次读取值正确。
3. 非法接口模式被拒绝且原值不变。
4. 缺少 `EndpointFormat` 的旧 SQLite schema 启动后自动补列并得到 `responses`。
5. 运行配置快照包含接口模式。
6. 现有 passthrough 请求测试继续通过，证明新增配置未改变请求行为。
7. 仓库不再包含静态 JSON 配置加载器、模型、测试或项目内容引用。

桌面端验收覆盖：

- 选中 OpenAI Provider 时出现“请求格式”下拉框。
- 选择 `OpenAI-Completions` 或 `Responses API` 后自动保存。
- 切换到其他 Provider 类型时控件隐藏，切回 OpenAI 时恢复上次选择。
- 新建 Provider 默认显示 `Responses API`。
