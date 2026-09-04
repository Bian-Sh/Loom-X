# 修复方案

1. 删除服务端 `ConfigurationRefreshService` 注册及实现，保留启动时的首次 `ReloadAsync`，配置管理服务成功写入后仍显式重载运行时快照。
2. 删除桌面 `ConfigSnapshotService` 的 `FileSystemWatcher` 和 `ExternalChangeDetected` 事件，以及 `AppDataStore` 对应的延迟重载订阅与取消逻辑。
3. 为 Provider/Model 编辑 ViewModel 增加轻量脏状态跟踪。响应数据库结果后清除脏状态；新建实体始终允许首次保存；已有实体在未发生用户修改时，保存命令直接返回。
4. 增加单元和契约测试，验证周期刷新入口已移除、文件监听已移除，以及编辑器无变化保存不会继续执行。

不改变 SQLite schema、数据库路径、服务端 API 或单实例启动策略。
