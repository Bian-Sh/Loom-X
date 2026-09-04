# 修复方案

1. 删除服务端 `ConfigurationRefreshService` 注册及实现，保留启动时的首次 `ReloadAsync`，配置管理服务成功写入后仍显式重载运行时快照。
2. 删除桌面 `ConfigSnapshotService` 的 `FileSystemWatcher` 和 `ExternalChangeDetected` 事件，以及 `AppDataStore` 对应的延迟重载订阅与取消逻辑。
3. 为 Provider/Model 编辑 ViewModel 增加轻量脏状态跟踪。响应数据库结果后清除脏状态；新建实体始终允许首次保存；已有实体在未发生用户修改时，保存命令直接返回。
4. Provider/Model 由编辑 ViewModel 的 `PropertyChanged` 触发自动保存。文本输入、下拉选择、开关、数字编辑和 Header 增删最终都通过属性变化进入 350ms 防抖队列；保存队列捕获对应编辑对象，避免切换选择后保存到错误对象。视图不再注册 `LostFocus` 或自动保存控件事件。
5. 增加单元和契约测试，验证周期刷新入口已移除、文件监听已移除、编辑器无变化保存不会继续执行，以及焦点事件不会触发保存。

不改变 SQLite schema、数据库路径、服务端 API 或单实例启动策略。
