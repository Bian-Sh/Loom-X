## MODIFIED Requirements

### Requirement: LoomX runtime data paths

正常运行时 SHALL 只从 `AppDataPaths` 解析并使用以下路径：根目录 `%LOCALAPPDATA%\\LoomX`、配置库 `LoomX.db`、活动库 `LoomX.Activity.db`、日志目录 `logs` 和配置库初始化锁 `LoomX.db.init.lock`。运行时 SHALL 不解析、不读取、不写入或迁移 `%LOCALAPPDATA%\\OllamaHub` 下的数据库，也不得保留旧数据库迁移锁或旧数据库路径常量。

#### Scenario: New installation

- **WHEN** LoomX 在没有旧数据目录的用户环境中首次启动
- **THEN** 应用创建 `%LOCALAPPDATA%\\LoomX` 及其新数据库和日志目录，不创建或检查 `%LOCALAPPDATA%\\OllamaHub` 数据库

#### Scenario: Legacy directory is ignored

- **WHEN** 用户环境中仍存在 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 或 `Activity.db`
- **THEN** LoomX 不打开、不复制、不校验这些文件，直接按当前 LoomX 路径初始化和使用数据库

### Requirement: Legacy database migration

LoomX SHALL NOT 将 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 或 `Activity.db` 自动迁移到 LoomX 数据目录。LoomX SHALL 在 `AppDataPaths.EnsureCreated()` 后直接使用当前 LoomX 数据库路径执行现有初始化流程，不得在创建配置数据库连接前调用旧数据库迁移服务，也不得提供旧数据库自动迁移、快照、临时提交或迁移异常处理能力。

#### Scenario: First launch with legacy data

- **WHEN** 用户首次启动 LoomX 且旧配置库和活动库存在
- **THEN** LoomX 忽略旧配置库和活动库，只按当前 LoomX 路径初始化并继续运行

#### Scenario: Legacy configuration only

- **WHEN** 旧配置库存在但旧活动库不存在
- **THEN** LoomX 不迁移旧配置库，按正常初始化流程使用当前 LoomX 配置库和活动库

#### Scenario: Current database initialization

- **WHEN** LoomX 启动且 `%LOCALAPPDATA%\\LoomX\\LoomX.db` 尚不存在
- **THEN** LoomX 使用当前数据库初始化流程创建目标库，不搜索或复制旧 OllamaHub 数据库

#### Scenario: Existing current database

- **WHEN** `%LOCALAPPDATA%\\LoomX\\LoomX.db` 或 `%LOCALAPPDATA%\\LoomX\\LoomX.Activity.db` 已存在
- **THEN** LoomX 继续使用当前数据库，不输出旧库迁移跳过信息，也不访问旧数据库目录

## REMOVED Requirements

### Requirement: Safe SQLite copy and integrity validation

移除旧库 `VACUUM INTO` 快照、临时目标、完整性检查和原子提交流程。

#### Scenario: WAL database migration

- **WHEN** 旧数据库存在尚未合并到主文件的 WAL 数据
- **THEN** LoomX 不读取或迁移旧数据库

#### Scenario: Interrupted migration

- **WHEN** 迁移在临时文件阶段中断
- **THEN** LoomX 不再创建或清理旧库迁移临时文件

#### Scenario: Activity database retry after configuration success

- **WHEN** 配置库迁移已成功但活动库迁移失败
- **THEN** LoomX 不执行分阶段旧库迁移重试

### Requirement: Idempotent conflict handling

移除旧库迁移的目标冲突判断、重复迁移和旧库优先级处理。

#### Scenario: Relaunch after migration

- **WHEN** LoomX 已启动过且旧目录仍然存在
- **THEN** LoomX 只使用当前 LoomX 数据库，不检查旧目录

#### Scenario: New database already exists

- **WHEN** 新数据库存在而旧数据库也存在
- **THEN** LoomX 保持当前数据库不变且不访问旧数据库

### Requirement: Failure safety and legacy retention

移除旧库迁移失败、迁移锁、临时文件清理和迁移异常阻止启动等专用行为；旧目录保留属于应用不触碰旧目录的结果，不再是迁移流程的运行时保证。

#### Scenario: Corrupt legacy database

- **WHEN** 旧配置库无法通过 SQLite 完整性检查
- **THEN** LoomX 不打开、不校验该旧配置库，也不因旧库状态改变当前数据库初始化

#### Scenario: Successful migration keeps source

- **WHEN** 旧目录和源数据库仍然存在
- **THEN** LoomX 不删除、不覆盖且不读取旧目录