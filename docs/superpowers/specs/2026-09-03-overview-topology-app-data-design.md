---
comet_change: overview-topology-app-data
role: technical-design
canonical_spec: openspec
language: zh-CN
---

# 概览拓扑与 AppDataStore 实时 Web 交互设计

## 1. 目标与范围

本变更继续完善首页概览的三维 Web 拓扑，解决 Provider 丢失或拥挤、Combo Model 不可见、标签难读、初始相机视角不一致，以及 Web 容器圆角和 HUD 占用空间等问题。

本变更复用桌面进程已有的 `AppDataStore`，不新增第二套配置缓存，不直接访问 SQLite，不改变网关协议和其他页面的数据流。

HUD 三项指标保留在 Web 左上角：活动请求、5 分钟请求、P95。每项只显示标题和数值，移除底部辅助文字，并缩小内边距、间距和最小宽度。

## 2. 数据来源与拓扑合同

`OverviewViewModel` 从 `AppDataStore` 的现有快照投影拓扑数据：

- Endpoint 使用 `CurrentConfig.GatewayEndpoints`；
- Combo 使用 Endpoint 下的 `Combos`，保留名称、启用状态和排序；
- Provider 使用 `AppDataStore.Providers`，按 `BusinessId` 保留所有已配置 Provider，即使没有可展示 Model 也不能丢失；
- Model 使用 `CurrentConfig.Models`，按 `ProviderId + ModelId` 去重；
- 路由的 Provider/Model 归属使用解析配置中的稳定标识，保证遥测高亮可以匹配；
- 配置变更继续由 `AppDataStore.ConfigurationChanged` 驱动；
- 刷新继续调用 `AppDataStore.RefreshAsync()`。

发送给 Web 的拓扑快照包含：

```text
endpoints
combos
providers
models
edges
```

节点关系为：

```text
Endpoint → Combo → Provider → Model
```

边至少包含关系类型及关联节点标识。对同一 Provider/Model 被多个 Endpoint 或 Combo 复用的情况，结构关系去重，路由归属通过 Combo 和边元数据表达；不复制同一 Provider 或 Model 节点。

## 3. 实时事件与 C#→JavaScript 桥接

实时刷新采用进程内事件驱动，不使用轮询，也不把 Web 当作常规表单网页或双向绑定页面。

事件链路为：

```text
GatewayProcessService / RequestTelemetryHub
        ↓
OverviewViewModel
        ↓ 事件
OverviewGraphHost
        ↓ C# InvokeScript
window.applyTopology / window.applyMetrics / window.receiveTelemetry
        ↓
Three.js 场景状态与 DOM 指标直接更新
```

职责边界：

- C# 负责配置快照、请求生命周期、指标聚合、事件顺序和安全字段；
- Web 负责节点、边、粒子、标签、HUD DOM 和相机渲染；
- `OverviewGraphHost` 只负责页面生命周期、待发送快照合并和 C#→JS 调用，不引入网页轮询或复杂绑定协议；
- 页面尚未就绪时，宿主暂存最新拓扑和指标；页面就绪后直接调用 JS 函数发送一次最新状态；
- 页面已就绪后，拓扑/指标事件只发送最新待处理值，遥测事件按生命周期增量直接发送；
- 遥测枚举必须以字符串序列化，JS 按事件名称处理 `RequestStarted`、边尝试和 `RequestCompleted`；
- Web 端不回传敏感数据，不接收或记录 API Key、Authorization、Header、prompt、请求正文或响应正文。

## 4. Web 布局与视觉

### 4.1 宿主布局

- 删除 XAML 中 Web 左上角的 `Endpoint → Model` 和说明文字；
- `NativeWebView` 直接填充拓扑容器，释放标题占用的高度；
- 外层 Border 使用 `ClipToBounds` 和圆角；HTML 根容器同步使用相同圆角与 `overflow: hidden`，共同处理原生 WebView 子窗口露出直角的问题；
- 保留拓扑区域的深色高对比背景。

### 4.2 HUD 与图例

- HUD 固定左上角；
- 保留三项指标及数值；
- 删除每项底部辅助文字，只保留标题和数值；
- 通过更小的 padding、gap 和最小宽度压缩 HUD；
- 删除“活跃边”图例，仅保留必要的节点类型图例；
- 活跃边的真实高亮、粒子和节点脉冲继续保留。

## 5. 四层拓扑布局

- Endpoint 位于左侧列；
- Combo 位于中间列，并按 Endpoint 分组；
- Provider 使用包围模型的容器表达，容器标题显示 Provider；多个容器按真实数量自动分行/分列；
- Model 位于对应 Provider 容器内，保持稳定垂直间距；
- 没有 Model 的 Provider 仍显示独立容器；
- 标签使用稳定布局，避免多个 Provider 或 Combo 挤在同一位置；
- 结构边与遥测高亮状态分离，刷新拓扑时保留可匹配的活动状态；`Endpoint → Model` 的 route 边仅作为遥测归属元数据，不绘制为可见直连线，活动请求高亮对应的 Endpoint → Combo → Provider 容器 → Model 结构链。

## 6. 标签与相机

- 标签使用世界空间 Sprite，根据相机距离实时缩放；
- 设置最小可读尺寸和最大尺寸，避免远处不可读、近处过大；
- 首次加载、`fitTopology` 和重置视角共享同一相机状态函数；
- 初始目标点提高，修复首次视角偏低；
- 滚轮只调整相机距离，不改变目标点、yaw 和 pitch；
- 拓扑刷新只在需要时重新计算适配范围，不强制破坏用户当前旋转状态；
- 提供明确的重置视角逻辑，恢复统一初始状态。

## 7. 错误处理与兼容性

- 页面未加载或脚本调用失败时，记录结构化安全日志并保留网关和桌面摘要；
- `graph-ready` 或导航完成后发送最新待处理快照；
- 页面 JS 错误通过现有 Web 消息通道回报 C#，不把正文或敏感参数写入日志；
- WebView 不可用不影响网关启动、停止、请求转发和 AppDataStore；
- 保持现有 `IOverviewGraphHost` 抽象，不把 Three.js 实现泄漏到 ViewModel。

## 8. 验证标准

- 拓扑数据包含全部 Provider、Combo、Model 和 Endpoint；
- 4 个 Provider 即使没有 Model 也能显示；
- Combo 层级和路由归属可从图中辨识；
- HUD 位于左上角，三项指标只有两行内容，且不显示辅助文字；
- Web 左上角旧标题已删除，左下角没有“活跃边”；
- 拓扑容器左下、右下圆角不再露出直角；
- 配置变化和实时请求事件能通过 C# 直接调用 JS 更新页面，无轮询；
- 遥测事件枚举以字符串传递，活跃边和节点状态与请求生命周期一致；
- 标签在不同相机距离保持可读；
- 首次视角、滚轮缩放和重置视角一致；
- 运行契约测试、全量测试、桌面构建和发布包验证，并使用指定的 Explorer/Shell 启动方式确认真实 Provider 数据。

## 9. 非目标

- 不修改网关协议、Provider 配置模型和设置数据库路径；
- 不重写 `AppDataStore` 的活动窗口或配置刷新机制；
- 不引入 Web 轮询、前端状态管理框架或双向数据绑定；
- 不改变其他页面视觉和交互。
