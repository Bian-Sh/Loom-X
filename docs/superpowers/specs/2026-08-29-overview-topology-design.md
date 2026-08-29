# OllamaHub 概览立体拓扑设计

## 目标

概览页用于同时回答两类问题：网关当前是否健康，以及请求此刻从哪个公开 Endpoint 经过哪个 Model/Provider。场景不表达地理位置，而表达 OllamaHub 的多对多路由拓扑。

三个固定公开入口为 OpenAI、Ollama、Azure。一个 Model 可被多个入口复用，因此场景必须保留单一 Model 实体并显示多条 Endpoint -> Model 关系。请求进行中时，实际边高亮并沿线显示流动亮斑。

## 渲染架构

概览业务不依赖具体 WebView 实现。定义 `IOverviewGraphHost` 作为渲染宿主边界，负责：

- 加载/销毁图形场景；
- 接收拓扑快照、实时请求事件和主题配置；
- 返回节点点击、边点击和场景就绪事件。

首个实现使用 Windows WebView2 承载 Three.js 场景。未来可增加 WebKit、WKWebView 或其他 embedded web 适配器，不改变 ViewModel、遥测事件和拓扑 JSON 契约。宿主初始化失败时，概览仍展示网关和健康摘要，并显示不可用状态，不影响网关运行。

Three.js 场景只负责渲染、交互和相机；拓扑和请求状态由 C# 侧计算，避免把业务规则复制到 JavaScript。

## 空间拓扑

### 节点

- Endpoint 节点位于左前方入口层，固定为 OpenAI、Ollama、Azure，按三角形错层布局。
- Provider 是空间分组容器，不作为第三种大型节点；容器带名称、模型数和健康摘要。
- Model 是唯一实体节点，位于 Provider 容器内。节点带 Provider 色环、显示名和活跃计数。
- Model 被多个 Endpoint 使用时不复制节点，只增加对应边。

### 边

- 业务关系统一为 `Endpoint -> Model`，边元数据包含 EndpointKey、ProviderId、ModelId、路由别名和启用状态。
- 默认边使用低亮度细线；活跃边使用发光管线和粒子亮斑。
- 故障转移按尝试顺序逐条激活边；失败边短暂转为琥珀色并衰减，最终成功边保持活跃态直到请求完成。
- 历史成功关系可保留短暂余辉，超时自动消失，不累积成永久噪声。

### 视觉语言

使用深色中性场景作为概览主视觉，Endpoint 使用协议区分色，Model 使用 Provider 色环区分，活跃态统一采用青色/薄荷色发光，失败态采用琥珀色。背景使用轻量空间网格和雾化深度，不使用地球、地图或地理坐标语义。

## 实时遥测

现有活动记录在请求完成后才入队，只能表达历史活动。新增进程内 `RequestTelemetryHub`，使用线程安全状态表和事件流表达请求生命周期：

1. `RequestStarted`：记录 RequestId、EndpointKey、Protocol、ModelAlias 等安全摘要。
2. `EdgeAttemptStarted`：路由选中 Model 后发布 Endpoint -> Model 边开始事件。
3. `EdgeAttemptCompleted`、`EdgeAttemptFailed`、`EdgeAttemptCancelled`：发布尝试结果、状态码、耗时和是否进入下一条路由。
4. `RequestCompleted`：清理活跃状态；现有 `ActivityEventInput` 继续异步写入活动数据库。

ActivityMiddleware 扩展到三个公开入口及其响应/聊天路径。事件不得包含 prompt、请求正文、响应正文、API Key、Authorization 或自定义 Header 值。日志继续遵循结构化、脱敏规范。

`GatewayProcessService` 转发遥测事件给 `OverviewViewModel`。ViewModel 维护活跃请求数、Endpoint 计数、Model 计数、边状态及 5 分钟聚合指标，并向图形宿主发送增量事件。

## 交互与信息卡片

### 悬停

- Endpoint：协议/公开路径、状态、活跃请求数、最近 5 分钟请求量/成功率/P95，以及当前最活跃的最多 3 个 Model。
- Model：显示名/真实模型 ID、Provider、复用它的 Endpoint、活跃请求数、最近使用时间、最近 5 分钟 P95/错误数。
- Provider 容器：Provider 名、模型数、健康模型数、活跃数、最近 5 分钟成功率/P95、最近一次失败时间。
- 边：只显示 `Endpoint -> Model`、当前状态和 RequestId，避免边悬停成为第二个详情面板。

悬停卡片为轻量浮层，包含标题、状态环、三个关键指标和迷你趋势线。API Key、Header 和完整 Base URL 不展示。

### 点击

点击节点或 Provider 容器后，卡片固定为右侧详情面板，展示完整模型清单、路由摘要和最近活动。点击边可定位到对应 RequestId；详情面板可跳转活动页。

## 相机规则

- 首次打开只根据全量拓扑计算一次最佳总览取景，尽量同时包含三个 Endpoint 和所有 Provider/Model。
- 实时请求不会触发相机移动；活跃状态只改变边、粒子和节点脉冲。
- “聚焦活跃”是显式工具栏动作，按当前活跃请求的整体包围盒执行一次平滑 `fit-to-selection`。
- 节点点击执行单点聚焦，包含节点及其相邻关系。
- “跟随单个请求”默认关闭，只能由用户在详情面板主动开启；其他请求不会抢镜头。
- 视口外的活跃边通过画布边缘方向标记和计数提示，不强制移动镜头。
- Provider/Model 增删时对节点位置插值，保留当前相机变换，不重新全屏 fit。

## 概览布局

- 上方：网关状态、活动请求数、吞吐、P95 和刷新/聚焦工具。
- 中央：占主要面积的 Three.js 拓扑场景。
- 右侧：节点点击后固定的详情面板，未选中时显示三个 Endpoint 健康摘要。
- 下方：最近请求条带，显示时间、Endpoint、Model、状态和延迟，点击可打开活动详情。

## 验证标准

- 三个 Endpoint 在无请求时均可辨识，且共享 Model 只出现一个节点。
- 同一 Model 被多个 Endpoint 路由时，所有关系边同时可见且不重叠成不可读线团。
- 并发请求不会造成镜头跳动；活跃边、节点计数和亮斑与请求生命周期一致。
- 故障转移能按尝试顺序显示边状态，最终结果与活动记录一致。
- 悬停卡片在节点密集和窗口缩放时不遮挡核心拓扑；点击后详情面板稳定。
- WebView 宿主不可用时显示可理解的降级状态，网关启动、停止和请求转发不受影响。
- 自动化测试覆盖遥测生命周期、并发状态清理、敏感字段不进入事件/日志，以及拓扑快照的多对多去重。
