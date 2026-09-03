# 高层设计

## 架构

`OverviewViewModel` 仅依赖 `AppDataStore` 的 `CurrentConfig`、`Providers`、`GatewayEndpoints` 与 `ConfigurationChanged`，构造安全的拓扑快照和指标消息。`OverviewGraphHost` 维护页面就绪状态及最新待发送快照，在页面就绪后以 `InvokeScript` 调用 `window.applyTopology`、`window.applyMetrics` 和 `window.receiveTelemetry`。Web 页面负责 Three.js 节点、连线、粒子、标签、HUD 与相机。

## 数据合同

拓扑 JSON 必须包含 `endpoints`、`combos`、`providers`、`models`、`edges` 五个集合。节点关系为 Endpoint → Combo → Provider → Model；Provider 按真实配置列表保留，即使没有 Model 也显示。重复 Provider/Model 节点去重，边携带关系类型及稳定标识。

## 交互与视觉

- HUD 固定左上，仅显示三项标题和数值。
- 删除 Web 旧标题、说明与“活跃边”图例文字，但保留真实高亮动画。
- Endpoint、Combo、Provider、Model 使用稳定分列/网格布局。
- 标签按相机距离缩放并限制最小/最大尺寸；初始、适配和重置共享相机状态，滚轮只改距离。
- 原生宿主 Border 与 HTML 根容器同时裁剪圆角。

## 兼容与错误处理

页面未就绪时只保留最新拓扑和指标，遥测按事件顺序增量发送。枚举序列化为字符串。脚本调用失败记录结构化安全日志，不影响网关和 AppDataStore。
