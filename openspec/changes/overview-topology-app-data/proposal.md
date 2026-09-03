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
