## ADDED Requirements

### Requirement: LoomX runtime data paths

正常运行时 SHALL 只从 `AppDataPaths` 解析并使用以下路径：根目录 `%LOCALAPPDATA%\\LoomX`、配置库 `LoomX.db`、活动库 `LoomX.Activity.db`、日志目录 `logs` 和配置库初始化锁 `LoomX.db.init.lock`。正常运行时不得使用应用目录或当前工作目录创建数据库。

#### Scenario: New installation

- **WHEN** LoomX 在没有旧数据目录的用户环境中首次启动
- **THEN** 应用创建 `%LOCALAPPDATA%\\LoomX` 及其新数据库和日志目录，不创建新的 `%LOCALAPPDATA%\\OllamaHub` 数据库

### Requirement: Legacy database migration

当新配置库不存在且旧配置库 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 存在时，系统 SHALL 将配置库迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.db`。当旧活动库存在时，系统 SHALL 将其迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.Activity.db`。迁移 SHALL 在 LoomX 创建数据库连接前完成。

#### Scenario: First launch with legacy data

- **WHEN** 用户首次启动 LoomX 且旧配置库和活动库存在
- **THEN** LoomX 在新路径下保留配置、活动记录和 DPAPI 密文，并使用新路径继续运行

#### Scenario: Legacy configuration only

- **WHEN** 旧配置库存在但旧活动库不存在
- **THEN** LoomX 迁移配置库并按正常初始化流程创建新的空活动库

### Requirement: Safe SQLite copy and integrity validation

迁移 SHALL 使用 SQLite `VACUUM INTO` 在源库上创建一致性快照，支持 WAL/SHM 状态； SHALL 先写入临时目标并完成 SQLite 完整性及必要表检查，再原子提交正式目标文件。

#### Scenario: WAL database migration

- **WHEN** 旧数据库存在尚未合并到主文件的 WAL 数据
- **THEN** 迁移后的新数据库包含已提交数据，且完整性检查通过后才成为正式数据库

#### Scenario: Interrupted migration

- **WHEN** 迁移在临时文件阶段中断
- **THEN** 新路径不出现不完整的正式数据库，旧数据库保持可读，后续启动可以清理临时文件并重试

#### Scenario: Activity database retry after configuration success

- **WHEN** 配置库迁移已成功但活动库迁移失败
- **THEN** LoomX 保留已验证的配置库、阻止应用启动，并在下次启动只重试活动库迁移

### Requirement: Idempotent conflict handling

迁移 SHALL 是幂等的。当新数据库已经存在时，系统 SHALL 以新数据库为准，不覆盖、不重复导入，也不得使用旧库回写新库。

#### Scenario: Relaunch after migration

- **WHEN** LoomX 已完成迁移并再次启动，且旧目录仍然存在
- **THEN** LoomX 直接使用新数据库，不再次复制或改变新数据库内容

#### Scenario: New database already exists

- **WHEN** 新数据库存在而旧数据库也存在
- **THEN** 新数据库保持不变，旧目录继续作为备份保留

### Requirement: Failure safety and legacy retention

源库损坏、备份失败、完整性检查失败或正式文件提交失败时，系统 SHALL 删除未完成的临时目标、记录安全摘要错误并阻止继续启动；不得静默创建空配置库。迁移成功或失败后均 SHALL 保留旧 `%LOCALAPPDATA%\\OllamaHub` 目录，不删除或覆盖源库。

#### Scenario: Corrupt legacy database

- **WHEN** LoomX 检测到旧配置库无法通过 SQLite 完整性检查
- **THEN** LoomX 不创建可被误认为有效的空配置库，启动失败并保留旧文件供修复

#### Scenario: Successful migration keeps source

- **WHEN** 配置库和活动库迁移成功
- **THEN** 新数据库可正常读取，旧目录和源数据库仍然存在且未被覆盖
