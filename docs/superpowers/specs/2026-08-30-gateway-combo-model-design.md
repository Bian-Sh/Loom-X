# 网关 Combo 模型设计

## 背景

当前网关把 `GatewayRoute.Alias` 放在单个成员模型路由上，并将这些路由直接作为 Endpoint 的公开模型。这会导致客户端发现底层成员模型，且可以绕过组合模型直接请求成员，故障转移能力因此失去意义。

本次变更引入显式的 Combo 模型层。一个 Endpoint 可以配置多个 Combo；每个 Combo 具有一个对外模型名，并包含按优先级排序的成员模型。只有 Combo 模型对外可见和可请求。

## 目标

- 为每个 Endpoint 提供多个可独立命名的 Combo 模型。
- 仅公开启用的 Combo 名称，隐藏所有底层 Provider/Model 标识。
- 请求必须先解析到当前 Endpoint 的 Combo，再在 Combo 成员中执行故障转移。
- OpenAI、Azure 和 Ollama 的模型发现接口遵循同一公开边界。
- 网关 UI 使用 Foldout 呈现 Combo，成员模型在 Foldout 内维护。

## 非目标

- 不再新增或保留网关成员路由上的 Alias 模型映射语义。
- 不改变 Provider/Model 的全局配置和上游模型同步机制。
- 不允许未加入任何启用 Combo 的模型继续作为网关模型公开。

## 数据模型

新增 `GatewayComboEntity`：

- `Id`：稳定标识。
- `EndpointKey`：所属 Endpoint。
- `Name`：对外 Combo 模型名，在同一 Endpoint 内唯一且非空。
- `Enabled`：是否出现在发现接口并可接受请求。
- `SortOrder`：Endpoint 内 Combo 的展示顺序。

调整 `GatewayRouteEntity`：

- 增加 `ComboId` 外键，成员归属 Combo。
- 保留 `ModelId`、`Enabled`、`SortOrder`，分别表示成员模型和故障转移状态/顺序。
- 删除 `Alias` 字段及其 DTO、UI 和运行时解析逻辑。
- 同一 Combo 不允许重复引用同一个 Model；同一 Model 可以被多个 Combo 复用。

旧数据库中的路由不自动根据 Alias 推断 Combo。未显式归属 Combo 的旧路由在运行时不可见、不可请求；用户需在 UI 中创建 Combo 并加入成员模型。

## 管理 API 与运行时

管理 API 增加 Combo 的列表、新建、更新、删除接口；路由接口改为接收 `ComboId`。删除 Combo 时级联删除其成员路由；删除全局 Model 前仍需保证不存在任何 Combo 成员引用。

模型发现接口只返回当前 Endpoint 的启用 Combo：

- `/v1/models`、`/openai/v1/models`、`/azure/v1/models` 返回 Combo 名称。
- `/api/tags` 返回 Ollama Endpoint 的 Combo 名称。
- `/api/show` 仅接受 Ollama Endpoint 的 Combo 名称，不再通过全局模型目录解析。

请求处理按以下顺序执行：

1. 从请求模型名解析当前 Endpoint 的启用 Combo；未指定模型时选择第一个启用 Combo。
2. 若未找到 Combo，返回 404，不尝试按底层 ModelId、显示名或 Alias 匹配。
3. 按 Combo 内成员的 `SortOrder` 尝试上游请求；可转移的网络错误、408、429 和 5xx 继续下一个成员。
4. 成功或不可转移的 4xx 立即返回；所有成员失败时返回 502。

日志继续只记录 Endpoint、Combo 名称、Provider、Model、路径、状态码、字节数和耗时，不记录密钥、请求正文或响应正文。

## 桌面 UI

右侧 Endpoint 面板使用 Combo Foldout 列表：

- 面板右上角只有一个 `+` 图标，悬停 Tooltip 为“新增 Combo 模型”。
- 每个 Foldout header 显示 Combo 模型名，并提供启用 Toggle 和最右侧回收站图标。
- 新建或编辑 Combo 时，名称在 header/编辑区域中直接输入并保存；名称为空或重复时显示错误状态。
- Foldout 内容是成员模型列表。每个成员 cell 左侧为 drag handle，用于调整故障转移顺序；中间显示 Provider 与 Model；右侧为启用 Toggle，最右侧为回收站图标。
- 选择模型加入 Combo 使用现有模型选择器；成员 cell 不再显示 Endpoint 模型名输入框。
- 图标按钮必须有 Tooltip 和可访问名称，布局保持稳定，不使用字符箭头或文字删除按钮。

## 验收场景

- 一个 Endpoint 创建两个 Combo，各自有不同名称和成员顺序；模型发现只返回两个 Combo 名称。
- 请求 Combo 名称时，成员按顺序故障转移；请求成员的实际 ModelId、显示名或旧 Alias 返回 404。
- 未加入任何 Combo 的模型不出现在 `/v1/models`、`/api/tags` 或 `/api/show` 可解析范围内。
- 同一成员模型可被多个 Combo 复用，且各 Combo 的启停和排序互不影响。
- UI 可新增、折叠、展开、重命名、启停、拖拽排序和移除 Combo/成员；操作即时保存并显示安全反馈。
- 旧数据库中的无 Combo 路由不会被自动公开。
