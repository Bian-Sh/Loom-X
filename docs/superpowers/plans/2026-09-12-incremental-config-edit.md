---
change: incremental-config-edit
design-doc: docs/superpowers/specs/2026-09-12-incremental-config-edit-design.md
base-ref: fcedff5
---

# 配置控件增量保存实施计划

## 1. 事件契约与状态边界

在 `ConfigurationChange.cs` 增加字段 flags、Endpoint key 和 Route 父级 Combo 标识，保持旧构造和旧消费者兼容。为每类实体定义稳定字段集合，确保事件只表达实际变化。

## 2. 管理写入路径

给 `ConfigurationManagementService` 增加可选的重载策略；桌面 `ConfigSnapshotService` 管理写入跳过前置 Provider 重载和写后完整重载，直接管理服务测试继续保留默认行为。保留现有 EF 变更跟踪，让单字段输入只产生目标列更新。

## 3. AppDataStore 局部投影

将单字段保存从 `ReloadCoreAsync` 切换为局部锁内投影：替换目标 response、更新 `CurrentConfig` 相关对象和 `EnabledGatewayModels`，完成后只发布一次 `LocalSave`。创建、删除、同步和明确刷新继续使用完整快照。

## 4. 运行时局部替换

在 `DatabaseConfigurationProvider` 增加按实体应用方法，查询必要目标行并在锁内重建受影响的配置片段；禁止单字段调用完整 `ReloadAsync`。模型启用/禁用和路由变化需要同步运行中的网关。

## 5. ViewModel 定向响应

按事件 Kind、Fields、EntityId/EntityKey 修改 MainWindow、Overview、Providers、Gateway、Settings 的订阅逻辑，保留编辑器实例及纯 UI 状态，Settings 外观预览只由三个外观属性驱动。

## 6. 验证

先为每个数据层行为补一个失败测试，再完成最小实现并回归。覆盖 SQLite 目标列、事件次数、运行时不重载、无关对象引用、透明度回调和快速连续保存。最后运行 `dotnet build LoomX.slnx --no-restore`、`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore`，并执行桌面发布脚本生成 `outputs/` 时间命名包。
