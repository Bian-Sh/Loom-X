# plugin-host-bridge Specification

## Purpose
定义插件调用 LoomX 宿主能力时所依赖的稳定、版本化和实现无关边界，使存储、日志与通知可由宿主统一治理，并为未来进程外插件执行保留兼容的契约形状。

## Requirements

### Requirement: 插件仅通过公共 Bridge 使用宿主能力
宿主 SHALL 在插件初始化上下文中提供版本化 Plugin Bridge。插件与宿主交换的能力类型 MUST 来自公共契约程序集，不得向插件暴露依赖注入容器、宿主私有对象、UI 实例或具体基础设施连接对象。

#### Scenario: 插件初始化获得 Bridge
- **WHEN** 通过兼容性检查的插件被初始化
- **THEN** 插件获得与自身 id 绑定的 Storage、Logging 和 Notifications Bridge

#### Scenario: 插件请求宿主实现对象
- **WHEN** 插件尝试把 `IServiceProvider`、宿主 ViewModel、窗口对象或具体数据库连接声明为初始化依赖
- **THEN** Runtime 拒绝该入口契约且不向插件传递这些对象

### Requirement: Manifest 声明并协商 Bridge 能力版本
插件 Manifest SHALL 声明所需 Bridge 能力及契约版本。Runtime MUST 在编译或实例化插件前完成兼容性检查；缺失或不兼容的必需能力 MUST 导致插件被拒绝，不得猜测、静默降级或部分执行未知语义。

#### Scenario: 所需能力均受支持
- **WHEN** 插件要求 `storage.sqlite/v1`、`logging/v1` 和 `notifications/v1` 且宿主均支持
- **THEN** Runtime 允许插件继续加载并提供对应 Bridge

#### Scenario: 必需能力版本不兼容
- **WHEN** 插件要求宿主不支持的 `storage.sqlite/v2`
- **THEN** Runtime 在执行任何插件代码前拒绝加载并返回能力不兼容诊断

#### Scenario: 未声明能力不得调用
- **WHEN** 插件未在 Manifest 声明 `notifications/v1` 却调用 Notifications Bridge
- **THEN** 宿主拒绝该调用并返回稳定的能力未授权错误

### Requirement: Bridge 契约可跨进程传输
Bridge 的公开输入、输出与错误 SHALL 使用字符串、枚举、字节数组、只读值 DTO、取消令牌和异步结果表达。契约 MUST NOT 包含任意 `object` 值、流、委托、数据库连接、数据库事务或宿主私有类型。

#### Scenario: 进程内实现可替换为 Proxy
- **WHEN** 宿主未来将同一插件从进程内执行迁移到进程外 Worker
- **THEN** 插件业务代码继续使用同一 Bridge 契约，调用参数和结果可被确定性序列化

#### Scenario: 不可传输类型被阻止进入公共契约
- **WHEN** Bridge API 变更引入 `object`、`Stream`、delegate 或具体数据库类型
- **THEN** 契约兼容性检查失败且该变更不得发布

### Requirement: 插件自行命名和组织 SQLite 数据库
`storage.sqlite/v1` SHALL 允许插件按需打开其自有数据根目录内的数据库相对路径。插件自行决定是否落盘、文件名、相对子目录、表结构、索引、SQL、事务和迁移，宿主不得强制统一的 `plugin-storage.db` 或 KV collection。

#### Scenario: 插件创建自定义数据库
- **WHEN** 插件打开 `credential-tokens.db`、`settings.db` 或 `cache/models.db`
- **THEN** 宿主在 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\<relative-path>` 打开对应 SQLite 数据库

#### Scenario: 插件选择不落盘
- **WHEN** 插件不调用 Storage Bridge
- **THEN** 宿主不为该插件自动创建默认数据库文件

#### Scenario: 不同插件使用相同文件名
- **WHEN** 两个插件都打开 `settings.db`
- **THEN** 两个数据库分别位于各自插件数据根目录且互不可见

### Requirement: SQLite 路径严格隔离
Storage Bridge MUST 拒绝绝对路径、UNC 路径、设备路径、盘符相对路径、目录穿越、空路径以及规范化后或经 reparse point、符号链接逃逸插件数据根目录的路径。

#### Scenario: 合法嵌套路径
- **WHEN** 插件打开 `cache/models.db` 且规范化结果位于本插件数据根目录内
- **THEN** 宿主创建所需插件内子目录并打开该数据库

#### Scenario: 跨插件路径被拒绝
- **WHEN** 插件打开 `../other-plugin/data.db` 或其他规范化后逃逸的路径
- **THEN** 宿主返回 `InvalidPath` 且不创建、不读取目标文件

#### Scenario: 链接逃逸被拒绝
- **WHEN** 插件目录内路径经 reparse point 或符号链接指向根目录之外
- **THEN** 宿主返回 `InvalidPath` 且不跟随该链接打开数据库

### Requirement: SQLite Bridge 提供参数化 CRUD 与查询
`storage.sqlite/v1` SHALL 提供参数化命令执行、单行查询、多行查询和显式事务。SQL 参数与行值 MUST 使用 `Null`、`Integer`、`Real`、`Text`、`Blob` 的封闭值联合表达，不得使用任意对象字典。

#### Scenario: 执行参数化写入
- **WHEN** 插件提交包含命名参数的 INSERT、UPDATE 或 DELETE 命令
- **THEN** 宿主绑定显式类型参数并返回受影响行数

#### Scenario: 查询多行结果
- **WHEN** 插件执行 SELECT 并请求多行结果
- **THEN** 宿主以有序行和显式列值类型返回结果，不泄露具体 SQLite Reader

#### Scenario: 查询单行无结果
- **WHEN** 插件执行单行查询且结果集为空
- **THEN** Bridge 返回明确的无结果状态而非伪造空行

### Requirement: SQLite Bridge 管理事务生命周期
Storage Bridge SHALL 提供与数据库句柄绑定的显式事务。提交、回滚、释放和取消 MUST 具有确定性结果；事务完成后再次执行命令 MUST 被拒绝。

#### Scenario: 事务提交
- **WHEN** 插件在事务内成功执行多条命令并提交
- **THEN** 所有修改以同一事务提交且事务进入已完成状态

#### Scenario: 事务中途失败并回滚
- **WHEN** 插件在事务内遇到失败后请求回滚或释放未提交事务
- **THEN** 宿主回滚未提交修改且后续不得复用该事务

#### Scenario: 操作被取消
- **WHEN** 调用方取消正在等待的数据库操作
- **THEN** Bridge 尽快终止等待并返回 `Cancelled`，不得把取消误报为未知数据库故障

### Requirement: SQLite 错误映射为稳定契约错误
宿主 MUST 将具体 SQLite 异常映射为 Bridge 稳定错误分类，至少覆盖 `ConstraintViolation`、`Busy`、`Locked`、`Corrupt`、`ReadOnly`、`PermissionDenied`、`InvalidPath`、`InvalidSql`、`Cancelled` 和 `Unknown`，不得向插件暴露具体提供程序异常类型。

#### Scenario: 唯一约束冲突
- **WHEN** 插件写入违反唯一约束的数据
- **THEN** Bridge 返回 `ConstraintViolation`，插件可按自身业务规则重新查询或重试

#### Scenario: 数据库锁定
- **WHEN** SQLite 报告 busy 或 locked
- **THEN** Bridge 分别返回 `Busy` 或 `Locked` 并保留可诊断的安全摘要

#### Scenario: 未知提供程序错误
- **WHEN** 具体错误无法映射到已知分类
- **THEN** Bridge 返回 `Unknown`，诊断不得包含 SQL 参数中的敏感值

### Requirement: SQLite 实现由宿主提供
插件使用 `storage.sqlite/v1` 时 MUST NOT 需要携带 SQLite 托管程序集或原生动态库。宿主 SHALL 提供唯一受控的 SQLite 实现并阻止插件通过 Bridge 注入或替换提供程序。

#### Scenario: 插件包仅包含业务程序集和负载
- **WHEN** 使用 SQLite Bridge 的插件被构建和发布
- **THEN** 插件目录不包含 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*` 或平台 `e_sqlite3` 文件仍可正常访问数据库

#### Scenario: 插件携带自有 SQLite 实现
- **WHEN** 插件包尝试以 Bridge 依赖方式覆盖宿主 SQLite 提供程序
- **THEN** Runtime 不使用该实现且返回安全诊断

### Requirement: Logging Bridge 提供结构化安全日志
`logging/v1` SHALL 接受日志级别、消息模板和受限结构化字段，由宿主映射到统一日志管线。插件 MUST NOT 记录 API Key、Authorization、自定义 Header 值、请求或响应正文、用户 Prompt、图片及工具调用参数，也不得逐 token 或逐流式 chunk 高频记录。

#### Scenario: 记录结构化业务事件
- **WHEN** 插件记录包含插件 id、事件名、状态码和耗时的完成事件
- **THEN** 宿主为该插件加上安全作用域并写入统一日志管线

#### Scenario: 日志字段包含敏感值
- **WHEN** 日志调用包含被策略识别为凭据或正文的字段
- **THEN** 宿主拒绝或净化该字段并记录不含原值的安全诊断

### Requirement: Notifications Bridge 提供宿主管控的即时反馈
`notifications/v1` SHALL 允许插件提交安全摘要和 `Success`、`Info`、`Warning`、`Error` 级别，由宿主映射到统一 Toast 体验。插件不得接触窗口、ViewModel 或导航状态。

#### Scenario: 插件显示成功通知
- **WHEN** 插件提交不含敏感数据的 Success 摘要
- **THEN** 宿主通过现有 Toast 机制显示并按统一生命周期自动隐藏

#### Scenario: 通知包含敏感内容
- **WHEN** 插件提交 API Key、Authorization、请求正文或工具参数
- **THEN** 宿主拒绝显示原文并返回安全错误或净化后的反馈

### Requirement: Bridge 调用按插件隔离
宿主 SHALL 将每个 Bridge 绑定到不可变插件 id，并以该身份执行能力授权、数据路径、日志作用域和通知来源归属。插件不得通过调用参数冒充另一插件。

#### Scenario: 插件尝试指定其他插件 id
- **WHEN** 插件在数据库路径、日志字段或通知来源中尝试覆盖绑定身份
- **THEN** 宿主仍使用初始化时绑定的插件 id，并拒绝越权目标

#### Scenario: 单插件 Bridge 故障
- **WHEN** 一个插件的 Bridge 调用失败
- **THEN** 故障按该调用和既有 Extension 失败策略处理，不破坏其他插件的 Bridge 实例

### Requirement: Credential Protection 原 Vault 保持兼容
Credential Protection 迁移到 SQLite Bridge 后 MUST 继续使用既有 `credential-tokens.db`、`CredentialTokens` 表和 DPAPI 密文，不得创建第二个 Vault、重建数据或使既有 placeholder 失效。

#### Scenario: 升级后解析历史 placeholder
- **WHEN** 用户升级前已存在 token 映射且升级后请求恢复历史 placeholder
- **THEN** Credential Protection 通过 Bridge 精确读取原记录并恢复相同 DPAPI 保护值

#### Scenario: 初始化已有 Vault
- **WHEN** `credential-tokens.db` 已存在并包含既有表结构
- **THEN** 插件直接复用该数据库，不执行数据迁移或创建另一个数据库文件

#### Scenario: 唯一约束冲突保持原语义
- **WHEN** 并发创建映射触发 Token 或 OriginalHash 唯一约束冲突
- **THEN** 插件通过 `ConstraintViolation` 识别冲突、结束失败事务并重新精确查询既有映射

#### Scenario: Vault 访问失败时安全失败
- **WHEN** 数据库损坏、无法解密或 Bridge 返回不可恢复错误
- **THEN** Credential Protection 保持既有 fail closed 行为，不放行可能含明文凭据的数据

### Requirement: 事务归属与实例生命周期明确
数据库句柄 SHALL 区分非事务命令与活动事务命令，不得将非事务调用隐式加入其他调用的活动事务。句柄在其所属实例实际退休并释放后 MUST 拒绝复用；普通禁用 MUST NOT 提前销毁 Credential Protection 的历史解析资源。

#### Scenario: 非事务命令遇到活动事务
- **WHEN** 同一数据库句柄上存在未结束事务且其他调用提交非事务命令
- **THEN** Bridge 不让该命令混入活动事务，按受控串行或稳定错误处理

#### Scenario: 已退休句柄被复用
- **WHEN** 插件旧实例已排空在途调用并释放而代码继续使用其数据库或事务句柄
- **THEN** Bridge 拒绝调用且不访问或重建数据库

#### Scenario: 暂停主动保护后解析历史引用
- **WHEN** 用户普通禁用 Credential Protection 而历史 placeholder 仍需解析
- **THEN** 历史解析所需的 Bridge 保持可用，且不与实际实例退休混淆
