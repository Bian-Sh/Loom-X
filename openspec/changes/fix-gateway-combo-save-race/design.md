## 修复方案

在 `GatewayViewModel` 内使用 `SemaphoreSlim` 串行化 Combo/Route 管理写入。写入触发的 `ConfigurationChanged` 只设置待刷新标志，当前写入结束后统一刷新 UI；刷新期间不再次发起刷新。刷新按 Combo ID 恢复选中项，避免后续事件继续操作旧对象。

Combo 编辑器记录最近一次服务端保存的名称和启用状态，通过 `HasPendingChanges` 判断 LostFocus 是否确实产生了修改。所有 Combo/Route 写入入口在调用数据层前校验对象仍在当前集合中，路由回填按 ID 去重。

不改变服务端 API、数据库结构或运行时配置路径。
