# 修复方案

将 `LogFileState` 的文件打开逻辑集中到内部辅助方法，使用 `FileMode.Open`、`FileAccess.Read` 和 `FileShare.ReadWrite | FileShare.Delete`，并设置顺序扫描选项。这样读取句柄既能与 SQLite 的读写句柄共存，也能适应数据库文件替换场景。

通过 `InternalsVisibleTo` 让测试直接验证该打开策略：测试先以读写权限持有数据库风格句柄，再调用辅助方法读取文件内容。无需修改 SQLite 连接、数据库结构或桌面服务接口。
