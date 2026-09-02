# 配置库文件状态读取异常修复

## 问题

桌面端监听 `OllamaHub.db` 的变更事件并读取文件哈希时，偶发记录 `IOException`：文件正被其他进程使用。该异常出现在诊断日志路径，不影响 SQLite 配置读写，但会制造误导性的错误告警。

网关启动时还会重复执行配置库初始化。即使 schema、默认行和 Endpoint 都已完整，初始化仍执行迁移 SQL，导致主库被无意义地触碰并触发变更事件。

## 根因

`ConfigSnapshotService.LogFileState` 使用 `FileInfo.OpenRead()`，其共享模式为 `FileShare.Read`。SQLite 写入句柄仍持有文件时，诊断读取句柄的共享模式未允许并发写访问，Windows 因共享冲突拒绝打开文件。持锁者可以是同一桌面进程内的 SQLite 句柄，并不表示启动了第二个 OllamaHub 实例。

`ConfigurationDatabase.InitializeAsync` 每次都会调用 `EnsureSchemaAsync`，其中包含多条 `CREATE TABLE IF NOT EXISTS`、`ALTER TABLE` 和 Endpoint `UPDATE`，没有先判断当前 schema 是否已经满足要求。网关启动因此会重复执行初始化写 SQL，即使没有用户修改配置。

## 修复目标

- 允许文件状态诊断在 SQLite 读写句柄活动期间读取主库文件。
- 保持文件状态读取失败仅降级为诊断警告的现有行为。
- 增加回归测试覆盖写句柄活动时的并发读取场景。
- 配置库已完成初始化且没有结构或默认数据变更时，不执行写 SQL。
- 增加 WAL 模式下初始化无写命令的回归测试。
