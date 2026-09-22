# Comet Design Handoff

- Change: overview-topology-app-data
- Phase: design
- Mode: compact
- Context hash: 5f27300702f0f46f8480f9726fdd29f99b3ed981c03c1cb538aab82ca9b42462

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/overview-topology-app-data/proposal.md

- Source: openspec/changes/overview-topology-app-data/proposal.md
- Lines: 1-23
- SHA256: 982cece21adc21a9a4fb03e38597d607eff83138fc96c4a0f16b6376a43666dc

```md
# 概览拓扑与 AppDataStore 实时 Web 交互

## 问题背景

首页概览当前仍保留旧的 Web 标题与说明，拓扑数据从 Model 反推 Provider，导致没有 Model 的真实 Provider 丢失；Combo 没有独立层级，标签和相机在不同视角下难以阅读。Web 指标和拓扑刷新也需要与桌面端已有 `AppDataStore` 及实时事件链路统一。

## 目标

- 复用 `AppDataStore` 快照，投影完整的 Endpoint → Combo → Provider → Model 四层拓扑。
- 通过进程内事件和 C# `InvokeScript` 将配置、指标和遥测增量直接推送到 Web，不使用轮询或复杂前端状态管理。
- 将活动请求、5 分钟请求、P95 置于 Web 左上角紧凑 HUD，并移除旧标题、辅助文字和“活跃边”图例文案。
- 修复 Web 容器圆角、Provider 布局、标签可读性及统一相机行为。

## 范围

- `OverviewViewModel`、`OverviewGraphHost`、Overview XAML/HTML/JS 及相关契约测试。
- AppDataStore 现有配置和事件接口的复用与安全字段投影。
- 桌面应用构建、测试和 standalone 发布验证。

## 非目标

- 不修改网关协议、Provider 配置模型或设置数据库路径。
- 不重写 AppDataStore 活动窗口，不引入 Web 轮询、前端框架或双向绑定。

```

## openspec/changes/overview-topology-app-data/design.md

- Source: openspec/changes/overview-topology-app-data/design.md
- Lines: 1-21
- SHA256: a25561c79159b065b47d382937495893e5bed10a93461ad6af1c699fd1d30591

```md
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

```

## openspec/changes/overview-topology-app-data/tasks.md

- Source: openspec/changes/overview-topology-app-data/tasks.md
- Lines: 1-7
- SHA256: 16eb5a16fcc01b08a3de34a487bea147cc54e901df0039fb551a3ab540ce9525

```md
# 实施任务

- [ ] 扩展 Overview 拓扑投影，复用 AppDataStore 并加入 Combo/Provider 完整数据合同
- [ ] 实现事件驱动的 C#→JS 快照、指标和遥测桥接，确保枚举字符串序列化
- [ ] 调整 XAML/HTML HUD、标题、图例、圆角及四层布局
- [ ] 统一标签缩放与初始/适配/滚轮/重置相机行为
- [ ] 增加契约与行为测试，完成构建、发布和真实 Provider 验证

```
