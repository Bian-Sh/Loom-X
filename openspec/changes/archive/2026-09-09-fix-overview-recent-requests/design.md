# 修复方案

在桌面端新增可复用的 `ActivityQueryService` 依赖到 `OverviewViewModel`，概览刷新时查询 `ActivityQuery(Limit: 8)`，将记录映射为 `OverviewRecentRequestViewModel` 并替换集合内容。查询异常只记录结构化错误日志，不阻断概览拓扑刷新。

实时 `RequestCompleted` 事件仍由现有遥测订阅处理。插入前按请求 ID 去重，使数据库回填和事件通知的时间窗口重叠时不会出现重复行；集合始终限制为 8 条。XAML 列表为时间、Endpoint、Model、状态和延迟分别指定 `Grid.Column`，增加状态列并保持现有紧凑布局。

不改变 Activity 数据库结构、服务端接口或请求记录写入流程。
