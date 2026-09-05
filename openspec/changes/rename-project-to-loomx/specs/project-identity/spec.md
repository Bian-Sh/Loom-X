## ADDED Requirements

### Requirement: LoomX technical identity

项目 SHALL 使用 `LoomX` 作为 C# 根命名空间、桌面项目和程序集名称，使用 `LoomX.Tests` 作为测试项目和程序集名称，并使用 `LoomX.slnx` 作为解决方案名称。桌面发布包 SHALL 只提供 `LoomX.exe` 作为应用入口。

#### Scenario: Solution and project build

- **WHEN** 使用 `dotnet build LoomX.slnx` 构建解决方案
- **THEN** 构建系统解析 LoomX 和 LoomX.Tests 项目，且不依赖旧 `OllamaHub.Desktop` 或 `OllamaHub.Tests` 项目路径

#### Scenario: Published application entry

- **WHEN** 使用发布脚本发布 Windows 桌面应用
- **THEN** 发布目录包含唯一的应用入口 `LoomX.exe`，且不生成旧名称的应用入口

#### Scenario: Avalonia resource resolution

- **WHEN** LoomX 启动并加载 XAML 资源
- **THEN** `x:Class`、`using` 和 `avares://` URI 均解析到 `LoomX` 程序集和 namespace

### Requirement: Loom-x product branding

面向用户的窗口标题、导航品牌、诊断摘要、README 和发布说明 SHALL 使用 `Loom-x`。健康检查根响应的产品名 SHALL 为 `Loom-x`，OpenAI 模型列表的 `owned_by` SHALL 为 `loomx`。

#### Scenario: Desktop branding

- **WHEN** 用户打开 LoomX 桌面应用或查看设置页
- **THEN** 窗口标题、品牌文本和诊断摘要显示 `Loom-x`，不显示旧产品名

#### Scenario: Gateway identity

- **WHEN** 客户端请求健康检查根路径或 OpenAI 模型列表
- **THEN** 根响应的 `name` 为 `Loom-x`，模型条目的 `owned_by` 为 `loomx`

### Requirement: Protocol route compatibility

LoomX SHALL 保持现有 HTTP 路由、请求/响应结构和 Provider/Model 配置语义不变；名称改动不得改变 `/api/tags`、`/v1/chat/completions` 和其他现有兼容入口的路径。

#### Scenario: Existing Ollama route

- **WHEN** 客户端请求 `/api/tags`
- **THEN** LoomX 使用原有路由和模型列表结构返回结果

#### Scenario: Existing OpenAI route

- **WHEN** 客户端向 `/v1/chat/completions` 发送原有格式的请求
- **THEN** LoomX 按原有配置和响应约定处理请求

