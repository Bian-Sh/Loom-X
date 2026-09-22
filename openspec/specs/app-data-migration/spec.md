# app-data-migration Specification

## Purpose
描述 LoomX 当前用户数据目录、数据库初始化和旧版数据库目录隔离约束。LoomX 只使用当前 LoomX 数据目录，不提供旧版 OllamaHub 数据库自动迁移。

## Requirements

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
