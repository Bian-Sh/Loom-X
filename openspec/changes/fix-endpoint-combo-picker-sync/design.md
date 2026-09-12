# 修复方案

在 `GatewayComboEntity` 增加持久化 `IsDeleted` 字段，并为旧 SQLite 库补列迁移。删除组合时，如果仍存在 Endpoint binding 则只标记删除并保留路由、名称、ID 和全部 binding；没有 binding 的组合继续物理删除。Endpoint binding 更新允许提交已删除但仍勾选的 ID，取消最后一个 binding 后清理对应软删除组合。运行时配置过滤 `IsDeleted` 组合，避免已删除组合参与路由。

Endpoint DTO 携带组合删除状态。`GatewayEndpointEditorViewModel.ApplyBindings` 对已删除且仍绑定的组合重建 tombstone，保留勾选；用户取消勾选后从该 Endpoint 下拉移除。组合以相同 ID 更新恢复时清除删除状态并同步所有下拉项。删除状态和启用状态均通过资源键解析，`不存在` 使用红色显示，`停用` 保持普通状态色。

测试使用临时 SQLite 覆盖软删除保留数据、Endpoint binding、重载后的勾选状态、取消最后绑定后的清理、同 ID 恢复和缺列迁移，并保留新增/启停的局部同步回归覆盖。

## 范围确认

实现涉及数据库实体、管理服务、运行时快照、桌面状态和视图契约。用户已确认继续 Hotfix，不升级为完整 Comet 流程。
