# 配置库文件状态读取异常修复

## 问题

桌面端监听 `OllamaHub.db` 的变更事件并读取文件哈希时，偶发记录 `IOException`：文件正被其他进程使用。该异常出现在诊断日志路径，不影响 SQLite 配置读写，但会制造误导性的错误告警。

## 根因

`ConfigSnapshotService.LogFileState` 使用 `FileInfo.OpenRead()`，其共享模式为 `FileShare.Read`。SQLite 写入句柄仍持有文件时，诊断读取句柄的共享模式未允许并发写访问，Windows 因共享冲突拒绝打开文件。持锁者可以是同一桌面进程内的 SQLite 句柄，并不表示启动了第二个 OllamaHub 实例。

## 修复目标

- 允许文件状态诊断在 SQLite 读写句柄活动期间读取主库文件。
- 保持文件状态读取失败仅降级为诊断警告的现有行为。
- 增加回归测试覆盖写句柄活动时的并发读取场景。
