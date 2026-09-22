# 修复方案

将 `LogFileState` 的文件打开逻辑集中到内部辅助方法，使用 `FileMode.Open`、`FileAccess.Read` 和 `FileShare.ReadWrite | FileShare.Delete`，并设置顺序扫描选项。这样读取句柄既能与 SQLite 的读写句柄共存，也能适应数据库文件替换场景。

在 `ConfigurationDatabase.InitializeAsync` 执行 `EnsureSchemaAsync` 前增加只读完整性检查：确认所有当前表和列存在、旧列已移除、默认配置行存在且三个标准 Endpoint 使用规范路径。检查通过时直接结束初始化；仅在首次创建、结构迁移、默认数据缺失或路径迁移时执行原有写入流程。WAL 模式回归测试通过 EF 命令拦截器确认二次初始化不执行写 SQL。

通过 `InternalsVisibleTo` 让测试直接验证该打开策略：测试先以读写权限持有数据库风格句柄，再调用辅助方法读取文件内容。无需修改 SQLite 连接、数据库结构或桌面服务接口。
