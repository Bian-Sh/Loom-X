# Loom-x 升级说明

## 数据迁移

Loom-x 首次启动时检查旧目录 `%LOCALAPPDATA%\OllamaHub\`。当新库不存在且旧库存在时，应用在创建数据库连接前使用 SQLite `VACUUM INTO` 生成一致性快照，完成完整性和必要表检查后再将其原子提交到新目录：

- 配置库：`OllamaHub.db` → `%LOCALAPPDATA%\LoomX\LoomX.db`
- 活动库：`Activity.db` → `%LOCALAPPDATA%\LoomX\LoomX.Activity.db`

迁移会保留旧目录、旧数据库及其 `-wal`/`-shm` 文件，不迁移旧日志。新库已经存在时始终以新库为准，不会被旧库覆盖。

## 失败处理与回滚

旧版仍占用数据库、源库损坏、权限不足、快照失败或完整性检查失败时，Loom-x 会阻止启动，不会创建可用的空配置库。修复占用或权限后重新启动即可重试；配置库迁移成功而活动库失败时，只会重试缺失的活动库目标。

需要回滚时，停止 Loom-x，使用旧版本读取未修改的 `%LOCALAPPDATA%\OllamaHub\` 目录。确认旧版本已退出后再修复问题并重新启动 Loom-x。

## 发布入口

发布包位于 `outputs\<时间戳>\`，唯一应用入口为 `LoomX.exe`。旧版本发布包和旧数据目录由用户自行保留，升级脚本不会清理它们。
